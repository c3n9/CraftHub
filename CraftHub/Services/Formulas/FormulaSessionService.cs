using System;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Addressing;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Functions;
using CraftHub.Formulas.Graph;
using CraftHub.Formulas.Parsing;
using CraftHub.Formulas.Sidecar;
using CraftHub.Formulas.Values;
using CraftHub.Helpers;

namespace CraftHub.Services.Formulas;

/// <summary>Snapshot of one cell's text+kind (plus its sidecar error state, if any), for undo/redo
/// and for reporting what a recalculation changed. Capturing <see cref="ErrorState"/> alongside the
/// value matters: undoing a formula edit must bring back whatever error marker (or lack of one) the
/// cell had before, not just its text — otherwise a cell could show a stale <c>null</c> with no
/// tooltip explaining why, or lose a real error explanation it previously had.</summary>
public sealed record CellSnapshot(int RowIndex, string ColumnKey, string Text, CellKind Kind, CellState? ErrorState);

/// <summary>Before/after of one formula assignment — everything <c>SetCellFormulaAction</c> needs
/// to replay in either direction without calling back into <see cref="FormulaSessionService"/>'s
/// parsing/evaluation machinery a second time.</summary>
public sealed record FormulaChangeSet(
    int RowIndex,
    string ColumnKey,
    string? OldFormula,
    string? NewFormula,
    IReadOnlyList<CellSnapshot> OldCells,
    IReadOnlyList<CellSnapshot> NewCells);

/// <summary>Before/after of one whole-column formula assignment. Carries
/// <see cref="ClearedCellOverrides"/> as well as the cells, because applying a column formula is
/// the one formula operation that removes *other* formulas as a side effect — undoing it has to
/// put those per-cell overrides back, not just restore the column's own template.</summary>
public sealed record ColumnFormulaChangeSet(
    string ColumnKey,
    string? OldColumnFormula,
    string? NewColumnFormula,
    IReadOnlyDictionary<string, FormulaEntry> ClearedCellOverrides,
    IReadOnlyList<string> ClearedExclusions,
    IReadOnlyList<CellSnapshot> OldCells,
    IReadOnlyList<CellSnapshot> NewCells);

/// <summary>Before/after of taking one cell out of its column's formula. The value and the opt-out
/// travel together so undo restores both — putting the cell back under the template and letting it
/// recompute, rather than leaving a stranded value nothing owns.</summary>
public sealed record CellExclusionChangeSet(
    int RowIndex,
    string ColumnKey,
    string Path,
    CellSnapshot Before,
    CellSnapshot After);

public enum SidecarLoadOutcome { Absent, Clean, HashMismatch, Corrupt }

public sealed record FormulaLoadResult(SidecarLoadOutcome Outcome, string? CorruptBackupPath, string? CorruptReason);

/// <summary>
/// Owns one workspace's formula state: the in-memory <see cref="FormulaSidecar"/>, the dependency
/// graph, and every operation that touches both together (set/remove a formula, recalculate,
/// detach, persist). One instance per <c>WorkspaceViewModel</c> — like <c>UndoRedoService</c>, it's
/// created directly by the workspace rather than resolved through DI, and holds a reference to the
/// SAME live <see cref="DynamicDataRow"/>/<see cref="JsonPropertyDefinition"/> collections the
/// workspace owns (not copies), since those collections are stable for the tab's lifetime.
/// </summary>
public sealed class FormulaSessionService
{
    private static readonly FunctionRegistry Functions = FunctionRegistry.CreateStandard();

    private readonly IReadOnlyList<DynamicDataRow> _rows;
    private readonly IReadOnlyList<JsonPropertyDefinition> _properties;
    private readonly A1Translator _translator = new();
    private readonly StorageFormConverter _storageConverter = new();
    private readonly Evaluator _evaluator = new();
    private readonly DependencyGraph<string> _graph = new();

    /// <summary>Only set for a top-level session — a nested (per-cell) session runs without one and
    /// therefore does not itself recurse into deeper Object/Array cells. Used to parse an
    /// Object/Array cell's JSON into a sub-table so formulas stored against a path *inside* that
    /// cell (<c>$[3].person.full</c>) can be recomputed from the main recalculation.</summary>
    private readonly Core.IJsonService? _jsonService;

    public FormulaSidecar Sidecar { get; private set; } = NewEmptySidecar();

    public bool HasAnyFormulas => Sidecar.HasAnyFormulas;

    /// <summary>How many Object/Array cells deep this session already is. Bounds
    /// <see cref="MaterializeNestedScopes"/>'s recursion into cells-inside-cells, which is
    /// otherwise limited only by how deeply the user nested their JSON.</summary>
    private readonly int _nestingDepth;

    private const int MaxNestingDepth = 4;

    public FormulaSessionService(IReadOnlyList<DynamicDataRow> rows, IReadOnlyList<JsonPropertyDefinition> properties,
        Core.IJsonService? jsonService = null, bool subscribeToChanges = true, int nestingDepth = 0)
    {
        _rows = rows;
        _properties = properties;
        _jsonService = jsonService;
        _nestingDepth = nestingDepth;
        if (subscribeToChanges) SubscribeToStructuralChanges();
    }

    private static FormulaSidecar NewEmptySidecar() => new()
    {
        Target = new TargetInfo("", "", TargetHash.HashInputId, DateTime.UtcNow)
    };

    // -----------------------------------------------------------------------
    //  Loading
    // -----------------------------------------------------------------------

    /// <summary>Loads the sidecar for <paramref name="mainPath"/> (if any), rebuilds the dependency
    /// graph from it, and checks <paramref name="canonicalMainJson"/> against its stored hash. Does
    /// NOT recalculate on a mismatch — see <see cref="FullRecalculate"/>, called by the workspace
    /// once it knows the user's chosen policy for that case.</summary>
    public async Task<FormulaLoadResult> LoadAsync(string mainPath, string canonicalMainJson)
    {
        // Whatever was stashed for "undo of a column removal" belongs to the document being
        // replaced, and its column names could collide with the incoming one's.
        _formulasDroppedWithColumn.Clear();

        var result = await SidecarFileIO.LoadAsync(mainPath);
        switch (result)
        {
            case SidecarLoadResult.Absent:
                Sidecar = NewEmptySidecar();
                _graph.Clear();
                return new FormulaLoadResult(SidecarLoadOutcome.Absent, null, null);

            case SidecarLoadResult.Corrupt corrupt:
                Sidecar = NewEmptySidecar();
                _graph.Clear();
                return new FormulaLoadResult(SidecarLoadOutcome.Corrupt, corrupt.BackupPath, corrupt.Reason);

            case SidecarLoadResult.Loaded loaded:
                Sidecar = loaded.Sidecar;
                RebuildGraph();
                var outcome = TargetHash.Matches(canonicalMainJson, Sidecar.Target.Hash)
                    ? SidecarLoadOutcome.Clean
                    : SidecarLoadOutcome.HashMismatch;
                return new FormulaLoadResult(outcome, null, null);
        }
        throw new InvalidOperationException("Unreachable.");
    }

    /// <summary>Adopts a sidecar that came from somewhere other than a <c>.formulas.json</c> file —
    /// currently a <c>.crhb</c> bundle, which carries its formulas inside itself. Recalculates
    /// rather than trusting the bundled values: the data and the formulas were written together, so
    /// they should agree, and if they don't the formulas are the definition and the values are the
    /// stale copy.</summary>
    public void AdoptSidecar(FormulaSidecar sidecar)
    {
        _formulasDroppedWithColumn.Clear();
        Sidecar = sidecar;
        RebuildGraph();
        if (HasAnyFormulas) FullRecalculate();
    }

    /// <summary>Reconstructs the whole dependency graph from the sidecar — the graph holds no
    /// information the sidecar doesn't, so this is always safe and is how every operation too
    /// broad to patch edge-by-edge (a column formula, a structural row/column change, undoing a
    /// detach) resyncs. Clears first: without that, a path whose formula has just been *removed*
    /// would keep its old edges and go on being pulled into cycle detection and recalculation
    /// order as a node that no longer computes anything.</summary>
    private void RebuildGraph()
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        _graph.Clear();
        // Any structural or formula change can move a base path or alter what a nested scope
        // computes; drop the memoized round-trips so the next materialization is authoritative.
        _nestedScopeCache.Clear();

