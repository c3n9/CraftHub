using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Text.Json;

namespace CraftHub.Converters;

public class JsonPreviewConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var str = value as string;
        if (string.IsNullOrWhiteSpace(str)) return str;

        try
        {
            using var doc = JsonDocument.Parse(str);
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var formatted = JsonSerializer.Serialize(doc.RootElement, options);
            
            return formatted;
        }
        catch { }

        // Fallback to returning raw text if malformed
        return str;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
