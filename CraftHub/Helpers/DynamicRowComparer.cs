using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using System;
using System.Collections;

namespace CraftHub.Helpers;

/// <summary>
/// Custom comparer for DataGrid column sorting on DynamicDataRow.
/// Numeric field types are sorted numerically; everything else lexicographically.
/// </summary>
public sealed class DynamicRowComparer : IComparer
{
    private readonly string _propName;
    private readonly JsonFieldType _fieldType;

    public DynamicRowComparer(string propName, JsonFieldType fieldType)
    {
        _propName = propName;
        _fieldType = fieldType;
    }

    public int Compare(object? x, object? y)
    {
        var valX = (x as DynamicDataRow)?[_propName] ?? string.Empty;
        var valY = (y as DynamicDataRow)?[_propName] ?? string.Empty;

        switch (_fieldType)
        {
            case JsonFieldType.Int:
            case JsonFieldType.Short:
            case JsonFieldType.Byte:
                if (long.TryParse(valX, out var lx) && long.TryParse(valY, out var ly))
                    return lx.CompareTo(ly);
                break;

            case JsonFieldType.Float:
            case JsonFieldType.Double:
            case JsonFieldType.Decimal:
                if (double.TryParse(valX, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var dx) &&
                    double.TryParse(valY, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var dy))
                    return dx.CompareTo(dy);
                break;

            case JsonFieldType.Bool:
                // false < true
                var bx = valX.Equals("true", StringComparison.OrdinalIgnoreCase);
                var by = valY.Equals("true", StringComparison.OrdinalIgnoreCase);
                return bx.CompareTo(by);
        }

        // String, Char, Object, Array — lexicographic, case-insensitive
        return string.Compare(valX, valY, StringComparison.OrdinalIgnoreCase);
    }
}