        foreach (var (path, entry) in Sidecar.CellFormulas)
        {
            if (!WorkspacePathCodec.TryTargetCell(path, out var rowIndex, out var columnKey)) continue;
            RegisterDependencies(path, entry.Formula, new CellAddress(rowIndex, columnKey), shape);
        }

        foreach (var (columnKey, entry) in Sidecar.ColumnFormulas)
        {
            for (var row = 0; row < _rows.Count; row++)
            {
                var path = shape.PathForCell(row, columnKey);
                if (path is null) continue;
                var pathText = path.ToCanonicalString();
                // Skips a cell with its own formula (that one is registered above) and a cell that
                // opted out of the template entirely.
                if (EffectiveFormulaFor(pathText, columnKey) != entry.Formula) continue;
                RegisterDependencies(pathText, entry.Formula, new CellAddress(row, columnKey), shape);
            }
        }
    }

    private void RegisterDependencies(string targetPath, string formulaText, CellAddress owner, WorkspaceTableShape shape)
    {
        List<string> deps;
        try
        {
            var ast = FormulaParser.ParseFormula(formulaText);
            deps = new List<string>();
            CollectReferencePaths(ast, owner, shape, deps);
        }
        catch (FormulaParseException)
        {
            deps = new List<string>();
        }
        _graph.SetDependencies(targetPath, deps);
    }

    private void CollectReferencePaths(FormulaAst node, CellAddress owner, WorkspaceTableShape shape, List<string> into)
    {
        switch (node)
        {
            case CellRefSyntax or RangeRefSyntax or ColumnBandSyntax or RowBandSyntax
                or ColumnRefSyntax or CurrentColumnRefSyntax or JsonPathSyntax:
                switch (_translator.Resolve(node, shape, owner))
                {
                    case ReferenceResolution.Single s: into.Add(s.Path.ToCanonicalString()); break;
                    case ReferenceResolution.Multiple m: into.AddRange(m.Paths.Select(p => p.ToCanonicalString())); break;
                }
                break;
            case UnaryExpr u: CollectReferencePaths(u.Operand, owner, shape, into); break;
            case PercentExpr p: CollectReferencePaths(p.Operand, owner, shape, into); break;
            case BinaryExpr b:
                CollectReferencePaths(b.Left, owner, shape, into);
                CollectReferencePaths(b.Right, owner, shape, into);
                break;
            case CallExpr c:
                foreach (var arg in c.Arguments) CollectReferencePaths(arg, owner, shape, into);
                break;
        }
    }

    // -----------------------------------------------------------------------
    //  Setting / removing formulas
    // -----------------------------------------------------------------------

    /// <summary>Parses and stores a formula typed in A1 form for one cell, recalculates it and
    /// everyone downstream, and writes the results into the grid. Returns null (with
    /// <paramref name="error"/> set) if the text doesn't parse or targets an unusable column.</summary>
    public FormulaChangeSet? TrySetCellFormula(int rowIndex, string columnKey, string a1FormulaText, out string? error)
    {
        error = null;

        var shape = new WorkspaceTableShape(_rows, _properties);
        var targetPath = shape.PathForCell(rowIndex, columnKey);
        if (targetPath is null)
        {
            error = Localizer.Get("FormulaUnknownColumn");
            return null;
        }

        FormulaAst ast;
        try
        {
            ast = FormulaParser.ParseFormula(a1FormulaText);
        }
        catch (FormulaParseException ex)
        {
            error = ex.Message;
            return null;
        }

        var owner = new CellAddress(rowIndex, columnKey);
        var storageText = "=" + AstPrinter.Print(_storageConverter.ToStorageForm(ast, shape, owner));
        var pathText = targetPath.ToCanonicalString();

        var oldFormula = Sidecar.CellFormulas.TryGetValue(pathText, out var oldEntry) ? oldEntry.Formula : null;
        var affectedBefore = AffectedPaths(new[] { pathText });
        var oldCells = CaptureCells(affectedBefore);

        Sidecar.CellFormulas[pathText] = new FormulaEntry(storageText);
        RegisterDependencies(pathText, storageText, owner, shape);

        var newCells = RecalculateFrom(new[] { pathText }, shape);

        return new FormulaChangeSet(rowIndex, columnKey, oldFormula, storageText, oldCells, newCells);
    }

    /// <summary>Removes a cell's own formula override. If the column has a <c>columnFormulas</c>
    /// template, the cell falls back to it (and is recalculated accordingly); otherwise the cell's
    /// current value is left exactly as it was — becoming a plain static value.</summary>
    public FormulaChangeSet? TryRemoveCellFormula(int rowIndex, string columnKey)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var targetPath = shape.PathForCell(rowIndex, columnKey);
        if (targetPath is null) return null;
        var pathText = targetPath.ToCanonicalString();

        if (!Sidecar.CellFormulas.TryGetValue(pathText, out var oldEntry)) return null; // nothing to remove

        var affectedBefore = AffectedPaths(new[] { pathText });
        var oldCells = CaptureCells(affectedBefore);

        Sidecar.CellFormulas.Remove(pathText);
        Sidecar.State.Remove(pathText);

        if (Sidecar.ColumnFormulas.TryGetValue(columnKey, out var colEntry))
            RegisterDependencies(pathText, colEntry.Formula, new CellAddress(rowIndex, columnKey), shape);
        else
            _graph.RemoveNode(pathText);

        var newCells = RecalculateFrom(new[] { pathText }, shape);
        return new FormulaChangeSet(rowIndex, columnKey, oldEntry.Formula, null, oldCells, newCells);
    }

    /// <summary>Copies <paramref name="sourceRowIndex"/>'s formula down to every row in
    /// <paramref name="targetRowIndices"/> — the Excel-style fill handle. Reuses the source's
    /// stored formula text verbatim for every target: a relative row offset already means
    /// "relative to whichever row this formula lives in" (see <c>StorageFormConverter</c>'s own
    /// doc comment), so it's already correct wherever it's copied to, and an absolute (<c>$</c>)
    /// reference stays pointing at the same row precisely because the text doesn't change either.
    /// Returns one <see cref="FormulaChangeSet"/> per row actually changed (skips
    /// <paramref name="sourceRowIndex"/> itself and any row with no usable source formula).</summary>
    public IReadOnlyList<FormulaChangeSet> FillDown(int sourceRowIndex, string columnKey, IReadOnlyList<int> targetRowIndices)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var sourcePathText = shape.PathForCell(sourceRowIndex, columnKey)?.ToCanonicalString();
        if (sourcePathText is null) return Array.Empty<FormulaChangeSet>();

        var formulaText = Sidecar.CellFormulas.TryGetValue(sourcePathText, out var cellEntry) ? cellEntry.Formula
            : Sidecar.ColumnFormulas.TryGetValue(columnKey, out var colEntry) ? colEntry.Formula
            : null;
        if (formulaText is null) return Array.Empty<FormulaChangeSet>();

        var results = new List<FormulaChangeSet>();
        foreach (var targetRow in targetRowIndices)
        {
            if (targetRow == sourceRowIndex) continue;

            var targetPath = shape.PathForCell(targetRow, columnKey);
            if (targetPath is null) continue;
            var targetPathText = targetPath.ToCanonicalString();

            var oldFormula = Sidecar.CellFormulas.TryGetValue(targetPathText, out var oldEntry) ? oldEntry.Formula : null;
            var oldCells = CaptureCells(AffectedPaths(new[] { targetPathText }));

            Sidecar.CellFormulas[targetPathText] = new FormulaEntry(formulaText);
            RegisterDependencies(targetPathText, formulaText, new CellAddress(targetRow, columnKey), shape);

            var newCells = RecalculateFrom(new[] { targetPathText }, shape);
            results.Add(new FormulaChangeSet(targetRow, columnKey, oldFormula, formulaText, oldCells, newCells));
        }
        return results;
    }

    // -----------------------------------------------------------------------
    //  Column formulas — one template that computes every row of a column
    // -----------------------------------------------------------------------

    /// <summary>Stores one formula as the template for an entire column, computes every row with
    /// it, and clears any per-cell overrides that column had. The formula text is kept in
    /// row-relative storage form (see <see cref="StorageFormConverter"/>), which is what lets a
    /// single stored string mean "this row's price times this row's qty" in every row at once —
    /// and what makes a column formula automatically apply to rows added later, unlike a fill-down,
    /// which only ever touches the rows that existed when it ran.
    ///
    /// <paramref name="authoringRowIndex"/> is the row the user was looking at when they typed it:
    /// relative offsets are resolved against that row, so <c>=B5</c> typed in row 3 means "two rows
    /// below me" for every row, exactly as it would if it had been filled down from there.</summary>
    public ColumnFormulaChangeSet? TrySetColumnFormula(int authoringRowIndex, string columnKey, string a1FormulaText, out string? error)
    {
        error = null;

        var shape = new WorkspaceTableShape(_rows, _properties);
        var authoringRow = Math.Clamp(authoringRowIndex, 0, Math.Max(0, _rows.Count - 1));
        if (shape.PathForCell(authoringRow, columnKey) is null)
        {
            error = Localizer.Get("FormulaUnknownColumn");
            return null;
        }

        FormulaAst ast;
        try
        {
            ast = FormulaParser.ParseFormula(a1FormulaText);
        }
        catch (FormulaParseException ex)
        {
            error = ex.Message;
            return null;
        }

        var owner = new CellAddress(authoringRow, columnKey);
        var storageText = "=" + AstPrinter.Print(_storageConverter.ToStorageForm(ast, shape, owner));

        var columnPaths = ColumnPaths(columnKey, shape);
        var oldColumnFormula = Sidecar.ColumnFormulas.TryGetValue(columnKey, out var oldEntry) ? oldEntry.Formula : null;

        // A per-cell formula wins over the column's template (see RebuildGraph), so leaving those
        // in place would mean "apply to the whole column" visibly skipping some of the column.
        var clearedOverrides = new Dictionary<string, FormulaEntry>();
        foreach (var path in columnPaths)
            if (Sidecar.CellFormulas.TryGetValue(path, out var over))
                clearedOverrides[path] = over;

        // "Apply to the whole column" means the whole column, including cells that had previously
        // opted out by having a value typed into them.
        var clearedExclusions = columnPaths.Where(Sidecar.ExcludedCells.Contains).ToList();

        var oldCells = CaptureCells(AffectedPaths(columnPaths));

        Sidecar.ColumnFormulas[columnKey] = new FormulaEntry(storageText);
        foreach (var path in clearedOverrides.Keys) Sidecar.CellFormulas.Remove(path);
        foreach (var path in clearedExclusions) Sidecar.ExcludedCells.Remove(path);
        RebuildGraph();

        var newCells = RecalculateFrom(columnPaths, shape);
        return new ColumnFormulaChangeSet(columnKey, oldColumnFormula, storageText, clearedOverrides,
            clearedExclusions, oldCells, newCells);
    }

    /// <summary>Drops a column's template. Every cell keeps the value it last computed — that value
    /// IS the column's data (same reasoning as <see cref="DetachAll"/>) — so this is "stop
    /// recomputing this column", not "clear this column". Returns null if the column had no
    /// template to begin with.</summary>
    public ColumnFormulaChangeSet? TryRemoveColumnFormula(string columnKey)
    {
        if (!Sidecar.ColumnFormulas.TryGetValue(columnKey, out var oldEntry)) return null;

        var shape = new WorkspaceTableShape(_rows, _properties);
        var columnPaths = ColumnPaths(columnKey, shape);
        var oldCells = CaptureCells(AffectedPaths(columnPaths));

        Sidecar.ColumnFormulas.Remove(columnKey);
        foreach (var path in columnPaths)
            if (!Sidecar.CellFormulas.ContainsKey(path)) // a per-cell override, if any, survives
                Sidecar.State.Remove(path);
        RebuildGraph();

        var newCells = RecalculateFrom(columnPaths, shape);
        return new ColumnFormulaChangeSet(columnKey, oldEntry.Formula, null,
            new Dictionary<string, FormulaEntry>(), Array.Empty<string>(), oldCells, newCells);
    }

    /// <summary>Replays a <see cref="ColumnFormulaChangeSet"/> in either direction. Unlike the
    /// per-cell version this has to restore the cleared overrides too, since applying a column
    /// formula is the one operation that removes other formulas as a side effect.</summary>
    public void ApplyColumnChangeSet(ColumnFormulaChangeSet changeSet, bool redo)
    {
        var formula = redo ? changeSet.NewColumnFormula : changeSet.OldColumnFormula;
        var cells = redo ? changeSet.NewCells : changeSet.OldCells;

        if (formula is null) Sidecar.ColumnFormulas.Remove(changeSet.ColumnKey);
        else Sidecar.ColumnFormulas[changeSet.ColumnKey] = new FormulaEntry(formula);

        foreach (var (path, entry) in changeSet.ClearedCellOverrides)
        {
            if (redo) Sidecar.CellFormulas.Remove(path);
            else Sidecar.CellFormulas[path] = entry;
        }

        foreach (var path in changeSet.ClearedExclusions)
        {
            if (redo) Sidecar.ExcludedCells.Remove(path);
            else Sidecar.ExcludedCells.Add(path);
        }

        RebuildGraph();

        var shape = new WorkspaceTableShape(_rows, _properties);
        foreach (var cell in cells)
        {
            if (!IsLiveRow(cell.RowIndex)) continue;
            _rows[cell.RowIndex].SetCell(cell.ColumnKey, cell.Text, cell.Kind);
            var cellPath = shape.PathForCell(cell.RowIndex, cell.ColumnKey)?.ToCanonicalString();
            if (cellPath is null) continue;

            if (cell.ErrorState is { } state) Sidecar.State[cellPath] = state;
            else Sidecar.State.Remove(cellPath);
        }
    }

    public bool HasColumnFormula(string columnKey) => Sidecar.ColumnFormulas.ContainsKey(columnKey);

    /// <summary>Takes one cell out of its column's formula and puts <paramref name="newValue"/> in
    /// it — what typing a plain value into a computed cell means, as in Excel. Both halves have to
    /// happen together and be undone together: writing the value without recording the opt-out
    /// leaves the next recalculation free to overwrite it, which is precisely the bug this fixes.
    /// Returns null if the cell isn't computed by its column after all.</summary>
    public CellExclusionChangeSet? ExcludeCellFromColumnFormula(int rowIndex, string columnKey, string newValue)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var pathText = shape.PathForCell(rowIndex, columnKey)?.ToCanonicalString();
        if (pathText is null) return null;
        if (!Sidecar.ColumnFormulas.ContainsKey(columnKey)) return null;
        if (Sidecar.CellFormulas.ContainsKey(pathText)) return null;   // its own formula, a different case
        if (Sidecar.ExcludedCells.Contains(pathText)) return null;     // already opted out

        var row = _rows[rowIndex];
        var before = new CellSnapshot(rowIndex, columnKey, row[columnKey], row.GetKind(columnKey),
            Sidecar.State.TryGetValue(pathText, out var st) ? st : null);

        Sidecar.ExcludedCells.Add(pathText);
        Sidecar.State.Remove(pathText);
        RebuildGraph();

        row[columnKey] = newValue;
        var after = new CellSnapshot(rowIndex, columnKey, row[columnKey], row.GetKind(columnKey), null);

        // Anything that read this cell was reading a computed value that just changed.
        RecalculateFrom(new[] { pathText }, shape);

        return new CellExclusionChangeSet(rowIndex, columnKey, pathText, before, after);
    }

    public void ApplyExclusionChangeSet(CellExclusionChangeSet changeSet, bool redo)
    {
        if (redo) Sidecar.ExcludedCells.Add(changeSet.Path);
        else Sidecar.ExcludedCells.Remove(changeSet.Path);

        var cell = redo ? changeSet.After : changeSet.Before;
        if (IsLiveRow(cell.RowIndex))
        {
            _rows[cell.RowIndex].SetCell(cell.ColumnKey, cell.Text, cell.Kind);
            if (cell.ErrorState is { } state) Sidecar.State[changeSet.Path] = state;
            else Sidecar.State.Remove(changeSet.Path);
        }

        RebuildGraph();
        var shape = new WorkspaceTableShape(_rows, _properties);
        RecalculateFrom(new[] { changeSet.Path }, shape);
    }

    /// <summary>A column's template rendered in A1 form as it reads from
    /// <paramref name="viewingRowIndex"/> — what the "edit this column's formula" UI shows.</summary>
    public string? GetDisplayColumnFormula(string columnKey, int viewingRowIndex)
    {
        if (!Sidecar.ColumnFormulas.TryGetValue(columnKey, out var entry)) return null;

        var shape = new WorkspaceTableShape(_rows, _properties);
        var viewingRow = Math.Clamp(viewingRowIndex, 0, Math.Max(0, _rows.Count - 1));
        try
        {
            var ast = FormulaParser.ParseFormula(entry.Formula);
            return "=" + AstPrinter.Print(_storageConverter.ToDisplayForm(ast, shape, new CellAddress(viewingRow, columnKey)));
        }
        catch (FormulaParseException)
        {
            return entry.Formula;
        }
    }

    private List<string> ColumnPaths(string columnKey, WorkspaceTableShape shape)
    {
        var paths = new List<string>(_rows.Count);
        for (var row = 0; row < _rows.Count; row++)
            if (shape.PathForCell(row, columnKey) is { } path)
                paths.Add(path.ToCanonicalString());
        return paths;
    }

    /// <summary>Replays a previously captured change (Undo restores <see cref="FormulaChangeSet.OldFormula"/>
    /// and <see cref="FormulaChangeSet.OldCells"/>; Redo restores the New* side) without re-parsing
    /// or re-evaluating anything — the values were already computed once.</summary>
    public void ApplyChangeSet(FormulaChangeSet changeSet, bool redo)
    {
        var formula = redo ? changeSet.NewFormula : changeSet.OldFormula;
        var cells = redo ? changeSet.NewCells : changeSet.OldCells;
        var shape = new WorkspaceTableShape(_rows, _properties);
        var targetPath = shape.PathForCell(changeSet.RowIndex, changeSet.ColumnKey);
        var pathText = targetPath?.ToCanonicalString();

        if (pathText is not null)
        {
            if (formula is null)
            {
                Sidecar.CellFormulas.Remove(pathText);
                Sidecar.State.Remove(pathText);
                if (Sidecar.ColumnFormulas.TryGetValue(changeSet.ColumnKey, out var colEntry))
                    RegisterDependencies(pathText, colEntry.Formula, new CellAddress(changeSet.RowIndex, changeSet.ColumnKey), shape);
                else
                    _graph.RemoveNode(pathText);
            }
            else
            {
                Sidecar.CellFormulas[pathText] = new FormulaEntry(formula);
                RegisterDependencies(pathText, formula, new CellAddress(changeSet.RowIndex, changeSet.ColumnKey), shape);
            }
        }

        foreach (var cell in cells)
        {
            if (!IsLiveRow(cell.RowIndex)) continue;
            _rows[cell.RowIndex].SetCell(cell.ColumnKey, cell.Text, cell.Kind);
            var cellPath = shape.PathForCell(cell.RowIndex, cell.ColumnKey)?.ToCanonicalString();
            if (cellPath is null) continue;

            if (cell.ErrorState is { } state) Sidecar.State[cellPath] = state;
            else Sidecar.State.Remove(cellPath);
        }
    }

    // -----------------------------------------------------------------------
    //  Detach
    // -----------------------------------------------------------------------

    public sealed record DetachSnapshot(
        IReadOnlyDictionary<string, FormulaEntry> ColumnFormulas,
        IReadOnlyDictionary<string, FormulaEntry> CellFormulas,
        IReadOnlyDictionary<string, CellState> State);

    /// <summary>"Отсоединить формулы" — no cell values change (a formula's last computed value IS
    /// already the cell's value), just stop treating any path as a formula. Returns the removed
    /// state so an undo action can restore it.</summary>
    public DetachSnapshot DetachAll()
    {
        var snapshot = new DetachSnapshot(
            new Dictionary<string, FormulaEntry>(Sidecar.ColumnFormulas),
            new Dictionary<string, FormulaEntry>(Sidecar.CellFormulas),
            new Dictionary<string, CellState>(Sidecar.State));

        Sidecar.DetachAll();
        _graph.Clear();
        return snapshot;
    }

    public void RestoreFromDetach(DetachSnapshot snapshot)
    {
        foreach (var (k, v) in snapshot.ColumnFormulas) Sidecar.ColumnFormulas[k] = v;
        foreach (var (k, v) in snapshot.CellFormulas) Sidecar.CellFormulas[k] = v;
        foreach (var (k, v) in snapshot.State) Sidecar.State[k] = v;
        RebuildGraph();
    }

    // -----------------------------------------------------------------------
    //  Recalculation
    // -----------------------------------------------------------------------

    /// <summary>Recomputes every formula cell from scratch — used after import/load when the
    /// document doesn't match <c>target.hash</c> and the user chose to trust the formulas over
    /// whatever's currently on disk.</summary>
    public IReadOnlyList<CellSnapshot> FullRecalculate()
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var allTargets = Sidecar.CellFormulas.Keys
            .Concat(Sidecar.ColumnFormulas.Keys.SelectMany(col => Enumerable.Range(0, _rows.Count)
                .Select(row => shape.PathForCell(row, col)?.ToCanonicalString())
                .Where(p => p is not null)!))
            .Distinct()
            .ToList();

        var wasRecalculating = _recalculating;
        _recalculating = true;
        try
        {
            var result = RecalculateFrom(allTargets, shape);
            MaterializeNestedScopes();
            return result;
        }
        finally
        {
            _recalculating = wasRecalculating;
        }
    }

    private HashSet<string> AffectedPaths(IEnumerable<string> changed)
    {
        var set = new HashSet<string>(changed);
        set.UnionWith(_graph.GetAllDependents(changed));
        return set;
    }

    private List<CellSnapshot> RecalculateFrom(IEnumerable<string> changedPaths, WorkspaceTableShape shape)
    {
        var wasRecalculating = _recalculating;
        _recalculating = true;
        try
        {
            return RecalculateFromCore(changedPaths, shape);
        }
        finally
        {
            _recalculating = wasRecalculating;
        }
    }

    private List<CellSnapshot> RecalculateFromCore(IEnumerable<string> changedPaths, WorkspaceTableShape shape)
    {
        var affected = AffectedPaths(changedPaths);

        var cyclic = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var path in affected)
        {
            if (cyclic.ContainsKey(path)) continue;
            if (_graph.TryFindCycle(path, out var chain))
                foreach (var node in chain.Distinct())
                    cyclic[node] = chain;
        }

        var results = new List<CellSnapshot>();
        var valueSource = new WorkspaceValueSource(shape, _rows, _properties);
        var limits = ToEvalLimits();

        foreach (var path in cyclic.Keys)
        {
            if (!WorkspacePathCodec.TryTargetCell(path, out var rowIndex, out var columnKey)) continue;
            if (!IsLiveRow(rowIndex)) continue;
            _rows[rowIndex].SetCell(columnKey, "", CellKind.Null);
            var chainText = string.Join(" → ", cyclic[path]);
            Sidecar.State[path] = new CellState(new FormulaError(FormulaErrorCode.Cycle, "").Symbol,
                Localizer.Get("FormulaCycleMessage", chainText), DateTime.UtcNow);
            results.Add(CaptureCell(rowIndex, columnKey, path));
        }

        var order = _graph.TopologicalOrder(affected.Except(cyclic.Keys));
        foreach (var path in order)
        {
            if (!WorkspacePathCodec.TryTargetCell(path, out var rowIndex, out var columnKey)) continue;
            if (!IsLiveRow(rowIndex)) continue;
            var type = _properties.FirstOrDefault(p => p.Name == columnKey)?.FieldType;
            if (type is null) continue;

            var formulaText = EffectiveFormulaFor(path, columnKey);
            if (formulaText is null) continue;

            FormulaValue result;
            try
            {
                var ast = FormulaParser.ParseFormula(formulaText);
                var context = new EvalContext
                {
                    CurrentCell = new CellAddress(rowIndex, columnKey),
                    Values = valueSource,
                    Functions = Functions,
                    Limits = limits
                };
                result = _evaluator.Evaluate(ast, context);
            }
            catch (FormulaParseException)
            {
                result = FormulaValue.Of(FormulaErrorCode.Value, Localizer.Get("FormulaCorruptStoredText"));
            }

            ApplyResult(rowIndex, columnKey, type.Value, result, path);
            results.Add(CaptureCell(rowIndex, columnKey, path));
        }

        return results;
    }

    private void ApplyResult(int rowIndex, string columnKey, JsonFieldType type, FormulaValue result, string pathText)
    {
        var row = _rows[rowIndex];

        if (result.IsError)
        {
            row.SetCell(columnKey, "", CellKind.Null);
            Sidecar.State[pathText] = new CellState(result.AsError.Symbol, result.AsError.Message, DateTime.UtcNow);
            return;
        }

        if (FormulaResultWriter.TryConvert(result, type, out var text, out var kind, out var typeError))
        {
            row.SetCell(columnKey, text, kind);
            Sidecar.State.Remove(pathText);
        }
        else
        {
            row.SetCell(columnKey, "", CellKind.Null);
            Sidecar.State[pathText] = new CellState(typeError.Symbol, typeError.Message, DateTime.UtcNow);
        }
    }

    private EvalLimits ToEvalLimits() => new()
    {
        MaxDepth = Sidecar.Options.Limits.MaxDepth,
        MaxRangeCells = Sidecar.Options.Limits.MaxRangeCells,
        Timeout = TimeSpan.FromMilliseconds(Sidecar.Options.Limits.FormulaTimeoutMs)
    };

    /// <summary>Whether a row index the sidecar names actually exists right now. A sidecar is a
    /// separate file that can legitimately describe more rows than the document currently has —
    /// the document may have been edited outside the app, which is exactly the
    /// <see cref="SidecarLoadOutcome.HashMismatch"/> case that then asks for a recalculation. Every
    /// row access driven by a stored path goes through this rather than trusting the index.</summary>
    private bool IsLiveRow(int rowIndex) => rowIndex >= 0 && rowIndex < _rows.Count;

    private CellSnapshot CaptureCell(int rowIndex, string columnKey, string pathText)
    {
        var row = _rows[rowIndex];
        var errorState = Sidecar.State.TryGetValue(pathText, out var state) ? state : null;
        return new CellSnapshot(rowIndex, columnKey, row[columnKey], row.GetKind(columnKey), errorState);
    }

    private List<CellSnapshot> CaptureCells(IEnumerable<string> paths)
    {
        var list = new List<CellSnapshot>();
        foreach (var p in paths)
            if (WorkspacePathCodec.TryTargetCell(p, out var r, out var c) && IsLiveRow(r))
                list.Add(CaptureCell(r, c, p));
        return list;
    }

    // -----------------------------------------------------------------------
    //  Formula lookup (for UI: markers, tooltips, F2 display)
    // -----------------------------------------------------------------------

    /// <summary>The A1-form formula text for a cell (its own override, or the column's template
    /// re-displayed relative to this row), or null if the cell isn't a formula.</summary>
    public string? GetDisplayFormula(int rowIndex, string columnKey)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var pathText = shape.PathForCell(rowIndex, columnKey)?.ToCanonicalString();
        if (pathText is null) return null;

        var storageFormula = EffectiveFormulaFor(pathText, columnKey);
        if (storageFormula is null) return null;

        try
        {
            var ast = FormulaParser.ParseFormula(storageFormula);
            var displayAst = _storageConverter.ToDisplayForm(ast, shape, new CellAddress(rowIndex, columnKey));
            return "=" + AstPrinter.Print(displayAst);
        }
        catch (FormulaParseException)
        {
            return storageFormula;
        }
    }

    /// <summary>The formula that computes this cell, or null if nothing does. The single place
    /// that answers the question, so the three rules can't drift apart at the half-dozen call
    /// sites that ask: a cell's own formula wins; failing that the column's template applies;
    /// unless the cell has opted out of that template (see <see cref="FormulaSidecar.ExcludedCells"/>),
    /// in which case it holds a plain value and nothing computes it.</summary>
    private string? EffectiveFormulaFor(string pathText, string columnKey)
    {
        if (Sidecar.CellFormulas.TryGetValue(pathText, out var cellEntry)) return cellEntry.Formula;
        if (Sidecar.ExcludedCells.Contains(pathText)) return null;
        return Sidecar.ColumnFormulas.TryGetValue(columnKey, out var colEntry) ? colEntry.Formula : null;
    }

    /// <summary>True when this cell has a formula of its own, as opposed to being computed by its
    /// column's template — the two look identical in the grid but behave differently under
    /// fill-down and "stop computing this column", so the UI has to tell them apart.</summary>
    public bool CellHasOwnFormula(int rowIndex, string columnKey)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var pathText = shape.PathForCell(rowIndex, columnKey)?.ToCanonicalString();
        return pathText is not null && Sidecar.CellFormulas.ContainsKey(pathText);
    }

    public bool IsFormulaCell(int rowIndex, string columnKey)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var pathText = shape.PathForCell(rowIndex, columnKey)?.ToCanonicalString();
        return pathText is not null && EffectiveFormulaFor(pathText, columnKey) is not null;
    }

    public CellState? GetErrorState(int rowIndex, string columnKey)
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var pathText = shape.PathForCell(rowIndex, columnKey)?.ToCanonicalString();
        return pathText is not null && Sidecar.State.TryGetValue(pathText, out var state) ? state : null;
    }

    // -----------------------------------------------------------------------
    //  Structural sync — rows and columns moving under the formulas
    // -----------------------------------------------------------------------
    //
    //  Driven entirely by the live collections' own change notifications rather than by calls from
    //  each row/column command. That's deliberate: a cell formula is stored against a row INDEX
    //  ($[3].total), so inserting a row above it silently makes every such formula point one row
    //  off — and the paths that mutate Rows are many (the row commands, paste, import, and every
    //  undo/redo of those, each of which mutates the collection directly rather than re-running the
    //  command). Hooking the collection catches all of them, including the undo direction, with no
    //  per-call-site plumbing to keep in sync. Renames arrive the same way, via each column's own
    //  PropertyChanging/PropertyChanged pair, which is the only place the old name still exists.

    private string? _renamingColumnFrom;

    /// <summary>Formulas removed along with a column, keyed by that column's name. A column removal
    /// is undoable, and undo puts the column back by re-adding it to <c>Properties</c> — so keeping
    /// what was dropped means the column's formulas come back with it, instead of the user losing
    /// them permanently to an action the UI told them was reversible.</summary>
    private readonly Dictionary<string, DetachSnapshot> _formulasDroppedWithColumn = new();

    private void SubscribeToStructuralChanges()
    {
        if (_rows is INotifyCollectionChanged rowEvents)
            rowEvents.CollectionChanged += OnRowsCollectionChanged;

        if (_properties is INotifyCollectionChanged columnEvents)
            columnEvents.CollectionChanged += OnPropertiesCollectionChanged;

        foreach (var property in _properties) WatchForRename(property);
        foreach (var row in _rows) WatchForValueChanges(row);
    }

    // -----------------------------------------------------------------------
    //  Value changes — a formula has to notice when what it READS changes
    // -----------------------------------------------------------------------
    //
    //  Editing a plain cell that a formula reads has to recompute that formula. Without this,
    //  writing "=@[a]*2" and then filling in the a column leaves the result blank forever, because
    //  nothing ever asked the engine to run again.
    //
    //  Driven off each row's own PropertyChanged rather than from the edit commands, for the same
    //  reason the structural sync is: the paths that write a value are many (cell edit and its
    //  undo, paste, replace-all, the JSON editor, recalculation itself) and hooking them one by one
    //  would miss some and double-apply on others.
    //
    //  DynamicDataRow reports "something on this row changed" without naming the column, so there
    //  is nothing to recalculate *from* — hence a full pass. Two things keep that affordable: it is
    //  skipped entirely when the document has no formulas, and it is coalesced onto the dispatcher,
    //  so a paste that writes a thousand cells still recalculates once.

    private bool _recalculating;
    private bool _recalcQueued;

    private void WatchForValueChanges(DynamicDataRow row) => row.PropertyChanged += OnRowValueChanged;

    private void StopWatchingForValueChanges(DynamicDataRow row) => row.PropertyChanged -= OnRowValueChanged;

    private void OnRowValueChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Recalculation writes cells itself; without this it would retrigger itself forever.
        if (_recalculating || _recalcQueued || !HasAnyFormulas) return;

        _recalcQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _recalcQueued = false;
            RecalculateAll();
        }, DispatcherPriority.Background);
    }

    /// <summary>Raised after a recalculation that actually changed something. The workspace turns
    /// this into a grid refresh: values re-render through their bindings on their own, but a cell
    /// that has just stopped erroring keeps its red marker and stale tooltip until the row is
    /// rebuilt, because those are decided when the cell template runs.</summary>
    public event EventHandler? Recalculated;

    /// <summary>Recomputes every formula and reports whether any cell actually came out different,
    /// so the caller can skip refreshing the grid when nothing moved.</summary>
    public bool RecalculateAll()
    {
        if (!HasAnyFormulas) return false;

        var before = SnapshotFormulaCells();
        FullRecalculate();
        var changed = !SnapshotFormulaCells().SequenceEqual(before);
        if (changed) Recalculated?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    private List<string> SnapshotFormulaCells()
    {
        var shape = new WorkspaceTableShape(_rows, _properties);
        var snapshot = new List<string>();
        for (var row = 0; row < _rows.Count; row++)
            foreach (var property in _properties)
            {
                var path = shape.PathForCell(row, property.Name)?.ToCanonicalString();
                if (path is null) continue;
                if (!Sidecar.CellFormulas.ContainsKey(path) && !Sidecar.ColumnFormulas.ContainsKey(property.Name)) continue;
                var state = Sidecar.State.TryGetValue(path, out var st) ? st.ErrorCode : "";
                snapshot.Add($"{path}\u001F{_rows[row][property.Name]}\u001F{state}");
            }

        // An Object/Array cell that hosts formulas at paths *inside* it has no top-level formula
        // of its own, so the loop above never sees it — but MaterializeNestedScopes rewrites its
        // JSON, and RecalculateAll must notice that to refresh the grid.
        foreach (var (basePath, cellText) in NestedScopeCells())
            snapshot.Add($"{basePath}|{cellText}");

        return snapshot;
    }

    private void WatchForRename(JsonPropertyDefinition property)
    {
        property.PropertyChanging += OnColumnPropertyChanging;
        property.PropertyChanged += OnColumnPropertyChanged;
    }

    private void StopWatchingForRename(JsonPropertyDefinition property)
    {
        property.PropertyChanging -= OnColumnPropertyChanging;
        property.PropertyChanged -= OnColumnPropertyChanged;
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { Count: > 0 } && e.NewStartingIndex >= 0:
                foreach (var added in e.NewItems.OfType<DynamicDataRow>()) WatchForValueChanges(added);
                // Once per inserted row at the SAME index: each call shifts everything at or below
                // that index down by one, which composes to the n-row shift the batch needs.
                for (var i = 0; i < e.NewItems.Count; i++)
                    SidecarStructuralSync.OnRowInserted(Sidecar, e.NewStartingIndex);
                RebuildGraph();
                // New rows in a column that has a template must actually be computed — that's the
                // whole difference between a column formula and a one-off fill down.
                RecalculateRows(e.NewStartingIndex, e.NewItems.Count);
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is { Count: > 0 } && e.OldStartingIndex >= 0:
                foreach (var removed in e.OldItems.OfType<DynamicDataRow>()) StopWatchingForValueChanges(removed);
                for (var i = 0; i < e.OldItems.Count; i++)
                    SidecarStructuralSync.OnRowRemoved(Sidecar, e.OldStartingIndex);
                RebuildGraph();
                // Whatever read those rows is now reading different data (or nothing). Which
                // formulas that is can't be worked out from the dropped paths alone — removal
                // renumbers every path below it, so a dependent list gathered before the shift
                // names cells that no longer exist under those names. Hence a full pass. It costs
                // one evaluation per formula cell, and multi-row deletions arrive as one event per
                // row, so a bulk delete on a document with a formula in every row is the case to
                // watch if this ever needs to get faster.
                if (HasAnyFormulas) FullRecalculate();
                break;

            default:
                // Reset (a bulk AddRange from import/replace), Move, Replace — no index arithmetic
                // that would be safe to guess at, so just resync the graph with what's there now.
                foreach (var row in _rows) { StopWatchingForValueChanges(row); WatchForValueChanges(row); }
                RebuildGraph();
                break;
        }
    }

    private void OnPropertiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var property in _properties) WatchForRename(property);
            RebuildGraph();
            return;
        }

        foreach (var removed in e.OldItems?.OfType<JsonPropertyDefinition>() ?? Enumerable.Empty<JsonPropertyDefinition>())
        {
            StopWatchingForRename(removed);
            DropFormulasForColumn(removed.Name);
        }

        foreach (var added in e.NewItems?.OfType<JsonPropertyDefinition>() ?? Enumerable.Empty<JsonPropertyDefinition>())
        {
            WatchForRename(added);
            RestoreFormulasForColumn(added.Name);
        }
    }

    private void DropFormulasForColumn(string columnKey)
    {
        var columnFormulas = new Dictionary<string, FormulaEntry>();
        if (Sidecar.ColumnFormulas.TryGetValue(columnKey, out var template))
            columnFormulas[columnKey] = template;

        // Matches both the column's own cells and any formula stored against a path INSIDE an
        // Object/Array cell of it ($[3].person.full) — those go with the column too.
        bool BelongsToColumn(string key) =>
            key == columnKey || key.StartsWith(columnKey + JsonFieldMapping.PathSeparator, StringComparison.Ordinal);

        var cellFormulas = new Dictionary<string, FormulaEntry>();
        foreach (var (path, entry) in Sidecar.CellFormulas)
            if (WorkspacePathCodec.TryTargetCell(path, out _, out var key) && BelongsToColumn(key))
                cellFormulas[path] = entry;

        var state = new Dictionary<string, CellState>();
        foreach (var (path, entry) in Sidecar.State)
            if (WorkspacePathCodec.TryTargetCell(path, out _, out var key) && BelongsToColumn(key))
                state[path] = entry;

        if (columnFormulas.Count == 0 && cellFormulas.Count == 0 && state.Count == 0) return;

        _formulasDroppedWithColumn[columnKey] = new DetachSnapshot(columnFormulas, cellFormulas, state);

        SidecarStructuralSync.OnColumnRemoved(Sidecar, columnKey, KeySegments(columnKey));
        RebuildGraph();
        if (HasAnyFormulas) FullRecalculate(); // formulas that READ the removed column now say so
    }

    private void RestoreFormulasForColumn(string columnKey)
    {
        if (!_formulasDroppedWithColumn.Remove(columnKey, out var snapshot)) return;

        RestoreFromDetach(snapshot);
        FullRecalculate();
    }

    private void OnColumnPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        // The only moment the old name is still readable — PropertyChanged arrives too late.
        if (e.PropertyName == nameof(JsonPropertyDefinition.Name) && sender is JsonPropertyDefinition property)
            _renamingColumnFrom = property.Name;
    }

    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(JsonPropertyDefinition.Name)) return;
        if (sender is not JsonPropertyDefinition property) return;

        var oldKey = _renamingColumnFrom;
        _renamingColumnFrom = null;
        if (oldKey is null || oldKey == property.Name) return;

        SidecarStructuralSync.OnColumnRenamed(Sidecar, oldKey, property.Name,
            KeySegments(oldKey), KeySegments(property.Name));
        RebuildGraph();
    }

    /// <summary>Splits a column key into its path segments — one for a flat column, several for an
    /// expanded nested field whose key joins its segments with <see cref="JsonFieldMapping.PathSeparator"/>.</summary>
    private static IReadOnlyList<string> KeySegments(string columnKey) =>
        columnKey.Split(JsonFieldMapping.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Recomputes the formula cells of <paramref name="count"/> rows starting at
    /// <paramref name="startIndex"/>, plus everything downstream of them.</summary>
    private void RecalculateRows(int startIndex, int count)
    {
        if (!HasAnyFormulas) return;

        var shape = new WorkspaceTableShape(_rows, _properties);
        var paths = new List<string>();
        for (var row = startIndex; row < startIndex + count && row < _rows.Count; row++)
            foreach (var columnKey in _properties.Select(p => p.Name))
                if (shape.PathForCell(row, columnKey) is { } path)
                    paths.Add(path.ToCanonicalString());

        if (paths.Count > 0) RecalculateFrom(paths, shape);
    }

    // -----------------------------------------------------------------------
    //  Nested scopes — formulas stored against a path INSIDE an Object/Array cell
    // -----------------------------------------------------------------------
    //
    //  A formula typed in the pencil-button editor for an Object/Array cell is kept in this same
    //  sidecar, keyed by the full path of the sub-node it computes ($[3].person.full). The
    //  top-level recalculation pass skips those keys — their "column" is not a real Property — so
    //  after every full recalculation each host cell is parsed into a throwaway sub-table, a nested
    //  session recomputes it, and the JSON is written back. A nested formula therefore sees only
    //  its own sub-table: a reference across the Object/Array boundary is #REF!.

    private sealed record NestedScope(
        int RowIndex,
        string ColumnName,
        bool IsArray,
        string BasePath,
        Dictionary<string, FormulaEntry> LocalCellFormulas,
        Dictionary<string, CellState> LocalState);

    /// <summary>Every distinct Object/Array cell that has at least one formula stored inside it,
    /// with those formulas (and any cached error state) re-keyed to paths within a standalone
    /// sub-table.</summary>
    private List<NestedScope> EnumerateNestedScopes()
    {
        var byBase = new Dictionary<string, NestedScope>(StringComparer.Ordinal);

        NestedScope? ScopeFor(string pathText)
        {
            if (!TrySplitNestedPath(pathText, out var row, out var col, out var isArray, out var baseText, out _))
                return null;
            if (!byBase.TryGetValue(baseText, out var scope))
                byBase[baseText] = scope = new NestedScope(row, col, isArray, baseText,
                    new Dictionary<string, FormulaEntry>(StringComparer.Ordinal),
                    new Dictionary<string, CellState>(StringComparer.Ordinal));
            return scope;
        }

        foreach (var (path, entry) in Sidecar.CellFormulas)
            if (TrySplitNestedPath(path, out _, out _, out _, out _, out var localKey) && ScopeFor(path) is { } s)
                s.LocalCellFormulas[localKey] = entry;

        foreach (var (path, st) in Sidecar.State)
            if (TrySplitNestedPath(path, out _, out _, out _, out _, out var localKey) && ScopeFor(path) is { } s)
                s.LocalState[localKey] = st;

        return byBase.Values.ToList();
    }

    /// <summary>(base path, current JSON text) for every Object/Array cell that hosts nested
    /// formulas — feeds <see cref="SnapshotFormulaCells"/> so a rewrite of the cell by
    /// <see cref="MaterializeNestedScopes"/> is seen as a change worth a grid refresh.</summary>
    private IEnumerable<(string BasePath, string CellText)> NestedScopeCells() =>
        EnumerateNestedScopes().Select(s =>
            (s.BasePath, IsLiveRow(s.RowIndex) ? _rows[s.RowIndex][s.ColumnName] : ""));

    private bool TrySplitNestedPath(string pathText, out int rowIndex, out string columnName,
        out bool isArray, out string baseText, out string localKey)
    {
        rowIndex = 0; columnName = ""; isArray = false; baseText = ""; localKey = "";

        // Cheap reject before invoking the path parser: a plain top-level cell path ("$[3].total")
        // has exactly one '[' and one '.'. Anything nested has more.
        var punct = 0;
        foreach (var c in pathText) if (c is '.' or '[') punct++;
        if (punct <= 2) return false;

        JsonPath jp;
        try { jp = JsonPath.Parse(pathText); }
        catch (FormatException) { return false; }

        if (jp.Segments.Count < 3) return false;
        if (jp.Segments[0] is not JsonPathSegment.Index rowIdx) return false;
        if (jp.Segments[1] is not JsonPathSegment.Key colSeg) return false;

        var prop = _properties.FirstOrDefault(p => p.Name == colSeg.Name);
        if (prop is null || prop.FieldType is not (JsonFieldType.Object or JsonFieldType.Array)) return false;

        rowIndex = rowIdx.Value;
        columnName = colSeg.Name;
        isArray = prop.FieldType == JsonFieldType.Array;
        baseText = new JsonPath(new JsonPathSegment[]
            { new JsonPathSegment.Index(rowIndex), new JsonPathSegment.Key(columnName) }).ToCanonicalString();

        var rest = jp.Segments.Skip(2).ToList();
        var local = new List<JsonPathSegment>();
        if (isArray)
        {
            if (rest[0] is not JsonPathSegment.Index elem) return false;
            local.Add(new JsonPathSegment.Index(elem.Value));
            local.AddRange(rest.Skip(1));
        }
        else
        {
            local.Add(new JsonPathSegment.Index(0));
            local.AddRange(rest);
        }
        if (local.Count < 2) return false; // needs $[i].something
        localKey = new JsonPath(local).ToCanonicalString();
        return true;
    }

    private string? ComposeNestedPath(NestedScope scope, string localKey)
    {
        JsonPath lp;
        try { lp = JsonPath.Parse(localKey); }
        catch (FormatException) { return null; }
        if (lp.Segments.Count < 2 || lp.Segments[0] is not JsonPathSegment.Index elem) return null;

        var segs = new List<JsonPathSegment>
        {
            new JsonPathSegment.Index(scope.RowIndex),
            new JsonPathSegment.Key(scope.ColumnName)
        };
        if (scope.IsArray) segs.Add(new JsonPathSegment.Index(elem.Value));
        segs.AddRange(lp.Segments.Skip(1));
        return new JsonPath(segs).ToCanonicalString();
    }

    /// <summary>base path -> (host JSON in, computed JSON out) from the last materialization, so an
    /// unchanged host cell is not re-parsed and re-evaluated on every recalculation. Invalidated for
    /// a base whenever <see cref="WriteNestedScope"/> changes its formulas.</summary>
    private readonly Dictionary<string, (string In, string Out)> _nestedScopeCache = new(StringComparer.Ordinal);

    /// <summary>Recomputes every nested scope: parse the host cell's JSON into a sub-table, run a
    /// nested session over the re-based formulas, and write the serialized result back into the
    /// cell. A no-op unless this is a top-level session (<see cref="_jsonService"/> set).</summary>
    private void MaterializeNestedScopes()
    {
        if (_jsonService is null) return;

        foreach (var scope in EnumerateNestedScopes())
        {
            if (!IsLiveRow(scope.RowIndex)) continue;
            var hostRow = _rows[scope.RowIndex];
            var cellJson = hostRow[scope.ColumnName];
            if (string.IsNullOrWhiteSpace(cellJson)) continue;

            // Skip the parse+evaluate+serialize round-trip when this cell's JSON is exactly what we
            // last produced from — the common case on an unrelated edit elsewhere in the document.
            if (_nestedScopeCache.TryGetValue(scope.BasePath, out var cached)
                && (cached.In == cellJson || cached.Out == cellJson))
                continue;

            List<JsonPropertyDefinition> subProps;
            List<DynamicDataRow> subRows;
            var primitiveArray = false;
            try
            {
                subProps = _jsonService.DetectFields(cellJson)
                    .Select(f => new JsonPropertyDefinition { Name = f.FieldName, FieldType = f.SelectedType })
                    .ToList();

                if (subProps.Count > 0)
                {
                    subRows = _jsonService.ParseJsonData(cellJson, subProps);
                }
                else
                {
                    // An array of plain values ("tags": [1,2,3]) has no fields to detect. Rebuild
                    // the same synthetic one-column shape the editor shows, so a formula stored
                    // against $[i].value still has a table to compute against.
                    if (!scope.IsArray || !TrySeedPrimitiveArray(cellJson, out subProps, out subRows)) continue;
                    primitiveArray = true;
                }
            }
            catch { continue; }
            if (subRows.Count == 0) continue;

            var childSidecar = new FormulaSidecar
            {
                Target = new TargetInfo("", "", TargetHash.HashInputId, DateTime.UtcNow)
            };
            foreach (var (k, v) in scope.LocalCellFormulas) childSidecar.CellFormulas[k] = v;
            foreach (var (k, v) in scope.LocalState) childSidecar.State[k] = v;

            // One-shot: no change subscriptions (nothing mutates subRows after this). It keeps the
            // IJsonService until MaxNestingDepth so a formula inside a cell inside a cell still
            // recomputes; past that it gets null and the recursion stops.
            var child = new FormulaSessionService(subRows, subProps,
                jsonService: _nestingDepth + 1 < MaxNestingDepth ? _jsonService : null,
                subscribeToChanges: false,
                nestingDepth: _nestingDepth + 1);
            try { child.AdoptSidecar(childSidecar); }
            catch { continue; }

            string newJson;
            try
            {
                newJson = primitiveArray
                    ? UnwrapPrimitiveArray(_jsonService.SerializeToJson(subRows, subProps))
                    : scope.IsArray
                        ? _jsonService.SerializeToJson(subRows, subProps)
                        : _jsonService.SerializeSingleRowToJson(subRows[0], subProps);
            }
            catch { continue; }

            if (newJson != cellJson)
                hostRow.SetCell(scope.ColumnName, newJson, CellKind.Value);

            _nestedScopeCache[scope.BasePath] = (cellJson, newJson);

            // Refresh this scope's cached error state in the parent sidecar (drop stale, re-add
            // current) so a reopened editor shows the right markers straight away.
            foreach (var key in Sidecar.State.Keys
                         .Where(k => k.StartsWith(scope.BasePath + ".", StringComparison.Ordinal)
                                     || k.StartsWith(scope.BasePath + "[", StringComparison.Ordinal))
                         .ToList())
                Sidecar.State.Remove(key);

            foreach (var (localKey, st) in child.Sidecar.State)
                if (ComposeNestedPath(scope, localKey) is { } composed)
                    Sidecar.State[composed] = st;
        }
    }

    /// <summary>Synthetic column the JSON editor gives one element of an array of plain values
    /// (<c>["a","b"]</c>), which has no fields of its own to name. Formulas inside such an array are
    /// stored against it, so this side has to agree with the editor on the name.</summary>
    public const string PrimitiveArrayColumn = "value";

    /// <summary>Rebuilds the editor's one-column view of an array of plain values. False when
    /// <paramref name="cellJson"/> isn't a JSON array (or is an array of objects, which
    /// <c>DetectFields</c> would already have handled).</summary>
    private static bool TrySeedPrimitiveArray(string cellJson,
        out List<JsonPropertyDefinition> props, out List<DynamicDataRow> rows)
    {
        props = new List<JsonPropertyDefinition>();
        rows = new List<DynamicDataRow>();

        JsonNode? node;
        try { node = JsonNode.Parse(cellJson); }
        catch (JsonException) { return false; }
        if (node is not JsonArray array) return false;

        var elementType = JsonFieldType.String;
        var values = new List<(string Text, CellKind Kind)>();
        foreach (var element in array)
        {
            if (values.Count == 0 && element is JsonValue first) elementType = InferPrimitiveType(first);
            values.Add(element switch
            {
                null => ("", CellKind.Null),
                JsonObject or JsonArray => (element.ToJsonString(), CellKind.Value),
                _ => (element.ToString(), CellKind.Value)
            });
        }

        props.Add(new JsonPropertyDefinition { Name = PrimitiveArrayColumn, FieldType = elementType });
        foreach (var (text, kind) in values)
        {
            var row = new DynamicDataRow();
            row.InitializeProperty(PrimitiveArrayColumn, text, kind);
            rows.Add(row);
        }
        return true;
    }

    private static JsonFieldType InferPrimitiveType(JsonValue value)
    {
        if (value.TryGetValue<bool>(out _)) return JsonFieldType.Bool;
        if (value.TryGetValue<int>(out _)) return JsonFieldType.Int;
        if (value.TryGetValue<double>(out _)) return JsonFieldType.Double;
        return JsonFieldType.String;
    }

    /// <summary>Turns <c>[{"value":1},{"value":2}]</c> back into <c>[1,2]</c> — the inverse of
    /// <see cref="TrySeedPrimitiveArray"/>, matching what the editor does on save.</summary>
    private static string UnwrapPrimitiveArray(string wrappedJson)
    {
        var unwrapped = new JsonArray();
        if (JsonNode.Parse(wrappedJson) is JsonArray wrapped)
            foreach (var item in wrapped)
                unwrapped.Add(item?[PrimitiveArrayColumn]?.DeepClone());

        return unwrapped.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    /// <summary>The canonical base-path text for an Object/Array cell (<c>$[3].person</c>) — the key
    /// the pencil-button editor's formula bridge uses.</summary>
    public string NestedBasePathFor(int rowIndex, string columnName) =>
        new JsonPath(new JsonPathSegment[]
        {
            new JsonPathSegment.Index(rowIndex),
            new JsonPathSegment.Key(columnName)
        }).ToCanonicalString();

    /// <summary>The formulas currently stored inside <paramref name="basePath"/> (an Object/Array
    /// cell), re-keyed to a standalone sub-table — what the pencil-button editor loads when it
    /// opens. Object cell → keys like <c>$[0].field</c>; array cell → <c>$[i].field</c>.</summary>
    public (Dictionary<string, FormulaEntry> CellFormulas, Dictionary<string, CellState> State) ReadNestedScope(string basePath)
    {
        var scope = EnumerateNestedScopes().FirstOrDefault(s => s.BasePath == basePath);
        return scope is null
            ? (new Dictionary<string, FormulaEntry>(), new Dictionary<string, CellState>())
            : (new Dictionary<string, FormulaEntry>(scope.LocalCellFormulas), new Dictionary<string, CellState>(scope.LocalState));
    }

    /// <summary>Replaces every formula/state entry stored inside <paramref name="basePath"/> with
    /// <paramref name="localCellFormulas"/>/<paramref name="localState"/> (keyed within the
    /// sub-table, as returned by <see cref="ReadNestedScope"/>), then recomputes. Called when the
    /// pencil-button editor is submitted.</summary>
    public void WriteNestedScope(string basePath, int rowIndex, string columnName, bool isArray,
        IReadOnlyDictionary<string, FormulaEntry> localCellFormulas,
        IReadOnlyDictionary<string, CellState> localState)
    {
        var scope = new NestedScope(rowIndex, columnName, isArray, basePath,
            new Dictionary<string, FormulaEntry>(), new Dictionary<string, CellState>());

        foreach (var key in Sidecar.CellFormulas.Keys
                     .Where(k => k.StartsWith(basePath + ".", StringComparison.Ordinal)
                                 || k.StartsWith(basePath + "[", StringComparison.Ordinal)).ToList())
            Sidecar.CellFormulas.Remove(key);
        foreach (var key in Sidecar.State.Keys
                     .Where(k => k.StartsWith(basePath + ".", StringComparison.Ordinal)
                                 || k.StartsWith(basePath + "[", StringComparison.Ordinal)).ToList())
            Sidecar.State.Remove(key);

        foreach (var (localKey, entry) in localCellFormulas)
            if (ComposeNestedPath(scope, localKey) is { } composed)
                Sidecar.CellFormulas[composed] = entry;
        foreach (var (localKey, st) in localState)
            if (ComposeNestedPath(scope, localKey) is { } composed)
                Sidecar.State[composed] = st;

        RebuildGraph();
        FullRecalculate();
        Recalculated?.Invoke(this, EventArgs.Empty);
    }

    // -----------------------------------------------------------------------
    //  Persistence
    // -----------------------------------------------------------------------

    /// <summary>Stamps <see cref="FormulaSidecar.Target"/> with the document's current hash right
    /// before saving, and returns the serialized sidecar text — or null if there are no formulas at
    /// all, in which case the caller shouldn't write a sidecar file (or should delete a stale one).</summary>
    public string? PrepareForSave(string mainFileName, string canonicalMainJson)
    {
        if (!HasAnyFormulas) return null;

        Sidecar.Target = Sidecar.Target with
        {
            FileName = mainFileName,
            Hash = TargetHash.Compute(canonicalMainJson),
            HashInput = TargetHash.HashInputId,
            SavedAtUtc = DateTime.UtcNow
        };
        return SidecarJsonSerializer.Serialize(Sidecar);
    }
}
