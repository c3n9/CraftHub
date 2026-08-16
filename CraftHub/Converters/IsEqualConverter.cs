using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CraftHub.Converters;

/// <summary>True when the bound value equals the ConverterParameter. Used to show the settings
/// pane matching the selected sidebar entry — one binding per pane, no code-behind and no
/// per-section view models.</summary>
public sealed class IsEqualConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
