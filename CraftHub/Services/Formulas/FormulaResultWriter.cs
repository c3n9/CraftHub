using System.Globalization;
using CraftHub.Domain.Enums;
using CraftHub.Formulas.Values;

namespace CraftHub.Services.Formulas;

/// <summary>
/// Converts a formula's result into what actually gets written into a cell — see docs/TYPES.md:
/// "a formula never silently changes a column's declared type." A result whose kind doesn't match
/// the column's declared type (or an integer column's result isn't a whole number, or a decimal
/// magnitude doesn't fit) becomes <see cref="FormulaErrorCode.Type"/> rather than being coerced.
/// </summary>
public static class FormulaResultWriter
{
    public static bool TryConvert(FormulaValue value, JsonFieldType columnType, out string text, out CellKind kind, out FormulaError error)
    {
        text = "";
        kind = CellKind.Value;
        error = default;

        if (value.Kind == FormulaValueKind.Null)
        {
            kind = CellKind.Null;
            return true;
        }

        if (value.Kind == FormulaValueKind.Missing)
        {
            // No operation genuinely produces "no key" — the closest honest equivalent a formula
            // result can be is a blank cell of whatever type the column declares.
            kind = CellKind.Empty;
            return true;
        }

        switch (columnType)
        {
            case JsonFieldType.Int:
            case JsonFieldType.Short:
            case JsonFieldType.Byte:
                if (value.Kind != FormulaValueKind.Number) return Fail(columnType, value, out error);
                var whole = value.AsNumber;
                if (whole != decimal.Truncate(whole)) return Fail(columnType, value, out error, "the result has a fractional part");
                if (!FitsIntegerRange(whole, columnType)) return Fail(columnType, value, out error, "the result is out of range for the column type");
                text = whole.ToString(CultureInfo.InvariantCulture);
                return true;

            case JsonFieldType.Float:
            case JsonFieldType.Double:
            case JsonFieldType.Decimal:
                if (value.Kind != FormulaValueKind.Number) return Fail(columnType, value, out error);
                text = value.AsNumber.ToString(CultureInfo.InvariantCulture);
                return true;

            case JsonFieldType.Bool:
                if (value.Kind != FormulaValueKind.Boolean) return Fail(columnType, value, out error);
                text = value.AsBoolean ? "true" : "false";
                return true;

            case JsonFieldType.String:
                if (value.Kind != FormulaValueKind.Text) return Fail(columnType, value, out error);
                text = value.AsText;
                if (text.Length == 0) kind = CellKind.Empty;
                return true;

            case JsonFieldType.Char:
                if (value.Kind != FormulaValueKind.Text || value.AsText.Length != 1)
                    return Fail(columnType, value, out error, "expected exactly one character");
                text = value.AsText;
                return true;

            case JsonFieldType.Array:
                if (value.Kind != FormulaValueKind.Array) return Fail(columnType, value, out error);
                text = FormulaJsonBridge.ToJsonText(value);
                return true;

            case JsonFieldType.Object:
                if (value.Kind != FormulaValueKind.Object) return Fail(columnType, value, out error);
                text = FormulaJsonBridge.ToJsonText(value);
                return true;

            default:
                error = new FormulaError(FormulaErrorCode.Type, $"Unsupported column type {columnType}.");
                return false;
        }
    }

    private static bool FitsIntegerRange(decimal value, JsonFieldType type) => type switch
    {
        JsonFieldType.Byte => value is >= byte.MinValue and <= byte.MaxValue,
        JsonFieldType.Short => value is >= short.MinValue and <= short.MaxValue,
        _ => value is >= int.MinValue and <= int.MaxValue
    };

    private static bool Fail(JsonFieldType columnType, FormulaValue value, out FormulaError error, string? reason = null)
    {
        var detail = reason ?? $"the result is {value.TypeName}, not compatible with {JsonPropertyTypeName(columnType)}";
        error = new FormulaError(FormulaErrorCode.Type, $"Formula result doesn't fit the column's type: {detail}.");
        return false;
    }

    private static string JsonPropertyTypeName(JsonFieldType type) =>
        Domain.Models.JsonPropertyDefinition.GetTypeDisplayName(type);
}
