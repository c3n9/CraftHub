using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CraftHub.Converters;

public class SearchHighlightConverter : IMultiValueConverter
{
    private static readonly IBrush HighlightBrush = SolidColorBrush.Parse("#44FFaa44");

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
                    return HighlightBrush;
                }
            }
        }
        
        return Brushes.Transparent;
    }
}
