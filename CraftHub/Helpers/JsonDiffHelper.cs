using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CraftHub.Helpers;

/// <summary>Normalization shared by the "view changes since save" flow and the JSON comparer.</summary>
public static class JsonDiffHelper
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Re-indents JSON text for consistent line-by-line diffing. Falls back to the raw
    /// text unchanged if it isn't valid JSON — still diffable, just not reformatted.</summary>
    public static string CanonicalizeForDiff(string json) => CanonicalizeForDiff(json, ignoreKeyOrder: false);

    /// <summary>
    /// As above, but optionally rewrites every object with its keys in ordinal order, so that two
    /// documents differing only in property order compare as identical.
    /// </summary>
    public static string CanonicalizeForDiff(string json, bool ignoreKeyOrder)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!ignoreKeyOrder)
                return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);

            // JsonElement is immutable, so reordering means rebuilding the tree as JsonNode.
            var sorted = SortKeys(doc.RootElement);
            return sorted?.ToJsonString(IndentedOptions) ?? string.Empty;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonNode? SortKeys(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (var prop in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    obj[prop.Name] = SortKeys(prop.Value);
                return obj;

            case JsonValueKind.Array:
                var arr = new JsonArray();
                foreach (var item in el.EnumerateArray())
                    arr.Add(SortKeys(item));
                return arr;

            default:
                return JsonNode.Parse(el.GetRawText());
        }
    }

    /// <summary>
    /// Parses without throwing, reporting a human-readable location on failure. Used where invalid
    /// input must degrade gracefully (text diff still works, structural comparison doesn't).
    /// </summary>
    public static bool TryParseJson(string text, out JsonDocument? document, out string? error)
    {
        try
        {
            document = JsonDocument.Parse(text);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            document = null;
            error = ex.LineNumber.HasValue
                ? $"{Localizer.Get("JsonErrorLocation", ex.LineNumber + 1, ex.BytePositionInLine + 1)}: {ex.Message}"
                : ex.Message;
            return false;
        }
    }
}
