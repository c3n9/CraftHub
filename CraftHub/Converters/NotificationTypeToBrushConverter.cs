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
        if (value is NotificationType type)
        {
            switch (type)
            {
                case NotificationType.Success:
                    return new SolidColorBrush(Color.FromArgb(255, 16, 185, 129));
                case NotificationType.Warning:
                    return new SolidColorBrush(Color.FromArgb(255, 245, 158, 11));
                case NotificationType.Error:
                    return new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
            }
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
