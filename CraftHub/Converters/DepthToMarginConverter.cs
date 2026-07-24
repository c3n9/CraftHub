using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace CraftHub.Converters;

/// <summary>
/// Turns a nesting depth into the row margin used by the import mapping dialog,
/// so children of an expanded object are visually indented under their parent.
/// </summary>
public class DepthToMarginConverter : IValueConverter
{
    private const double IndentPerLevel = 22;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int d && d > 0 ? d : 0;
        return new Thickness(depth * IndentPerLevel, 4, 15, 4);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
