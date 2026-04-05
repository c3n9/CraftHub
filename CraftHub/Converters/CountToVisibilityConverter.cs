using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace CraftHub.Converters;

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            null => 0,
            _ => 0
        };

        var mode = parameter as string;
        return mode switch
        {
            "empty" => count == 0,
            "notEmpty" => count != 0,
            _ => count != 0
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

