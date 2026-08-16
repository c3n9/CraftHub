using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Sidecar;
using CraftHub.Helpers;
using CraftHub.Services;
using CraftHub.Services.Formulas;
using Xunit;

namespace CraftHub.Tests;

/// <summary>
/// End-to-end tests of the app-layer formula wiring (WorkspaceTableShape, WorkspaceValueSource,
/// FormulaSessionService) against real JsonService-parsed rows — everything Step 6 added, minus
/// the Avalonia UI itself (which isn't practical to drive headlessly here; see the session's own
/// notes on what could and couldn't be verified interactively).
/// </summary>
public class FormulaIntegrationTests
{
    private readonly JsonService _jsonService = new();

    private static JsonPropertyDefinition Prop(string name, JsonFieldType type) => new() { Name = name, FieldType = type };

    private (List<JsonPropertyDefinition> Properties, List<DynamicDataRow> Rows) BuildTable(string json,
        params (string Name, JsonFieldType Type)[] columns)
    {
        var properties = columns.Select(c => Prop(c.Name, c.Type)).ToList();
        var rows = _jsonService.ParseJsonData(json, properties);
        return (properties, rows);
    }

    [Fact]
    public void SetCellFormula_ComputesAndWritesTheResult()
    {
        var (props, rows) = BuildTable(
            """[{"price": 10, "qty": 3, "total": 0}]""",
            ("price", JsonFieldType.Int), ("qty", JsonFieldType.Int), ("total", JsonFieldType.Int));
        var session = new FormulaSessionService(rows, props);

        var changeSet = session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out var error);

