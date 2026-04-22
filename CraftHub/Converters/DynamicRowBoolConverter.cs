using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CraftHub.Converters;

public sealed class DynamicRowBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (bool.TryParse(s, out var b)) return b;
            if (s == "1") return true;
            if (s == "0") return false;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return b ? "true" : "false";
        return string.Empty;
    }
}
