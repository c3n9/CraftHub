using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CraftHub.Converters;

public class SearchHighlightConverter : IMultiValueConverter
{
    private static IBrush GetHighlightBrush()
    {
        return new SolidColorBrush(Color.FromArgb(0x55, 0x38, 0xBD, 0xF8));
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2)
        {
            var cellValue = values[0] as string;
            var searchQuery = values[1] as string;

            if (!string.IsNullOrEmpty(searchQuery) && !string.IsNullOrEmpty(cellValue))
            {
                if (cellValue.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    return GetHighlightBrush();
                }
            }
        }
        
        return Brushes.Transparent;
    }
}
