using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CraftHub.Formulas.Values;

namespace CraftHub.Services.Formulas;

/// <summary>Converts between <see cref="FormulaValue"/> and <see cref="JsonNode"/> for Object/Array
/// typed columns, whose cell text is raw JSON (see JsonService.ParseJsonData). A separate,
/// deliberately duplicated copy of the same shape as the engine's own PARSEJSON/TOJSON conversion —
/// this is app-layer code bridging the grid's storage format, not engine internals, so it doesn't
/// reach into CraftHub.Formulas.Functions for it.</summary>
internal static class FormulaJsonBridge
{
    public static FormulaValue FromJsonNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return FormulaValue.Null;
            case JsonArray arr:
                return FormulaValue.Of(arr.Select(FromJsonNode).ToList());
            case JsonObject obj:
                return FormulaValue.Of(obj.ToDictionary(kv => kv.Key, kv => FromJsonNode(kv.Value)));
            case JsonValue val:
                if (val.TryGetValue<bool>(out var b)) return FormulaValue.Of(b);
                if (val.TryGetValue<decimal>(out var d)) return FormulaValue.Of(d);
                if (val.TryGetValue<string>(out var s)) return FormulaValue.Of(s);
                return FormulaValue.Of(FormulaErrorCode.Value, "Unsupported JSON value.");
            default:
                return FormulaValue.Of(FormulaErrorCode.Value, "Unsupported JSON value.");
        }
    }

    public static JsonNode? ToJsonNode(FormulaValue v) => v.Kind switch
    {
        FormulaValueKind.Null => null,
        FormulaValueKind.Number => JsonValue.Create(v.AsNumber),
        FormulaValueKind.Boolean => JsonValue.Create(v.AsBoolean),
        FormulaValueKind.Text => JsonValue.Create(v.AsText),
        FormulaValueKind.Array => new JsonArray(v.AsArray.Select(ToJsonNode).ToArray()),
        FormulaValueKind.Object => new JsonObject(v.AsObject.Select(kv =>
            new KeyValuePair<string, JsonNode?>(kv.Key, ToJsonNode(kv.Value)))),
        _ => null
    };

    public static string ToJsonText(FormulaValue v)
    {
        var node = ToJsonNode(v);
        return node is null ? "null" : node.ToJsonString();
    }
}
