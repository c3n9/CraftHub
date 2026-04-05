using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CraftHub.Models;
using Material.Icons;

namespace CraftHub.Converters;

public sealed class NotificationTypeToIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NotificationType type)
        {
            return MaterialIconKind.InformationCircleOutline;
        }

        return type switch
        {
            NotificationType.Success => MaterialIconKind.CheckCircleOutline,
            NotificationType.Warning => MaterialIconKind.AlertCircleOutline,
            NotificationType.Error => MaterialIconKind.CloseCircleOutline,
            _ => MaterialIconKind.InformationCircleOutline
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

