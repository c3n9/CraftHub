using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Addressing;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Services.Formulas;

/// <summary>
/// Resolves formula references against a workspace's live rows — the seam between the engine
/// (which knows nothing about A1 or JSON paths, only "give me the value this reference points to")
/// and <see cref="CellKind"/>/<see cref="JsonFieldType"/>, which only the app knows about. See
/// docs/TYPES.md for the mapping this implements: Missing/Null/Empty read exactly as they're
/// stored, never coerced.
/// </summary>
public sealed class WorkspaceValueSource : IValueSource
{
    private readonly A1Translator _translator = new();
    private readonly WorkspaceTableShape _shape;
    private readonly IReadOnlyList<DynamicDataRow> _rows;
    private readonly Dictionary<string, JsonFieldType> _typeByColumn;

    public WorkspaceValueSource(WorkspaceTableShape shape, IReadOnlyList<DynamicDataRow> rows, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        _shape = shape;
        _rows = rows;
        _typeByColumn = properties.ToDictionary(p => p.Name, p => p.FieldType);
    }

    public FormulaValue Resolve(FormulaAst reference, EvalContext context)
    {
        var resolution = _translator.Resolve(reference, _shape, context.CurrentCell);
        return resolution switch
        {
            ReferenceResolution.Single s => ReadPath(s.Path),
            ReferenceResolution.Multiple => FormulaValue.Of(FormulaErrorCode.Value, "This reference is a range, not a single value."),
            ReferenceResolution.Failed f => FormulaValue.Of(f.Error),
            _ => FormulaValue.Of(FormulaErrorCode.Value, "Unresolvable reference.")
        };
    }

    public IEnumerable<FormulaValue> ResolveMany(FormulaAst reference, EvalContext context)
    {
        var resolution = _translator.Resolve(reference, _shape, context.CurrentCell);
        return resolution switch
        {
            ReferenceResolution.Single s => new[] { ReadPath(s.Path) },
            ReferenceResolution.Multiple m => m.Paths.Select(ReadPath),
            ReferenceResolution.Failed f => new[] { FormulaValue.Of(f.Error) },
            _ => new[] { FormulaValue.Of(FormulaErrorCode.Value, "Unresolvable reference.") }
        };
    }

    private FormulaValue ReadPath(JsonPath path)
    {
        if (path.Segments.Count == 0 || path.Segments[0] is not JsonPathSegment.Index rowIdx)
            return FormulaValue.Of(FormulaErrorCode.Ref, "Paths outside the table (e.g. $.settings.x) aren't backed by data yet.");

        if (rowIdx.Value < 0 || rowIdx.Value >= _rows.Count)
            return FormulaValue.Of(FormulaErrorCode.Ref, "Row does not exist.");

        var columnKey = WorkspacePathCodec.ColumnKeyFor(path.Segments.Skip(1).ToList());
        if (columnKey is null || !_typeByColumn.TryGetValue(columnKey, out var type))
            return FormulaValue.Of(FormulaErrorCode.Ref, "Column does not exist.");

        return ReadCell(_rows[rowIdx.Value], columnKey, type);
    }

    public static FormulaValue ReadCell(DynamicDataRow row, string columnKey, JsonFieldType type)
    {
        var kind = row.GetKind(columnKey);
        return kind switch
        {
            CellKind.Missing => FormulaValue.Missing,
            CellKind.Null => FormulaValue.Null,
            CellKind.Empty => type is JsonFieldType.String or JsonFieldType.Char ? FormulaValue.Of("") : FormulaValue.Null,
            _ => ParseValue(row[columnKey], type)
        };
    }

    private static FormulaValue ParseValue(string text, JsonFieldType type)
    {
        switch (type)
        {
            case JsonFieldType.Int:
            case JsonFieldType.Short:
            case JsonFieldType.Byte:
            case JsonFieldType.Float:
            case JsonFieldType.Double:
            case JsonFieldType.Decimal:
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                    ? FormulaValue.Of(n)
                    : FormulaValue.Of(FormulaErrorCode.Value, $"'{text}' is not a number.");

            case JsonFieldType.Bool:
                return bool.TryParse(text, out var b)
                    ? FormulaValue.Of(b)
                    : FormulaValue.Of(FormulaErrorCode.Value, $"'{text}' is not a boolean.");

            case JsonFieldType.Object:
            case JsonFieldType.Array:
                try
                {
                    return FormulaJsonBridge.FromJsonNode(JsonNode.Parse(text));
                }
                catch (JsonException)
                {
                    return FormulaValue.Of(FormulaErrorCode.Value, "Cell does not contain valid JSON.");
                }

            default: // String, Char
                return FormulaValue.Of(text);
        }
    }
}
