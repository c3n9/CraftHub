using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using CraftHub.Domain.Models;

namespace CraftHub.Converters;

/// <summary>
/// True for the one cell matching the workspace's live CurrentCellRowIndex/CurrentCellColumnKey —
/// the boolean twin of <see cref="CurrentCellHighlightConverter"/>, for things that appear on the
/// active cell rather than merely restyling it. That's the fill handle: Excel puts one square on
/// the selected cell, not one on every cell that happens to hold a formula.
///
/// Being a live binding is the point. Cell templates are built when the grid's rows are rebuilt,
/// not when the selection moves, so anything decided at template-build time would either have to
/// show up everywhere or force a full grid refresh on every click.
/// </summary>
public sealed class IsCurrentCellConverter : IMultiValueConverter
{
    private readonly IReadOnlyList<DynamicDataRow> _rows;
    private readonly DynamicDataRow _row;
    private readonly string _columnKey;

    public IsCurrentCellConverter(IReadOnlyList<DynamicDataRow> rows, DynamicDataRow row, string columnKey)
    {
        _rows = rows;
        _row = row;
        _columnKey = columnKey;
    }

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count > 1
        && values[0] is int rowIndex
        && values[1] is string columnKey
        && columnKey == _columnKey
        && rowIndex >= 0 && rowIndex < _rows.Count
        && ReferenceEquals(_rows[rowIndex], _row);
}
