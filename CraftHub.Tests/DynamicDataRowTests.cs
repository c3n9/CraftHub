using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using Xunit;

namespace CraftHub.Tests;

public class DynamicDataRowTests
{
    [Fact]
    public void UnsetKey_ReadsAsMissing()
    {
        var row = new DynamicDataRow();
        Assert.Equal(CellKind.Missing, row.GetKind("price"));
        Assert.Equal("", row["price"]);
    }

    [Fact]
    public void IndexerSet_NonEmptyText_IsValue()
    {
        var row = new DynamicDataRow();
        row["price"] = "10";
        Assert.Equal(CellKind.Value, row.GetKind("price"));
    }

    [Fact]
    public void IndexerSet_EmptyText_IsEmptyNotNullNotMissing()
    {
        var row = new DynamicDataRow();
        row["price"] = "";
        Assert.Equal(CellKind.Empty, row.GetKind("price"));
    }

    [Fact]
    public void SetCell_CanExpressNull_WhichTheIndexerCannot()
    {
        var row = new DynamicDataRow();
        row.SetCell("price", "", CellKind.Null);
        Assert.Equal(CellKind.Null, row.GetKind("price"));
        Assert.Equal("", row["price"]);
    }

    [Fact]
    public void SetCell_NonEmptyValue_AlwaysNormalizesToValue_RegardlessOfRequestedKind()
    {
        // A non-empty string can never mean Null/Missing/Empty — those only exist for blank text.
        var row = new DynamicDataRow();
        row.SetCell("price", "10", CellKind.Null);
        Assert.Equal(CellKind.Value, row.GetKind("price"));
    }

    [Fact]
    public void RenameProperty_CarriesKindAcrossTheRename()
    {
        var row = new DynamicDataRow();
        row.SetCell("oldName", "", CellKind.Null);
        row.RenameProperty("oldName", "newName");
        Assert.Equal(CellKind.Null, row.GetKind("newName"));
        Assert.Equal(CellKind.Missing, row.GetKind("oldName"));
    }

    [Fact]
    public void RemoveProperty_ClearsKindToo()
    {
        var row = new DynamicDataRow();
        row.SetCell("price", "", CellKind.Null);
        row.RemoveProperty("price");
        Assert.Equal(CellKind.Missing, row.GetKind("price"));
    }

    [Fact]
    public void InitializeProperty_EmptyValue_DefaultsToEmptyKind()
    {
        var row = new DynamicDataRow();
        row.InitializeProperty("price"); // as AddRow/AddProperty call it
        Assert.Equal(CellKind.Empty, row.GetKind("price"));
    }

    [Fact]
    public void InitializeProperty_NonEmptyValue_IgnoresRequestedKind_IsAlwaysValue()
    {
        var row = new DynamicDataRow();
        row.InitializeProperty("price", "10", CellKind.Null);
        Assert.Equal(CellKind.Value, row.GetKind("price"));
    }
}
