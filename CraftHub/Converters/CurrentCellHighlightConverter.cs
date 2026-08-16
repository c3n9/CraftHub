using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CraftHub.Domain.Models;

namespace CraftHub.Converters;

/// <summary>
/// Border brush for the Excel-style "active cell" indicator: transparent everywhere except the
/// one cell matching the workspace's live CurrentCellRowIndex/CurrentCellColumnKey (tracked from
/// the DataGrid's own CurrentCellChanged/SelectionChanged events). One converter instance per
/// cell, capturing that cell's own row+column identity at template-build time; the row/column
/// bindings are the live, changing part.
/// </summary>
public sealed class CurrentCellHighlightConverter : IMultiValueConverter
{
    private readonly IReadOnlyList<DynamicDataRow> _rows;
    private readonly DynamicDataRow _row;
    private readonly string _columnKey;

    public CurrentCellHighlightConverter(IReadOnlyList<DynamicDataRow> rows, DynamicDataRow row, string columnKey)
    {
        _rows = rows;
        _row = row;
        _columnKey = columnKey;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = CurrentCellRowIndex, values[1] = CurrentCellColumnKey, values[2] = a live
        // "AccentPrimary" DynamicResource value resolved against the target element — same reason
        // as SearchHighlightConverter's own note: Application.Current lookups don't reliably track
        // the actually-rendered theme variant.
        var isCurrent = values.Count > 2
            && values[0] is int rowIndex
            && values[1] is string columnKey
            && columnKey == _columnKey
            && rowIndex >= 0 && rowIndex < _rows.Count
            && ReferenceEquals(_rows[rowIndex], _row);

        return isCurrent && values[2] is ISolidColorBrush accent ? accent : Brushes.Transparent;
    }
}
