using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CraftHub.Converters;

/// <summary>
/// Cell background: search-match highlight takes priority; otherwise a subtle tint marks the
/// cell as belonging to a pinned (frozen) column. The owning column is captured per-instance
/// (one converter per DataGridColumn) so it can read the column's live DisplayIndex/IsFrozen
/// state whenever the bound FrozenColumnCount value changes, without a separate refresh path.
/// </summary>
public class SearchHighlightConverter : IMultiValueConverter
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x38, 0xBD, 0xF8));

    private const byte PinnedAlpha = 0x3A;

    private readonly DataGridColumn? _column;

    public SearchHighlightConverter(DataGridColumn? column = null) => _column = column;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var cellValue = values.Count > 0 ? values[0] as string : null;
        var searchQuery = values.Count > 1 ? values[1] as string : null;

        if (!string.IsNullOrEmpty(searchQuery) && !string.IsNullOrEmpty(cellValue) &&
            cellValue.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
        {
            return HighlightBrush;
        }

        // values[3] is a live "AccentPrimary" DynamicResource value resolved against the actual
        // target element, not Application.Current — Application-level resource lookups don't
        // reliably track the visually-active ThemeVariant (a control's own resolved variant can
        // differ), which previously made this pick the wrong theme's accent color.
        if (_column != null && values.Count > 3 && values[2] is int frozenCount &&
            values[3] is ISolidColorBrush accent &&
            _column.DisplayIndex >= 0 && _column.DisplayIndex < frozenCount)
        {
            return new SolidColorBrush(accent.Color, PinnedAlpha / 255.0);
        }

        return Brushes.Transparent;
    }
}
