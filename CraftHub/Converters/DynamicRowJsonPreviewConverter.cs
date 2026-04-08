using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CraftHub.Domain.Models;
using CraftHub.Models;

namespace CraftHub.Converters;

public sealed class DynamicRowJsonPreviewConverter : IValueConverter
{
    private readonly JsonPreviewConverter _inner = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DynamicDataRow row && parameter is string key)
        {
            return _inner.Convert(row[key], targetType, null, culture) ?? string.Empty;
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

