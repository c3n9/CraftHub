using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CraftHub.Models;
using System;
using System.Globalization;

namespace CraftHub.Converters;

public class NotificationTypeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is NotificationType type ? type switch
        {
            NotificationType.Success => "SuccessColor",
            NotificationType.Warning => "WarningColor",
            NotificationType.Error => "DangerColor",
            _ => "AccentPrimary"
        } : "AccentPrimary";

        var resources = Application.Current?.Resources;
        if (resources != null && resources.TryGetResource(key, null, out var resource) &&
            resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