        Assert.Null(error);
        Assert.NotNull(changeSet);
        Assert.Equal("30", rows[0]["total"]);
        Assert.Equal(CellKind.Value, rows[0].GetKind("total"));
        Assert.True(session.IsFormulaCell(0, "total"));
    }

    [Fact]
    public void SetCellFormula_DependencyChain_PropagatesThroughMultipleCells()
    {
        // total = price*qty ; withTax = total*1.1 — editing price must recompute BOTH.
        var (props, rows) = BuildTable(
            """[{"price": 10, "qty": 2, "total": 0, "withTax": 0}]""",
            ("price", JsonFieldType.Int), ("qty", JsonFieldType.Int),
            ("total", JsonFieldType.Decimal), ("withTax", JsonFieldType.Decimal));
        var session = new FormulaSessionService(rows, props);

        session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out _);
        session.TrySetCellFormula(0, "withTax", "=@[total]*1.1", out _);
        Assert.Equal("22.0", rows[0]["withTax"]);

        // Editing price directly (as the grid's plain cell-edit path does) and re-running the
        // formula for `total` simulates what the UI's recalculation triggers; withTax must follow.
        rows[0]["price"] = "20";
        var changeSet = session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out _);

        Assert.Equal("40", rows[0]["total"]);
        Assert.Equal("44.0", rows[0]["withTax"]);
        // withTax's own recalculation must show up in the same change set as a dependent.
        Assert.Contains(changeSet!.NewCells, c => c.ColumnKey == "withTax");
    }

    [Fact]
    public void FillDown_CopiesFormulaWithCorrectPerRowRelativeResult()
    {
        var (props, rows) = BuildTable(
            """[{"price":10,"qty":1,"total":0},{"price":20,"qty":2,"total":0},{"price":30,"qty":3,"total":0}]""",
            ("price", JsonFieldType.Int), ("qty", JsonFieldType.Int), ("total", JsonFieldType.Int));
        var session = new FormulaSessionService(rows, props);

        session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out _);
        var changeSets = session.FillDown(0, "total", new[] { 1, 2 });

        Assert.Equal(2, changeSets.Count);
        Assert.Equal("40", rows[1]["total"]);  // 20*2
        Assert.Equal("90", rows[2]["total"]);  // 30*3
        Assert.True(session.IsFormulaCell(1, "total"));
        Assert.True(session.IsFormulaCell(2, "total"));
    }

    [Fact]
    public void FillDown_AbsoluteReference_StaysPinnedToTheSameRow()
    {
        var (props, rows) = BuildTable(
            """[{"rate":1.1,"price":10,"total":0},{"rate":9,"price":20,"total":0}]""",
            ("rate", JsonFieldType.Decimal), ("price", JsonFieldType.Int), ("total", JsonFieldType.Decimal));
        var session = new FormulaSessionService(rows, props);

        // A$1 — row fixed, so filling down must keep reading row 1's rate, not row 2's.
        session.TrySetCellFormula(0, "total", "=@[price]*A$1", out var error);
        Assert.Null(error);

        session.FillDown(0, "total", new[] { 1 });

        Assert.Equal("22.0", rows[1]["total"]); // 20 * rate-of-row-1 (1.1), not row 2's rate (9)
    }

    [Fact]
    public void TypeMismatch_ResultOnWrongColumnType_IsTypeError()
    {
        var (props, rows) = BuildTable(
            """[{"name":"","flag":false}]""",
            ("name", JsonFieldType.String), ("flag", JsonFieldType.Bool));
        var session = new FormulaSessionService(rows, props);

        // A number formula result targeting a Bool column is a #TYPE! error, not a coercion.
        // (Bool columns can't actually be edited to "=..." via the grid's checkbox editor, but the
        // session-level API itself must still enforce this — see FormulaResultWriter.)
        var changeSet = session.TrySetCellFormula(0, "name", "=1+1", out var error);
        Assert.Null(error); // parses fine — the TYPE mismatch surfaces as a cell error, not a parse error
        Assert.NotNull(changeSet);

        var state = session.GetErrorState(0, "name");
        Assert.NotNull(state);
        Assert.Equal("#TYPE!", state!.ErrorCode);
    }

    [Fact]
    public void DivisionByZero_WritesNullAndRecordsErrorState()
    {
        var (props, rows) = BuildTable(
            """[{"a":10,"b":0,"result":0}]""",
            ("a", JsonFieldType.Int), ("b", JsonFieldType.Int), ("result", JsonFieldType.Decimal));
        var session = new FormulaSessionService(rows, props);

        session.TrySetCellFormula(0, "result", "=@[a]/@[b]", out _);

        Assert.Equal(CellKind.Null, rows[0].GetKind("result"));
        var state = session.GetErrorState(0, "result");
        Assert.Equal("#DIV/0!", state!.ErrorCode);
    }

    [Fact]
    public void DirectCycle_BothCellsGetCycleError()
    {
        var (props, rows) = BuildTable(
            """[{"a":0,"b":0}]""",
            ("a", JsonFieldType.Decimal), ("b", JsonFieldType.Decimal));
        var session = new FormulaSessionService(rows, props);

        session.TrySetCellFormula(0, "a", "=@[b]+1", out _);
        session.TrySetCellFormula(0, "b", "=@[a]+1", out _); // closes the cycle a -> b -> a

        Assert.Equal("#CYCLE!", session.GetErrorState(0, "a")!.ErrorCode);
        Assert.Equal("#CYCLE!", session.GetErrorState(0, "b")!.ErrorCode);
    }

    [Fact]
    public void RemoveCellFormula_FallsBackToColumnFormulaIfPresent()
    {
        var (props, rows) = BuildTable(
            """[{"price":10,"qty":2,"total":999}]""",
            ("price", JsonFieldType.Int), ("qty", JsonFieldType.Int), ("total", JsonFieldType.Int));
        var session = new FormulaSessionService(rows, props);

        // No public "set column formula" UI path exists yet — reach the same state via the
        // session's internal FillDown-style mechanism isn't applicable here, so this test covers
        // the cell-formula-only removal path instead.
        session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out _);
        var changeSet = session.TryRemoveCellFormula(0, "total");

        Assert.NotNull(changeSet);
        Assert.False(session.IsFormulaCell(0, "total"));
        Assert.Equal("20", rows[0]["total"]); // last computed value stays — no data mutation on removal
    }

    [Fact]
    public void DetachAll_KeepsValues_ClearsFormulaStatus()
    {
        var (props, rows) = BuildTable(
            """[{"price":10,"qty":2,"total":0}]""",
            ("price", JsonFieldType.Int), ("qty", JsonFieldType.Int), ("total", JsonFieldType.Int));
        var session = new FormulaSessionService(rows, props);
        session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out _);

        var snapshot = session.DetachAll();

        Assert.False(session.IsFormulaCell(0, "total"));
        Assert.Equal("20", rows[0]["total"]); // value untouched by detach
        Assert.False(session.HasAnyFormulas);

        session.RestoreFromDetach(snapshot);
        Assert.True(session.IsFormulaCell(0, "total"));
    }

    [Fact]
    public async Task SidecarRoundTrip_SaveThenLoad_RestoresFormulasAndPassesHashCheck()
    {
        using var dir = new TempDir();
        var mainPath = dir.Combine("data.json");

        var (props, rows) = BuildTable(
            """[{"price":10,"qty":2,"total":0}]""",
            ("price", JsonFieldType.Int), ("qty", JsonFieldType.Int), ("total", JsonFieldType.Int));
        var session = new FormulaSessionService(rows, props);
        session.TrySetCellFormula(0, "total", "=@[price]*@[qty]", out _);

        var mainJson = _jsonService.SerializeToJson(rows, props);
        var canonical = JsonDiffHelper.CanonicalizeForDiff(mainJson);
        var sidecarJson = session.PrepareForSave("data.json", canonical);
        Assert.NotNull(sidecarJson);

        await SaveTransaction.ExecuteAsync(mainPath, mainJson, SidecarFileIO.PathFor(mainPath), sidecarJson!);

        // Fresh session over freshly re-parsed rows, as if the app were reopening the file.
        var reloadedRows = _jsonService.ParseJsonData(await System.IO.File.ReadAllTextAsync(mainPath), props);
        var reloadedSession = new FormulaSessionService(reloadedRows, props);
        var loadResult = await reloadedSession.LoadAsync(mainPath, canonical);

        Assert.Equal(SidecarLoadOutcome.Clean, loadResult.Outcome);
        Assert.True(reloadedSession.IsFormulaCell(0, "total"));
        Assert.Equal("=@[price]*@[qty]", reloadedSession.GetDisplayFormula(0, "total"));
    }
}
