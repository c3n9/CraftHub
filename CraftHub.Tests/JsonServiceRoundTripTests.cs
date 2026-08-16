using System.Text.Json;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Services;
using Xunit;

namespace CraftHub.Tests;

/// <summary>
/// Locks down the round-trip behavior CellKind exists to make possible: null, "", and an absent
/// key must all come back exactly as they went in, instead of collapsing onto the same empty
/// string the way they did before CellKind existed.
/// </summary>
public class JsonServiceRoundTripTests
{
    private readonly JsonService _service = new();

    private static JsonPropertyDefinition Prop(string name, JsonFieldType type = JsonFieldType.String)
        => new() { Name = name, FieldType = type };

    [Fact]
    public void ExplicitNull_RoundTripsAsNull_NotAsEmptyString()
    {
        var props = new[] { Prop("note") };
        var rows = _service.ParseJsonData("""[{"note": null}]""", props);

        Assert.Equal(CellKind.Null, rows[0].GetKind("note"));

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement[0].GetProperty("note").ValueKind);
    }

    [Fact]
    public void MissingKey_RoundTripsAsAbsent_NotAsNull()
    {
        // Second row has no "note" key at all (heterogeneous objects, as DetectFields would union).
        var props = new[] { Prop("note") };
        var rows = _service.ParseJsonData("""[{"note": "x"}, {}]""", props);

        Assert.Equal(CellKind.Value, rows[0].GetKind("note"));
        Assert.Equal(CellKind.Missing, rows[1].GetKind("note"));

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement[1].TryGetProperty("note", out _));
    }

    [Fact]
    public void EmptyString_RoundTripsAsEmptyString_NotAsNull()
    {
        var props = new[] { Prop("note") };
        var rows = _service.ParseJsonData("""[{"note": ""}]""", props);

        Assert.Equal(CellKind.Empty, rows[0].GetKind("note"));

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        var note = doc.RootElement[0].GetProperty("note");
        Assert.Equal(JsonValueKind.String, note.ValueKind);
        Assert.Equal("", note.GetString());
    }

    [Fact]
    public void EmptyString_OnNonStringColumn_FallsBackToNull_JsonHasNoEmptyNumber()
    {
        var props = new[] { Prop("qty", JsonFieldType.Int) };
        // A blank cell that was typed and cleared on an Int column.
        var rows = _service.ParseJsonData("""[{"qty": 5}]""", props);
        rows[0]["qty"] = ""; // indexer: infers Empty, as any grid edit would

        Assert.Equal(CellKind.Empty, rows[0].GetKind("qty"));

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Null, doc.RootElement[0].GetProperty("qty").ValueKind);
    }

    [Fact]
    public void IntegerValue_DoesNotBecomeFloatingPoint()
    {
        var props = new[] { Prop("qty", JsonFieldType.Int) };
        var rows = _service.ParseJsonData("""[{"qty": 5}]""", props);

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("5", doc.RootElement[0].GetProperty("qty").GetRawText());
    }

    [Fact]
    public void DecimalValue_PreservesTrailingZeros()
    {
        var props = new[] { Prop("price", JsonFieldType.Decimal) };
        var rows = _service.ParseJsonData("""[{"price": 19.90}]""", props);

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("19.90", doc.RootElement[0].GetProperty("price").GetRawText());
    }

    [Fact]
    public void DuplicatingAllThreeKindsInOneDocument_EachSurvivesIndependently()
    {
        var props = new[] { Prop("a"), Prop("b"), Prop("c") };
        var rows = _service.ParseJsonData("""[{"a": null, "b": "", "c": "x"}]""", props);

        Assert.Equal(CellKind.Null, rows[0].GetKind("a"));
        Assert.Equal(CellKind.Empty, rows[0].GetKind("b"));
        Assert.Equal(CellKind.Value, rows[0].GetKind("c"));

        var json = _service.SerializeToJson(rows, props);
        using var doc = JsonDocument.Parse(json);
        var obj = doc.RootElement[0];
        Assert.Equal(JsonValueKind.Null, obj.GetProperty("a").ValueKind);
        Assert.Equal("", obj.GetProperty("b").GetString());
        Assert.Equal("x", obj.GetProperty("c").GetString());
    }
}
