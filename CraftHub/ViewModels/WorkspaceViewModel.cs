using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Sidecar;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.Services.Actions;
using CraftHub.Services.Formulas;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CraftHub.ViewModels;

public partial class WorkspaceViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IJsonService _jsonService;
    private readonly IClassParserService _classParserService;
    private readonly IDialogService _dialogService;
    private readonly NotificationService _notificationService;

    [ObservableProperty] private string _header = "Tab";

    /// <summary>Absolute path of the file this tab is bound to, or null for an unsaved tab.</summary>
    [ObservableProperty] private string? _filePath;

    /// <summary>True when the tab has edits not yet written to <see cref="FilePath"/>.</summary>
    [ObservableProperty] private bool _isModified;

    /// <summary>Drives the busy overlay while a large document is being parsed, serialized or
    /// written — the work itself is off-thread, so without this the UI just looks frozen-but-idle.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Last known on-disk modification time, used to detect external changes.</summary>
    private DateTime? _fileWriteTimeUtc;

    /// <summary>Canonical (pretty-printed) JSON of the last-saved-on-disk state — or the
    /// originally imported state if never saved yet. The "old" side of the diff view.</summary>
    private string? _baselineJsonText;

    /// <summary>Suppresses dirty-tracking while data is being (re)loaded from disk.</summary>
    private bool _isLoading;

    /// <summary>Supplies the current file-explorer root, used as the default Save-As folder.</summary>
    public Func<string?>? GetProjectRoot { get; set; }

    /// <summary>Notifies the shell that a file was written to the given path (to refresh the tree).</summary>
    public Action<string>? FileSaved { get; set; }

    partial void OnRawJsonTextChanged(string value)
    {
        if (IsJsonEditorMode && !_isLoading) MarkDirty();
    }

    private void MarkDirty()
    {
        if (!_isLoading) IsModified = true;
    }

    private void OnRowValueChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        // A cell edit can move a row in or out of the filtered set (e.g. it now/no-longer
        // contains SearchQuery), so keep the filtered view honest.
        if (IsFilterActive) RefreshFilter();
    }

    /// <summary>Binds this (empty) tab to a newly created file without importing anything.</summary>
    public void BindToNewFile(string path)
    {
        FilePath = path;
        Header = Path.GetFileNameWithoutExtension(path);
        _fileWriteTimeUtc = SafeGetWriteTime(path);
        IsModified = false;
    }

    private static DateTime? SafeGetWriteTime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch { return null; }
    }

    [ObservableProperty] private string _propertyName = string.Empty;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private DynamicDataRow? _selectedRow;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isFilterActive;
    [ObservableProperty] private string _replaceQuery = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _selectedRowsCount = 0;
    [ObservableProperty] private bool _isJsonEditorMode = false;
    [ObservableProperty] private string _rawJsonText = string.Empty;
    [ObservableProperty] private string _jsonEditorError;
    [ObservableProperty] private bool _isJsonEditorErrorVisible;
    [ObservableProperty] private long _jsonEditorErrorLine = -1;
    [ObservableProperty] private bool _hasClipboardContent;

    /// <summary>
    /// True while a DataGrid cell editor (TextBox) is active.
    /// Disables the row-level clipboard commands so Ctrl+C/V/X fall through to the TextBox.
    /// </summary>
    [ObservableProperty] private bool _isCellEditing;

    public bool IsTableEditorMode => !IsJsonEditorMode;

    partial void OnIsJsonEditorModeChanged(bool value) => OnPropertyChanged(nameof(IsTableEditorMode));

    partial void OnSelectedRowsCountChanged(int value)
    {
        CopyRowsToJsonCommand.NotifyCanExecuteChanged();
        CopyRowsToJsonAsObjectsCommand.NotifyCanExecuteChanged();
        CutRowsToDataGridCommand.NotifyCanExecuteChanged();
        InsertRowAfterCommand.NotifyCanExecuteChanged();
        DuplicateRowsCommand.NotifyCanExecuteChanged();
        DuplicateAfterRowsCommand.NotifyCanExecuteChanged();
        RemoveRowsCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasClipboardContentChanged(bool value)
        => PasteRowsToDataGridCommand.NotifyCanExecuteChanged();

    partial void OnIsCellEditingChanged(bool value)
    {
        // Clipboard commands must yield to the cell TextBox while editing.
        CopyRowsToJsonCommand.NotifyCanExecuteChanged();
        CopyRowsToJsonAsObjectsCommand.NotifyCanExecuteChanged();
        CutRowsToDataGridCommand.NotifyCanExecuteChanged();
        PasteRowsToDataGridCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called from the View when the context menu opens to refresh clipboard state.</summary>
    internal async Task RefreshClipboardStateAsync()
    {
        var text = await _dialogService.GetFromClipboardAsync();
        HasClipboardContent = !string.IsNullOrWhiteSpace(text);
    }

    private bool HasSelection(object? _) => SelectedRowsCount > 0;

    // Row-level clipboard commands must not fire while a cell TextBox is active,
    // so that Ctrl+C/V/X fall through to the editor's built-in handling.
    private bool CanCopyOrCut(object? _) => SelectedRowsCount > 0 && !IsCellEditing;
    private bool CanPaste() => HasClipboardContent && !IsCellEditing;

    public BulkObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public BulkObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    /// <summary>Subset of <see cref="Rows"/> whose values contain <see cref="SearchQuery"/> —
    /// kept up to date whenever the query, the filter toggle, or the row data itself changes.</summary>
    public ObservableCollection<DynamicDataRow> FilteredRows { get; } = new();

    /// <summary>What the DataGrid actually shows: the live-filtered subset while the filter
    /// toggle is on and there's something to filter by, otherwise every row.</summary>
    public IEnumerable<DynamicDataRow> DisplayedRows =>
        IsFilterActive && !string.IsNullOrWhiteSpace(SearchQuery) ? FilteredRows : Rows;

    partial void OnSearchQueryChanged(string value) => RefreshFilter();

    partial void OnIsFilterActiveChanged(bool value) => OnPropertyChanged(nameof(DisplayedRows));

    private void RefreshFilter()
    {
        FilteredRows.Clear();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            foreach (var row in Rows)
                if (Properties.Any(p => (row[p.Name] ?? string.Empty).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)))
                    FilteredRows.Add(row);
        }
        OnPropertyChanged(nameof(DisplayedRows));
    }

    /// <summary>Replaces every occurrence of <see cref="SearchQuery"/> with <see cref="ReplaceQuery"/>
    /// in every column of the given row. Returns the individual cell changes made (empty if none).</summary>
    private List<ReplaceAllAction.Change> ReplaceInRow(DynamicDataRow row)
    {
        var changes = new List<ReplaceAllAction.Change>();
        foreach (var prop in Properties)
        {
            var oldValue = row[prop.Name] ?? string.Empty;
            if (!oldValue.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) continue;

            var newValue = oldValue.Replace(SearchQuery, ReplaceQuery, StringComparison.OrdinalIgnoreCase);
            if (newValue == oldValue) continue;

            row[prop.Name] = newValue;
            changes.Add(new ReplaceAllAction.Change(row, prop.Name, oldValue, newValue));
        }
        return changes;
    }

    /// <summary>Replace-current: applies to whichever row is selected. Called from the View,
    /// which then advances the selection to the next match (same "row is a match" granularity
    /// the search/highlight already uses).</summary>
    public void ReplaceInSelectedRow()
    {
        if (string.IsNullOrEmpty(SearchQuery) || SelectedRow is not { } row) return;

        var changes = ReplaceInRow(row);
        if (changes.Count == 0) return;

        UndoRedo.Push(new ReplaceAllAction(changes));
        MarkDirty();
        if (IsFilterActive) RefreshFilter();
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (string.IsNullOrEmpty(SearchQuery)) return;

        var changes = new List<ReplaceAllAction.Change>();
        foreach (var row in Rows)
            changes.AddRange(ReplaceInRow(row));

        if (changes.Count == 0)
        {
            NotifyWarning(Localizer.Get("NoMatchesMsg"));
            return;
        }

        UndoRedo.Push(new ReplaceAllAction(changes));
        MarkDirty();
        if (IsFilterActive) RefreshFilter();
        NotifySuccess(Localizer.Get("ReplacedAllMsg", changes.Count));
    }

    public event EventHandler? CloseRequested;
    public event EventHandler? ColumnsChanged;

    // Ctrl+F / Ctrl+H are declared as UserControl.KeyBindings (same mechanism as Ctrl+S/Ctrl+Z)
    // so they fire reliably no matter where focus currently is, including inside the replace
    // flyout's own popup content — but focusing/opening a specific control is a View concern,
    // so the actual work happens in WorkspaceView via these events.
    public event EventHandler? FocusSearchRequested;
    public event EventHandler? ToggleReplaceRequested;

    [RelayCommand] private void FocusSearch() => FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ToggleReplace() => ToggleReplaceRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Set by MainWindowViewModel. Called when ImportJsonAsync needs a fresh workspace
    /// for an additional file. Returns the new WorkspaceViewModel, or null if the tab
    /// limit (15) has been reached.
    /// </summary>
    public Func<WorkspaceViewModel?>? RequestNewWorkspace { get; set; }

    public int TotalRows => Rows.Count;

    private string _dataSizeKb = "0.0 KB";
    public string DataSizeKb => _dataSizeKb;

    // -----------------------------------------------------------------------
    //  Formulas
    // -----------------------------------------------------------------------

    // Constructed in the constructor (not = new(), like UndoRedo) because it needs the SAME live
    // Rows/Properties collections this workspace owns — see FormulaSessionService's own doc comment.
    private readonly FormulaSessionService _formulaSession;

    public bool HasFormulas => _formulaSession.HasAnyFormulas;

    /// <summary>Raised after any operation that changes which cells are formulas. The fx marker and
    /// the fill handle are decided when a cell template is built, not by a binding, so the view has
    /// to rebuild its rows to show them — and every formula operation except a single-cell edit
    /// (fill down, apply to column, detach, and the formula bar) reaches the session without the
    /// grid ever hearing about it. One event rather than a DataGrid reference threaded through each
    /// of them.</summary>
    public event EventHandler? FormulaVisualsChanged;

    private void FireFormulaVisualsChanged() => FormulaVisualsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Which cell the formula bar shows/edits — set by the View from the DataGrid's own
    /// CurrentCellChanged event, since "current cell" isn't something the DataGrid selection model
    /// (SelectedRow/SelectedRowsCount) already tracks at column granularity.</summary>
    [ObservableProperty] private int _currentCellRowIndex = -1;
    [ObservableProperty] private string? _currentCellColumnKey;

    public bool HasCurrentCell => CurrentCellRowIndex >= 0 && CurrentCellColumnKey is not null;

    /// <summary>What the formula bar should show for the current cell right now.</summary>
    public string CurrentCellText => HasCurrentCell ? GetEditableText(CurrentCellRowIndex, CurrentCellColumnKey!) : "";

    public bool IsFormulaCell(int rowIndex, string columnKey) => _formulaSession.IsFormulaCell(rowIndex, columnKey);

    /// <summary>The A1-form formula text for a cell (what F2 / double-click should show), or null
    /// if it isn't a formula.</summary>
    public string? GetDisplayFormula(int rowIndex, string columnKey) => _formulaSession.GetDisplayFormula(rowIndex, columnKey);

    /// <summary>The sidecar's recorded error for a cell, if it currently has one — for the marker
    /// tooltip.</summary>
    public CellState? GetFormulaErrorState(int rowIndex, string columnKey) => _formulaSession.GetErrorState(rowIndex, columnKey);

    /// <summary>What to actually display for a cell right now — its formula text if it's a
    /// formula, otherwise its stored value. What both the grid's cell editor and the formula bar
    /// show, and what a no-op edit must round-trip back to unchanged.</summary>
    public string GetEditableText(int rowIndex, string columnKey)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count) return "";
        return GetDisplayFormula(rowIndex, columnKey) ?? Rows[rowIndex][columnKey];
    }

    /// <summary>Single entry point for "the user finished typing this text into this cell" —
    /// used by both the grid's own cell editor (<see cref="Views.WorkspaceView"/>'s
    /// OnDataGridCellEditEnded) and the formula bar, so an edit behaves identically no matter
    /// where it was typed: text starting with '=' becomes a formula; anything else becomes a
    /// plain value, dropping any formula the cell previously had.
    ///
    /// <paramref name="oldValue"/>/<paramref name="oldKind"/> are the cell's state before the edit.
    /// Both callers can read that straight off the row — neither the grid's cell editor nor the
    /// formula bar writes into it before committing — but it stays an explicit parameter because
    /// this method itself mutates the row partway through (removing a formula), and the undo entry
    /// it pushes has to describe the state from before all of that.</summary>
    public void CommitCellEdit(int rowIndex, string columnKey, string oldValue, CellKind oldKind, string newText, DataGrid? dataGrid)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count) return;
        var row = Rows[rowIndex];

        if (newText.StartsWith('='))
        {
            // Committing the exact text the cell already shows is not an edit. Worth guarding
            // explicitly: a cell computed by its COLUMN's template shows that template here, so
            // without this, tabbing through such a cell would silently turn it into a per-cell
            // override of a formula identical to the one it already had.
            if (newText == GetEditableText(rowIndex, columnKey)) return;

            CommitCellFormula(rowIndex, columnKey, newText, dataGrid);
            return;
        }

        var wasFormula = IsFormulaCell(rowIndex, columnKey);
        if (wasFormula)
        {
            // The formula text was never the row's real stored content — drop the formula first
            // (its own undo step; reverts the row to its last computed value as a side effect),
            // then use THAT as the "old" side of the plain edit being applied on top.
            RemoveCellFormula(rowIndex, columnKey, dataGrid);
            oldValue = row[columnKey];
            oldKind = row.GetKind(columnKey);
        }

        if (oldValue == newText && !wasFormula) return; // no real change

        row[columnKey] = newText;
        UndoRedo.Push(new EditCellAction(row, columnKey, oldValue, oldKind, newText, dataGrid));
        MarkDirty();
    }

    /// <summary>Parses and stores a formula typed into a cell (text starting with '='), pushes an
    /// undo step, and marks the tab dirty. Called from the DataGrid's cell-edit commit.</summary>
    public void CommitCellFormula(int rowIndex, string columnKey, string a1FormulaText, DataGrid? dataGrid)
    {
        var changeSet = _formulaSession.TrySetCellFormula(rowIndex, columnKey, a1FormulaText, out var error);
        if (changeSet is null)
        {
            NotifyError(error ?? Localizer.Get("FormulaInvalidMsg"));
            return;
        }

        UndoRedo.Push(new SetCellFormulaAction(_formulaSession, changeSet, dataGrid));
        MarkDirty();
        FireFormulaVisualsChanged();
    }

    /// <summary>Removes a cell's own formula, falling back to the column's template if it has one.
    /// Pushes its own undo step. No-op (and pushes nothing) if the cell wasn't a formula.</summary>
    public void RemoveCellFormula(int rowIndex, string columnKey, DataGrid? dataGrid)
    {
        var changeSet = _formulaSession.TryRemoveCellFormula(rowIndex, columnKey);
        if (changeSet is null) return;

        UndoRedo.Push(new SetCellFormulaAction(_formulaSession, changeSet, dataGrid));
        MarkDirty();
        FireFormulaVisualsChanged();
    }

    /// <summary>Copies <paramref name="sourceRowIndex"/>'s formula down into every row in
    /// <paramref name="targetRowIndices"/> — the fill-down command.</summary>
    public void FillDown(int sourceRowIndex, string columnKey, IReadOnlyList<int> targetRowIndices, DataGrid? dataGrid)
    {
        var changeSets = _formulaSession.FillDown(sourceRowIndex, columnKey, targetRowIndices);
        if (changeSets.Count == 0)
        {
            NotifyWarning(Localizer.Get("FormulaFillDownNothingMsg"));
            return;
        }

        UndoRedo.Push(new FillDownAction(_formulaSession, changeSets, dataGrid));
        MarkDirty();
        FireFormulaVisualsChanged();
        NotifySuccess(Localizer.Get("FormulaFillDownMsg", changeSets.Count));
    }

    /// <summary>True when this column computes every row from one shared template, rather than from
    /// per-cell formulas — what the header marker and the context menu's "remove" entry key off.</summary>
    public bool ColumnHasFormula(string columnKey) => _formulaSession.HasColumnFormula(columnKey);

    /// <summary>True when the cell computes from its own formula rather than inheriting the
    /// column's — see <see cref="FormulaSessionService.CellHasOwnFormula"/>.</summary>
    public bool CellHasOwnFormula(int rowIndex, string columnKey) => _formulaSession.CellHasOwnFormula(rowIndex, columnKey);

    public bool CurrentColumnHasFormula => HasCurrentCell && _formulaSession.HasColumnFormula(CurrentCellColumnKey!);

    /// <summary>The column template's A1 text as it reads from a given row — what the formula bar
    /// shows when the current cell gets its value from the column rather than from its own formula.</summary>
    public string? GetDisplayColumnFormula(string columnKey, int viewingRowIndex) =>
        _formulaSession.GetDisplayColumnFormula(columnKey, viewingRowIndex);

    /// <summary>Applies one formula to every row of a column at once — the "calculated column" a
    /// fill-down only approximates, since this one also computes rows added later. Clears the
    /// column's per-cell formulas as a documented side effect (see
    /// <see cref="FormulaSessionService.TrySetColumnFormula"/>), which is why the toast says how
    /// many rows it touched: applying a column formula is a bigger action than editing one cell,
    /// and it should look like one.</summary>
    public void CommitColumnFormula(int authoringRowIndex, string columnKey, string a1FormulaText, DataGrid? dataGrid)
    {
        if (!a1FormulaText.StartsWith('='))
        {
            NotifyWarning(Localizer.Get("FormulaColumnNeedsFormulaMsg"));
            return;
        }

        var changeSet = _formulaSession.TrySetColumnFormula(authoringRowIndex, columnKey, a1FormulaText, out var error);
        if (changeSet is null)
        {
            NotifyError(error ?? Localizer.Get("FormulaInvalidMsg"));
            return;
        }

        UndoRedo.Push(new SetColumnFormulaAction(_formulaSession, changeSet, dataGrid));
        MarkDirty();
        FireFormulaVisualsChanged();
        NotifySuccess(Localizer.Get("FormulaColumnAppliedMsg",
            JsonPropertyDefinition.GetDisplayPath(columnKey), Rows.Count));
    }

    /// <summary>Stops a column recomputing itself. Every cell keeps its last computed value, so
    /// this is the column-scoped twin of "detach formulas", not a way to clear the column.</summary>
    public void RemoveColumnFormula(string columnKey, DataGrid? dataGrid)
    {
        var changeSet = _formulaSession.TryRemoveColumnFormula(columnKey);
        if (changeSet is null)
        {
            NotifyWarning(Localizer.Get("FormulaColumnNoneMsg"));
            return;
        }

        UndoRedo.Push(new SetColumnFormulaAction(_formulaSession, changeSet, dataGrid));
        MarkDirty();
        FireFormulaVisualsChanged();
        NotifySuccess(Localizer.Get("FormulaColumnRemovedMsg", JsonPropertyDefinition.GetDisplayPath(columnKey)));
    }

    [RelayCommand]
    private async Task DetachFormulasAsync()
    {
        if (!_formulaSession.HasAnyFormulas)
        {
            NotifyWarning(Localizer.Get("FormulaNoneToDetachMsg"));
            return;
        }

        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("DetachFormulasTitle"), Localizer.Get("DetachFormulasConfirmMsg"));
        if (!confirmed) return;

        var snapshot = _formulaSession.DetachAll();
        UndoRedo.Push(new DetachFormulasAction(_formulaSession, snapshot));
        MarkDirty();
        FireFormulaVisualsChanged();
        NotifySuccess(Localizer.Get("FormulaDetachedMsg"));
    }

    private async Task LoadFormulaSidecarAsync(string path, string canonicalMainJson)
    {
        var result = await _formulaSession.LoadAsync(path, canonicalMainJson);
        switch (result.Outcome)
        {
            case SidecarLoadOutcome.Corrupt:
                NotifyWarning(Localizer.Get("FormulaSidecarCorruptMsg",
                    result.CorruptBackupPath is { } p ? Path.GetFileName(p) : "", result.CorruptReason ?? ""));
                break;

            case SidecarLoadOutcome.HashMismatch:
                NotifyWarning(Localizer.Get("FormulaSidecarHashMismatchMsg"));
                if (_formulaSession.Sidecar.Options.RecalcOnOpen != RecalcOnOpenPolicy.Never)
                    _formulaSession.FullRecalculate();
                break;

            case SidecarLoadOutcome.Clean:
                if (_formulaSession.Sidecar.Options.RecalcOnOpen == RecalcOnOpenPolicy.Always)
                    _formulaSession.FullRecalculate();
                break;
        }
    }

    // -----------------------------------------------------------------------
    //  Undo / Redo
    // -----------------------------------------------------------------------

    public UndoRedoService UndoRedo { get; } = new();

    private bool CanUndo => UndoRedo.CanUndo;
    private bool CanRedo => UndoRedo.CanRedo;

    /// <summary>Dynamic tooltip: "Undo: Add row" or just "Undo" when stack is empty.</summary>
    [ObservableProperty] public string _undoTooltip;

    /// <summary>Dynamic tooltip: "Redo: Add row" or just "Redo" when stack is empty.</summary>
    [ObservableProperty] public string _redoTooltip;


    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private void NotifySuccess(string message) => _notificationService.Publish(NotificationType.Success, message);
    private void NotifyWarning(string message) => _notificationService.Publish(NotificationType.Warning, message);
    private void NotifyError(string message) => _notificationService.Publish(NotificationType.Error, message);

    private void FireColumnsChanged() => ColumnsChanged?.Invoke(this, EventArgs.Empty);

    // -----------------------------------------------------------------------
    //  Constructor
    // -----------------------------------------------------------------------

    public WorkspaceViewModel(
        IFileDialogService fileDialogService,
        IJsonService jsonService,
        IClassParserService classParserService,
        IDialogService dialogService,
        NotificationService notificationService)
    {
        _fileDialogService = fileDialogService;
        _jsonService = jsonService;
        _classParserService = classParserService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _formulaSession = new FormulaSessionService(Rows, Properties);

        Rows.CollectionChanged += OnRowsCollectionChanged;
        Properties.CollectionChanged += OnPropertiesCollectionChanged;

        // Any undoable data change (add / remove / undo / redo) flags the tab as modified.
        UndoRedo.StateChanged += MarkDirty;

        // Keep Undo/Redo buttons and tooltips in sync
        UndoRedo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UndoRedoService.CanUndo) or null)
            {
                UndoCommand.NotifyCanExecuteChanged();
                UndoTooltip = UndoRedo.UndoDescription is { } d
                    ? $"{Localizer.Get("UndoTip")}: {d}"
                    : Localizer.Get("UndoTip");
            }

            if (e.PropertyName is nameof(UndoRedoService.CanRedo) or null)
            {
                RedoCommand.NotifyCanExecuteChanged();
                RedoTooltip = UndoRedo.RedoDescription is { } d
                    ? $"{Localizer.Get("RedoTip")}: {d}"
                    : Localizer.Get("RedoTip");
            }

            if (e.PropertyName == nameof(UndoRedoService.UndoDescription))
                UndoTooltip = UndoRedo.UndoDescription is { } d
                    ? $"{Localizer.Get("UndoTip")}: {d}"
                    : Localizer.Get("UndoTip");
            if (e.PropertyName == nameof(UndoRedoService.RedoDescription))
                RedoTooltip = UndoRedo.RedoDescription is { } d
                    ? $"{Localizer.Get("RedoTip")}: {d}"
                    : Localizer.Get("RedoTip");
        };
    }

    // -----------------------------------------------------------------------
    //  Data size
    // -----------------------------------------------------------------------

    // Track row/property changes both to update the status bar and to flag unsaved edits.
    // Per-row PropertyChanged catches in-cell text edits as the user types (before the
    // DataGrid commits an undo action), so the "modified" marker appears immediately.
    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TotalRows));
        UpdateDataSize();
        if (IsFilterActive) RefreshFilter();

        if (e.OldItems != null)
            foreach (DynamicDataRow row in e.OldItems)
                row.PropertyChanged -= OnRowValueChanged;

        if (e.NewItems != null)
            foreach (DynamicDataRow row in e.NewItems)
                row.PropertyChanged += OnRowValueChanged;

        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var row in Rows)
            {
                row.PropertyChanged -= OnRowValueChanged;
                row.PropertyChanged += OnRowValueChanged;
            }

        MarkDirty();
    }

    private void OnPropertiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateDataSize();
        MarkDirty();
    }

    private void UpdateDataSize()
    {
        try
        {
            if (Rows.Count == 0 || Properties.Count == 0)
            {
                _dataSizeKb = "0.0 KB";
            }
            else
            {
                var json = _jsonService.SerializeToJson(Rows, Properties);
                _dataSizeKb = $"{Encoding.UTF8.GetByteCount(json) / 1024.0:F1} KB";
            }
        }
        catch
        {
            _dataSizeKb = "? KB";
        }

        OnPropertyChanged(nameof(DataSizeKb));
    }

    // -----------------------------------------------------------------------
    //  Schema (property) commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddProperty()
    {
        if (string.IsNullOrWhiteSpace(PropertyName))
        {
            NotifyWarning(Localizer.Get("EnterPropertyName"));
            return;
        }

        if (Properties.Any(p => p.Name == PropertyName))
        {
            NotifyWarning(Localizer.Get("PropertyAlreadyExists"));
            return;
        }

        var prop = new JsonPropertyDefinition
        {
            Name = PropertyName,
            FieldType = SelectedType,
        };

        Properties.Add(prop);
        foreach (var row in Rows)
            row.InitializeProperty(prop.Name);

        UndoRedo.Push(new AddPropertyAction(Properties, Rows, prop, FireColumnsChanged));

        PropertyName = string.Empty;
        NotifySuccess(Localizer.Get("PropertyAdded", prop.Name));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task RemovePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;

        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemovePropertyTitle"),
            Localizer.Get("RemovePropertyMsg", prop.Name));
        if (!confirmed) return;

        // Capture state before removal for undo
        var propIndex = Properties.IndexOf(prop);
        var savedValues = Rows.ToDictionary(r => r, r => (r[prop.Name], r.GetKind(prop.Name)));

        Properties.Remove(prop);
        foreach (var row in Rows)
            row.RemoveProperty(prop.Name);

        UndoRedo.Push(new RemovePropertyAction(Properties, Rows, prop, propIndex, savedValues, FireColumnsChanged));

        NotifySuccess(Localizer.Get("PropertyRemoved", prop.Name));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task RenamePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;

        var newName = await _dialogService.ShowInputDialogAsync(
            Localizer.Get("RenamePropertyTitle"),
            Localizer.Get("RenamePropertyPrompt"),
            prop.Name,
            Localizer.Get("PropertyNameWatermark"));

        if (newName == null) return;

        newName = newName.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            NotifyWarning(Localizer.Get("EnterPropertyName"));
            return;
        }

        if (newName == prop.Name) return;

        if (Properties.Any(p => p.Name == newName))
        {
            NotifyWarning(Localizer.Get("PropertyAlreadyExists"));
            return;
        }

        var oldName = prop.Name;

        prop.Name = newName;
        foreach (var row in Rows)
            row.RenameProperty(oldName, newName);

        UndoRedo.Push(new RenamePropertyAction(prop, oldName, newName, Rows, FireColumnsChanged));

        NotifySuccess(Localizer.Get("PropertyRenamed", oldName, newName));
        FireColumnsChanged();
    }

    // -----------------------------------------------------------------------
    //  Row commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddRow()
    {
        var row = new DynamicDataRow();
        foreach (var prop in Properties)
            row.InitializeProperty(prop.Name);

        Rows.Add(row);
        UndoRedo.Push(new AddRowAction(Rows, row));
        NotifySuccess(Localizer.Get("RowAdded", Rows.Count));
    }

    /// <summary>Inserts a new empty row immediately after the selected row (at selectedIndex + 1),
    /// rather than appending it to the end of the table.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void InsertRowAfter(object? parameter)
    {
        var source = ResolveSelectedRows(parameter);

        // Insert point = one position after the last selected row; fall back to the end.
        int insertIdx;
        if (source == null || source.Count == 0)
        {
            insertIdx = Rows.Count;
        }
        else
        {
            var insertAfter = source.OrderByDescending(r => Rows.IndexOf(r)).First();
            insertIdx = Rows.IndexOf(insertAfter) + 1;
        }

        var row = new DynamicDataRow();
        foreach (var prop in Properties)
            row.InitializeProperty(prop.Name);

        Rows.Insert(insertIdx, row);
        UndoRedo.Push(new InsertRowAction(Rows, row, insertIdx));
        NotifySuccess(Localizer.Get("RowInsertedMsg", insertIdx + 1));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateRows(object? parameter)
    {
        var source = ResolveSelectedRows(parameter);
        if (source == null || source.Count == 0) return;

        var duplicated = source.Select(CreateDuplicateRow).ToList();

        // Append to the end.
        foreach (var row in duplicated)
            Rows.Add(row);

        UndoRedo.Push(new DuplicateRowsAction(Rows, duplicated));
        NotifySuccess(Localizer.Get("RowsDuplicatedMsg", source.Count));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateAfterRows(object? parameter)
    {
        var source = ResolveSelectedRows(parameter);
        if (source == null || source.Count == 0) return;

        // Insert point = one position after the last selected row.
        var insertAfter = source.OrderByDescending(r => Rows.IndexOf(r)).First();
        int insertIdx = Rows.IndexOf(insertAfter) + 1;

        var duplicated = source
            .OrderBy(r => Rows.IndexOf(r)) // preserve original order
            .Select(CreateDuplicateRow)
            .ToList();

        for (int i = 0; i < duplicated.Count; i++)
            Rows.Insert(insertIdx + i, duplicated[i]);

        UndoRedo.Push(new DuplicateRowsAction(Rows, duplicated, insertAfter));
        NotifySuccess(Localizer.Get("RowsDuplicatedMsg", source.Count));
    }

    /// <summary>Creates a copy of <paramref name="row"/> without adding it to <see cref="Rows"/>.</summary>
    private DynamicDataRow CreateDuplicateRow(DynamicDataRow row)
    {
        var newRow = new DynamicDataRow();
        foreach (var prop in Properties)
            newRow.InitializeProperty(prop.Name, row[prop.Name], row.GetKind(prop.Name));
        return newRow;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveRowsAsync(object? parameter)
    {
        if (parameter is not IList items || items.Count == 0)
        {
            if (SelectedRow != null)
                await RemoveSingleRowAsync(SelectedRow);
            return;
        }

        var toRemove = items.Cast<DynamicDataRow>().ToList();
        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemoveRowsTitle"),
            Localizer.Get("RemoveRowsMsg", toRemove.Count));
        if (!confirmed) return;

        // Capture indices BEFORE removal so undo can restore positions
        var withIndices = toRemove
            .Select(r => (Index: Rows.IndexOf(r), Row: r))
            .Where(x => x.Index >= 0)
            .ToList();

        foreach (var item in withIndices)
            Rows.Remove(item.Row);

        UndoRedo.Push(new RemoveRowsAction(Rows, withIndices.Select(x => (x.Index, x.Row))));
        NotifySuccess(Localizer.Get("RowsRemovedMsg", withIndices.Count));
    }

    private async Task RemoveSingleRowAsync(DynamicDataRow row)
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemoveRowTitle"),
            Localizer.Get("RemoveRowMsg"));
        if (!confirmed) return;

        var idx = Rows.IndexOf(row);
        Rows.Remove(row);
        UndoRedo.Push(new RemoveRowsAction(Rows, new[] { (idx, row) }));
        NotifySuccess(Localizer.Get("RowRemovedMsg"));
    }

    // -----------------------------------------------------------------------
    //  Cell edit (complex types — called from code-behind)
    // -----------------------------------------------------------------------

    public async Task EditJsonCellAsync(DynamicDataRow row, string propertyName, JsonFieldType type)
    {
        var currentValue = row[propertyName];

        // Build merged schema from all rows in this column so every cell shares the same fields.
        var merged = new List<JsonPropertyDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in Rows)
        {
            var val = r[propertyName];
            if (string.IsNullOrWhiteSpace(val)) continue;
            try
            {
                foreach (var f in _jsonService.DetectFields(val))
                {
                    if (seen.Add(f.FieldName))
                        merged.Add(new JsonPropertyDefinition { Name = f.FieldName, FieldType = f.SelectedType });
                }
            }
            catch
            {
            }
        }

        var newValue = await _dialogService.ShowJsonEditorDialogAsync($"Edit {propertyName}", currentValue, type,
            _jsonService,
            merged.Count > 0 ? merged : null);

        if (newValue == null || newValue == currentValue) return;

        var newRow = new DynamicDataRow();
        foreach (var kvp in row.GetAllValues())
            newRow.InitializeProperty(kvp.Key, kvp.Value, row.GetKind(kvp.Key));
        newRow[propertyName] = newValue;

        var idx = Rows.IndexOf(row);
        if (idx < 0) return;

        Rows[idx] = newRow;
        UndoRedo.Push(new EditJsonCellAction(Rows, row, newRow, propertyName));
        NotifySuccess(Localizer.Get("UpdatedCellMsg", propertyName));
    }

    // -----------------------------------------------------------------------
    //  Copy commands (read-only, no undo needed)
    // -----------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task CopyRowsToJsonAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : _jsonService.SerializeToJson(selectedRows, Properties);

        await _dialogService.CopyToClipboardAsync(json);
        HasClipboardContent = true; // enable Paste immediately, no context-menu refresh needed
        NotifySuccess(Localizer.Get("RowsCopiedMsg", selectedRows.Count));
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteRowsToDataGridAsync(object? parameter)
    {
        var json = await _dialogService.GetFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(json)) return;
        var pasteData = _jsonService.ParseJsonData(json, Properties);
        foreach (var row in pasteData)
        {
            Rows.Add(row);
        }

        if (pasteData == null || pasteData.Count <= 0)
        {
            NotifyError(Localizer.Get("RowsPasteErrorMsg", pasteData.Count));
        }
        else
        {
            UndoRedo.Push(new PasteRowsAction(Rows, pasteData));
            NotifySuccess(Localizer.Get("RowsPasteMsg", pasteData.Count));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task CutRowsToDataGridAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var withIndices = selectedRows
            .Select(r => (Index: Rows.IndexOf(r), Row: r))
            .Where(x => x.Index >= 0)
            .ToList();

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : _jsonService.SerializeToJson(selectedRows, Properties);

        await _dialogService.CopyToClipboardAsync(json);
        HasClipboardContent = true; // enable Paste immediately

        foreach (var row in selectedRows)
            Rows.Remove(row);

        UndoRedo.Push(new RemoveRowsAction(Rows, withIndices.Select(x => (x.Index, x.Row))));

        NotifySuccess(Localizer.Get("RowsCutMsg", selectedRows.Count));
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task CopyRowsToJsonAsObjectsAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : string.Join(", ", selectedRows.Select(r => _jsonService.SerializeSingleRowToJson(r, Properties)));

        await _dialogService.CopyToClipboardAsync(json);
        NotifySuccess(Localizer.Get("RowsCopiedMsg", selectedRows.Count));
    }

    /// <summary>Writes just the selected rows to a JSON file — a partial dump for a bug report or
    /// a test fixture, without exporting (or clobbering) the whole table. Always an array, even
    /// for a single row, so the file re-imports the same way a full export does.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task ExportSelectedRowsAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var filters = new List<FileFilter> { new("JSON and TXT files", new[] { "*.json", "*.txt" }) };
        var suggestedName = $"{Header}_selected";
        var path = await _fileDialogService.SaveFileAsync(Localizer.Get("ExportSelectedTitle"), filters, suggestedName);
        if (path == null) return;

        var json = _jsonService.SerializeToJson(selectedRows, Properties);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        NotifySuccess(Localizer.Get("ExportedSelectedMsg", selectedRows.Count, Path.GetFileName(path)));
    }

    private List<DynamicDataRow>? ResolveSelectedRows(object? parameter)
    {
        if (parameter is IList { Count: > 0 } list)
            return list.Cast<DynamicDataRow>().ToList();
        if (SelectedRow != null)
            return new List<DynamicDataRow> { SelectedRow };
        return null;
    }

    // -----------------------------------------------------------------------
    //  Import / Export  (imports clear undo history — state changes completely)
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        var filters = new List<FileFilter> { new("JSON and TXT files", new[] { "*.json", "*.txt" }) };
        var paths = await _fileDialogService.OpenMultipleFilesAsync(Localizer.Get("ImportJson"), filters);
        if (paths.Count == 0) return;

        // First file goes into the current workspace (reuse it if empty, otherwise it will
        // overwrite — consistent with the previous single-file behaviour).
        await ImportFromPathAsync(paths[0]);

        // Remaining files each get their own new workspace tab.
        for (int i = 1; i < paths.Count; i++)
        {
            var newVm = RequestNewWorkspace?.Invoke();
            if (newVm == null)
            {
                // Tab limit reached — notify and stop.
                NotifyError(Localizer.Get("TabLimitReachedMsg", 15));
                break;
            }

            await newVm.ImportFromPathAsync(paths[i]);
        }
    }

    /// <summary>
    /// Imports a single JSON file into this workspace.
    /// Shows the field mapping dialog if the schema is not yet defined.
    /// Returns false if the user cancelled the mapping dialog.
    /// </summary>
    public async Task<bool> ImportFromPathAsync(string path)
    {
        _isLoading = true;
        IsBusy = true;
        try
        {
            if (Properties.Count > 0 || Rows.Count > 0)
            {
                var confirmed = await _dialogService.ShowConfirmAsync(
                    Localizer.Get("ImportOverwriteTitle"),
                    Localizer.Get("ImportOverwriteMsg"));
                if (!confirmed) return false;
            }

            var json = await File.ReadAllTextAsync(path);
            json = await Task.Run(() => _jsonService.SanitizeJson(json));

            if (Properties.Count == 0)
            {
                var detectedFields = await Task.Run(() => _jsonService.DetectFields(json));
                if (detectedFields.Count == 0)
                {
                    await _dialogService.ShowMessageAsync(Localizer.Get("ImportTitle"),
                        Localizer.Get("NoFieldsDetectedMsg"));
                    return false;
                }

                // Loop until the user either cancels or picks compatible types for every field.
                // JsonFieldMapping items are shared references so SelectedType changes made inside
                // the dialog are preserved when we reopen it after showing an error.
                List<JsonFieldMapping>? mappedFields;
                while (true)
                {
                    mappedFields =
                        await _dialogService.ShowFieldMappingDialogAsync(detectedFields, Path.GetFileName(path));
                    if (mappedFields == null) return false;

                    // Validate that Array/Object fields actually contain valid JSON of the correct kind.
                    // Numbers, booleans and plain strings are valid JSON but cannot be opened in the
                    // nested editor, which expects '[' or '{' as the first character.
                    var typeErrors = new List<string>();
                    foreach (var field in mappedFields)
                    {
                        if (field.SelectedType is JsonFieldType.Object or JsonFieldType.Array
                            && !string.IsNullOrEmpty(field.SampleValue))
                        {
                            var typeName = field.SelectedType == JsonFieldType.Array ? "Array" : "Object";
                            var expectedKind = field.SelectedType == JsonFieldType.Array
                                ? JsonValueKind.Array
                                : JsonValueKind.Object;
                            try
                            {
                                using var doc = JsonDocument.Parse(field.SampleValue);
                                if (doc.RootElement.ValueKind != expectedKind)
                                    typeErrors.Add($"  • '{field.FieldName}': \"{field.SampleValue}\" → не {typeName}");
                            }
                            catch (JsonException)
                            {
                                typeErrors.Add($"  • '{field.FieldName}': \"{field.SampleValue}\" → не {typeName}");
                            }
                        }
                    }

                    if (typeErrors.Count == 0) break; // all good, proceed

                    // Show error and reopen the dialog so the user can fix the types.
                    var msg = Localizer.Get("ImportTypeMismatchMsg") + "\n\n" + string.Join("\n", typeErrors);
                    await _dialogService.ShowMessageAsync(Localizer.Get("ImportTitle"), msg);
                }

                Properties.AddRange(mappedFields.Select(f =>
                    new JsonPropertyDefinition { Name = f.FieldName, FieldType = f.SelectedType }));
            }

            var rows = await Task.Run(() => _jsonService.ParseJsonData(json, Properties));
            Rows.Clear();
            Rows.AddRange(rows);

            Header = Path.GetFileNameWithoutExtension(path);
            UndoRedo.Clear(); // destructive — clear history
            NotifySuccess(Localizer.Get("ImportedMsg", Rows.Count, Properties.Count));
            FireColumnsChanged();

            if (IsJsonEditorMode)
                RawJsonText = await Task.Run(() => _jsonService.SerializeToJson(Rows, Properties));

            // Bind this tab to the source file so Ctrl+S writes back to it.
            FilePath = path;
            _fileWriteTimeUtc = SafeGetWriteTime(path);
            IsModified = false;
            _baselineJsonText = await Task.Run(() => _jsonService.SerializeToJson(Rows, Properties));

            // Hashed against the canonicalized SOURCE text (not the re-serialized rows) — the same
            // canonicalization WriteToFileAsync hashes on save, so a round-tripped, unedited
            // document reliably reads back as "unchanged" instead of drifting on formatting alone.
            var canonicalSource = await Task.Run(() => JsonDiffHelper.CanonicalizeForDiff(json));
            await LoadFormulaSidecarAsync(path, canonicalSource);

            return true;
        }
        catch (JsonException ex)
        {
            var location = ex.LineNumber.HasValue
                ? $" (строка {ex.LineNumber + 1}, позиция {ex.BytePositionInLine + 1})"
                : "";
            var msg = $"{Localizer.Get("InvalidJsonMsg")}{location}\n\n{ex.Message}";
            await _dialogService.ShowMessageAsync(Localizer.Get("ImportTitle"), msg);
            return false;
        }
        finally
        {
            IsBusy = false;
            _isLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(Localizer.Get("ExportTitle"), Localizer.Get("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter> { new("JSON and TXT files", new[] { "*.json", "*.txt" }) };
        var path = await _fileDialogService.SaveFileAsync("Export JSON", filters, Header);
        if (path == null) return;

        var json = await Task.Run(() => _jsonService.SerializeToJson(Rows, Properties));
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        NotifySuccess(Localizer.Get("ExportedMsg", Path.GetFileName(path)));
    }
    
    //  Save / Save As  (write back to the bound file)

    /// <summary>Ctrl+S: write to the bound file, or fall back to Save As for an unbound tab.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (FilePath == null || !File.Exists(FilePath))
        {
            await SaveAsAsync();
            return;
        }

        var (success, content) = await TryBuildSaveContentAsync();
        if (!success) return;

        // Warn if the file was changed on disk since we last loaded or saved it.
        var current = SafeGetWriteTime(FilePath);
        if (_fileWriteTimeUtc.HasValue && current.HasValue &&
            current.Value > _fileWriteTimeUtc.Value.AddSeconds(1))
        {
            var overwrite = await _dialogService.ShowConfirmAsync(
                Localizer.Get("ExternalChangeTitle"),
                Localizer.Get("ExternalChangeMsg", Path.GetFileName(FilePath)));
            if (!overwrite) return;
        }

        if (!await ConfirmSaveDiffAsync(content)) return;

        await WriteToFileAsync(FilePath, content);
    }

    /// <summary>Ctrl+Shift+S: pick a path (defaulting to the explorer folder) and bind the tab to it.</summary>
    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var (success, content) = await TryBuildSaveContentAsync();
        if (!success) return;

        if (!await ConfirmSaveDiffAsync(content)) return;

        var filters = new List<FileFilter> { new("JSON and TXT files", new[] { "*.json", "*.txt" }) };
        var suggestedName = FilePath != null
            ? Path.GetFileName(FilePath)
            : (string.IsNullOrWhiteSpace(Header) ? "data" : Header) + ".json";
        var directory = GetProjectRoot?.Invoke();

        var path = await _fileDialogService.SaveFileAsync(Localizer.Get("SaveAsTitle"), filters, suggestedName, directory);
        if (path == null) return;

        await WriteToFileAsync(path, content);
    }

    private async Task WriteToFileAsync(string path, string content)
    {
        try
        {
            var canonical = await Task.Run(() => JsonDiffHelper.CanonicalizeForDiff(content));

            if (_formulaSession.HasAnyFormulas)
            {
                var sidecarJson = _formulaSession.PrepareForSave(Path.GetFileName(path), canonical)!;
                var sidecarPath = SidecarFileIO.PathFor(path);
                var result = await SaveTransaction.ExecuteAsync(path, content, sidecarPath, sidecarJson);
                if (!result.Success)
                {
                    NotifyError(result.FailureMessage ?? Localizer.Get("SaveFailedMsg", ""));
                    if (!result.MainFileWritten) return;
                }
            }
            else
            {
                await File.WriteAllTextAsync(path, content, Encoding.UTF8);
            }

            FilePath = path;
            Header = Path.GetFileNameWithoutExtension(path);
            _fileWriteTimeUtc = SafeGetWriteTime(path);
            IsModified = false;
            _baselineJsonText = canonical;
            NotifySuccess(Localizer.Get("SavedMsg", Path.GetFileName(path)));
            FileSaved?.Invoke(path);
        }
        catch (Exception ex)
        {
            NotifyError(Localizer.Get("SaveFailedMsg", ex.Message));
        }
    }

    /// <summary>Builds the JSON text to persist, validating raw text when in JSON-editor mode.</summary>
    private async Task<(bool Success, string Content)> TryBuildSaveContentAsync()
    {
        if (IsJsonEditorMode)
        {
            try
            {
                await Task.Run(() => JsonDocument.Parse(RawJsonText).Dispose());
            }
            catch (JsonException ex)
            {
                NotifyError($"{Localizer.Get("InvalidJsonError")}: {ex.Message}");
                return (false, string.Empty);
            }
            return (true, RawJsonText);
        }

        if (Properties.Count == 0)
        {
            NotifyWarning(Localizer.Get("NothingToSaveMsg"));
            return (false, string.Empty);
        }

        var content = await Task.Run(() => _jsonService.SerializeToJson(Rows, Properties));
        return (true, content);
    }

    /// <summary>Canonical JSON for the data currently in memory (table or JSON mode), used as the
    /// "new" side of a diff.</summary>
    internal async Task<string> GetCurrentCanonicalJsonAsync()
    {
        if (IsJsonEditorMode)
            return await Task.Run(() => JsonDiffHelper.CanonicalizeForDiff(RawJsonText));

        return await Task.Run(() => _jsonService.SerializeToJson(Rows, Properties));
    }

    /// <summary>Canonical JSON of the last state written to disk (or of the originally imported
    /// file, if this tab has never been saved) — the "before" side of any diff against this tab.</summary>
    internal string? BaselineJsonSnapshot => _baselineJsonText;

    // CanExecute has to be synchronous, so it uses the cheap dirty flag. The command body then
    // does the real canonical comparison; if the edits happen to net out to no change, the window
    // simply opens on its "no changes" empty state rather than the button going stale.
    private bool CanShowChanges() => IsModified;

    /// <summary>Opens the non-modal "show changes" window: last saved on disk vs. what's in memory now.</summary>
    [RelayCommand(CanExecute = nameof(CanShowChanges))]
    private async Task ShowChangesAsync()
    {
        string current;
        IsBusy = true;
        try
        {
            current = await GetCurrentCanonicalJsonAsync();
        }
        finally
        {
            IsBusy = false;
        }

        var baseline = _baselineJsonText ?? string.Empty;

        await _dialogService.ShowJsonChangesWindowAsync(
            Localizer.Get("ShowChangesTitle", Header),
            Localizer.Get("DiffLabelSaved"),
            Localizer.Get("DiffLabelCurrent"),
            baseline,
            current);
    }

    partial void OnIsModifiedChanged(bool value) => ShowChangesCommand.NotifyCanExecuteChanged();

    /// <summary>Gate shown right before writing to disk, unless the user previously opted out.
    /// Returns false if the user cancels the save from the diff view.</summary>
    private async Task<bool> ConfirmSaveDiffAsync(string content)
    {
        if (!global::CraftHub.Properties.Settings.Default.ShowDiffOnSave) return true;

        var current = await Task.Run(() => JsonDiffHelper.CanonicalizeForDiff(content));
        var baseline = _baselineJsonText ?? string.Empty;
        if (baseline == current) return true; // nothing actually changed, nothing to confirm

        var result = await _dialogService.ShowJsonDiffAsync(
            Localizer.Get("SaveDiffTitle"),
            Localizer.Get("DiffLabelSaved"),
            Localizer.Get("DiffLabelCurrent"),
            baseline,
            current);
        if (result.DontShowAgain)
        {
            global::CraftHub.Properties.Settings.Default.ShowDiffOnSave = false;
            global::CraftHub.Properties.Settings.Default.Save();
        }
        return result.Proceed;
    }

    [RelayCommand]
    private async Task ImportClassAsync()
    {
        var filters = new List<FileFilter> { new("C# files", new[] { "*.cs" }) };
        var paths = await _fileDialogService.OpenMultipleFilesAsync("Import C# Class", filters);
        if (paths.Count == 0) return;

        await ImportClassFromPathAsync(paths[0]);

        for (int i = 1; i < paths.Count; i++)
        {
            var newVm = RequestNewWorkspace?.Invoke();
            if (newVm == null)
            {
                NotifyError(Localizer.Get("TabLimitReachedMsg", 15));
                break;
            }

            await newVm.ImportClassFromPathAsync(paths[i]);
        }
    }

    /// <summary>
    /// Импортирует один C# файл в этот workspace.
    /// Возвращает false, если импорт был отменён или не найден ни один класс.
    /// </summary>
    public async Task<bool> ImportClassFromPathAsync(string path)
    {
        if (Properties.Count > 0 || Rows.Count > 0)
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                Localizer.Get("ImportOverwriteTitle"),
                Localizer.Get("ImportOverwriteMsg"));
            if (!confirmed) return false;
        }

        var code = await File.ReadAllTextAsync(path);
        var allClasses = _classParserService.ParseAllClasses(code);
        var fileName = Path.GetFileName(path);

        if (allClasses.Count == 0)
        {
            await _dialogService.ShowMessageAsync(
                Localizer.Get("ImportTitle"),
                Localizer.Get("NoClassesFoundMsg"));
            return false;
        }

        string className;
        List<JsonPropertyDefinition> parsedProps;

        if (allClasses.Count == 1)
        {
            (className, parsedProps) = allClasses[0];
        }
        else
        {
            var classNames = allClasses.ConvertAll(c => c.className);
            var selected = await _dialogService.ShowSelectDialogAsync(
                Localizer.Get("SelectClassTitle"),
                Localizer.Get("SelectClassMsg"),
                fileName,
                classNames);
            if (selected == null) return false;
            (className, parsedProps) = allClasses.Find(c => c.className == selected);
        }

        if (parsedProps.Count == 0)
        {
            await _dialogService.ShowMessageAsync(
                Localizer.Get("ImportTitle"),
                Localizer.Get("NoPropsFoundMsg"));
            return false;
        }

        _isLoading = true;
        try
        {
            Properties.Clear();
            Properties.AddRange(parsedProps);

            Rows.Clear();
            Header = className;
            UndoRedo.Clear(); // destructive — clear history
            NotifySuccess(Localizer.Get("ImportedClassMsg", className, Properties.Count));
            FireColumnsChanged();
        }
        finally
        {
            _isLoading = false;
        }

        return true;
    }

    [RelayCommand]
    private async Task ExportClassAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(Localizer.Get("ExportTitle"), Localizer.Get("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter> { new("C# files", new[] { "*.cs" }) };
        var path = await _fileDialogService.SaveFileAsync("Export C# Class", filters, Header);
        if (path == null) return;

        var className = Path.GetFileNameWithoutExtension(path);
        var code = _classParserService.GenerateClassCode(className, Properties);
        await File.WriteAllTextAsync(path, code, Encoding.UTF8);
        Header = className;
        NotifySuccess(Localizer.Get("ExportedClassMsg", className));
    }

    // -----------------------------------------------------------------------
    //  JSON editor mode toggle
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task SwitchToJsonEditorAsync()
    {
        IsBusy = true;
        try
        {
            RawJsonText = Rows.Count > 0 && Properties.Count > 0
                ? await Task.Run(() => _jsonService.SerializeToJson(Rows, Properties))
                : Properties.Count > 0
                    ? "[]"
                    : "{}";
        }
        finally
        {
            IsBusy = false;
        }

        JsonEditorError = string.Empty;
        IsJsonEditorErrorVisible = false;
        IsJsonEditorMode = true;
    }

    /// <summary>Reformats the raw JSON with indentation (readable form).</summary>
    [RelayCommand]
    private Task PrettifyJsonAsync() => ReformatJsonAsync(indented: true);

    /// <summary>Collapses the raw JSON to a single line (compact form).</summary>
    [RelayCommand]
    private Task MinifyJsonAsync() => ReformatJsonAsync(indented: false);

    private async Task ReformatJsonAsync(bool indented)
    {
        if (string.IsNullOrWhiteSpace(RawJsonText)) return;
        IsBusy = true;
        try
        {
            var formatted = await Task.Run(() =>
            {
                using var doc = JsonDocument.Parse(RawJsonText);
                var options = new JsonSerializerOptions
                {
                    WriteIndented = indented,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                // A fully-minified top-level array collapses into a single multi-million-character
                // line for large datasets, which crashes the editor's text shaping/highlighting.
                // Keep one compact element per line instead so "minify" stays safe at any size.
                if (!indented && doc.RootElement.ValueKind == JsonValueKind.Array &&
                    doc.RootElement.GetArrayLength() > 1)
                {
                    var sb = new StringBuilder();
                    sb.Append('[');
                    var first = true;
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (!first) sb.Append(',');
                        sb.Append('\n').Append(JsonSerializer.Serialize(element, options));
                        first = false;
                    }
                    sb.Append('\n').Append(']');
                    return sb.ToString();
                }

                return JsonSerializer.Serialize(doc.RootElement, options);
            });
            RawJsonText = formatted;
            JsonEditorError = string.Empty;
            IsJsonEditorErrorVisible = false;
            JsonEditorErrorLine = -1;
        }
        catch (JsonException ex)
        {
            IsJsonEditorErrorVisible = true;
            JsonEditorErrorLine = ex.LineNumber ?? -1;
            JsonEditorError = $"{Localizer.Get("InvalidJsonError")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SwitchToTableEditorAsync()
    {
        if (string.IsNullOrWhiteSpace(RawJsonText))
        {
            IsJsonEditorMode = false;
            return;
        }

        IsBusy = true;
        try
        {
            var rawJson = RawJsonText;

            // Always detect fields from JSON and add any that are not yet in the schema.
            // This covers both the "empty schema" case and the "user added new fields in JSON mode" case.
            // DetectFields returns a tree, so the nested fields the user expanded during import
            // have to be recovered from the current schema — otherwise they'd be dropped here.
            var detected = await Task.Run(() =>
            {
                JsonDocument.Parse(rawJson).Dispose();
                return _jsonService.DetectFields(rawJson);
            });
            var schemaNames = Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var effective = ResolveDetectedFields(detected, schemaNames);
            var effectiveNames = effective.Select(f => f.FieldName).ToHashSet(StringComparer.Ordinal);

            var toRemove = Properties.Where(p => !effectiveNames.Contains(p.Name)).ToList();
            foreach (var p in toRemove)
                Properties.Remove(p);

            var existingNames = Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var newFields = effective
                .Where(f => !existingNames.Contains(f.FieldName))
                .Select(f => new JsonPropertyDefinition { Name = f.FieldName, FieldType = f.SelectedType });
            Properties.AddRange(newFields);

            var rows = await Task.Run(() => _jsonService.ParseJsonData(rawJson, Properties));
            Rows.Clear();
            Rows.AddRange(rows);

            UndoRedo.Clear();
            JsonEditorError = string.Empty;
            IsJsonEditorErrorVisible = false;
            JsonEditorErrorLine = -1;
            IsJsonEditorMode = false;
            MarkDirty();
            FireColumnsChanged();
            NotifySuccess(Localizer.Get("JsonAppliedMsg"));
        }
        catch (JsonException ex)
        {
            IsJsonEditorErrorVisible = true;
            JsonEditorErrorLine = ex.LineNumber ?? -1;
            JsonEditorError = $"{Localizer.Get("InvalidJsonError")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Flattens a detected field tree into the fields that should become columns.
    /// A nested object is used as a single column unless the current schema still refers to
    /// one of its descendants — that means the user expanded it during import, so we keep it
    /// expanded and take the descendants instead.
    /// </summary>
    private static List<JsonFieldMapping> ResolveDetectedFields(
        IEnumerable<JsonFieldMapping> roots, HashSet<string> schemaNames)
    {
        var result = new List<JsonFieldMapping>();
        Walk(roots);
        return result;

        void Walk(IEnumerable<JsonFieldMapping> level)
        {
            foreach (var node in level)
            {
                if (!schemaNames.Contains(node.FieldName) && HasKnownDescendant(node))
                {
                    node.IsExpanded = true;
                    Walk(node.Children);
                }
                else
                {
                    result.Add(node);
                }
            }
        }

        bool HasKnownDescendant(JsonFieldMapping node) =>
            node.Children.Any(c => schemaNames.Contains(c.FieldName) || HasKnownDescendant(c));
    }

    //  Other commands

    [RelayCommand]
    private async Task Close()
    {
        var result = await _dialogService.ShowConfirmAsync(Localizer.Get("CloseWorkspaceTitle"),
            IsModified ? Localizer.Get("CloseWorkspaceUnsavedMsg") : Localizer.Get("CloseWorkspaceMsg"));
        if (result)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        var newName = await _dialogService.ShowInputDialogAsync(
            Localizer.Get("RenameWorkspaceTitle"), Localizer.Get("RenameWorkspacePrompt"), Header,
            Localizer.Get("WorkspaceNameLabel"));

        if (newName == null) return;

        if (string.IsNullOrWhiteSpace(newName))
        {
            NotifyWarning(Localizer.Get("WorkspaceNameEmpty"));
            return;
        }

        var trimmed = newName.Trim();

        // If this tab is bound to a real file, renaming the tab renames the file on disk too.
        if (FilePath != null && File.Exists(FilePath))
        {
            await RenameBoundFileAsync(trimmed);
            return;
        }

        if (trimmed == Header) return;

        Header = trimmed;
        NotifySuccess(Localizer.Get("WorkspaceRenamedMsg", Header));
    }

    /// <summary>Renames the bound file on disk (keeping its extension unless the user typed one).</summary>
    private async Task RenameBoundFileAsync(string newName)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        var targetName = Path.HasExtension(newName)
            ? newName
            : newName + Path.GetExtension(FilePath);

        if (targetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            NotifyError(Localizer.Get("InvalidFileNameMsg"));
            return;
        }

        var newPath = Path.Combine(directory, targetName);

        // No change (same name) — nothing to do.
        if (string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(FilePath!), StringComparison.OrdinalIgnoreCase))
            return;

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            NotifyError(Localizer.Get("FileExistsMsg", targetName));
            return;
        }

        try
        {
            File.Move(FilePath!, newPath);
            SidecarFileIO.TagAlong(FilePath!, newPath, move: true);
        }
        catch (Exception ex)
        {
            NotifyError(Localizer.Get("SaveFailedMsg", ex.Message));
            return;
        }

        FilePath = newPath;
        _fileWriteTimeUtc = SafeGetWriteTime(newPath);
        Header = Path.GetFileNameWithoutExtension(newPath);
        FileSaved?.Invoke(newPath); // refresh the explorer tree
        NotifySuccess(Localizer.Get("FileRenamedMsg", targetName));
    }
}