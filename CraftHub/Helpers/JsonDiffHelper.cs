using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Domain.Models;

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
    public static string CanonicalizeForDiff(string json) =>
        CanonicalizeForDiff(json, JsonCompareOptions.Default);

    /// <summary>Convenience overload for the common single-option case.</summary>
    public static string CanonicalizeForDiff(string json, bool ignoreKeyOrder) =>
        CanonicalizeForDiff(json, new JsonCompareOptions(IgnoreKeyOrder: ignoreKeyOrder));

    /// <summary>
    /// Rewrites the document so that differences the user chose to ignore disappear before any
    /// comparison runs — both the text diff and the structural one then work on this output, which
    /// is what keeps the two views agreeing with each other.
    /// <para>
    /// Case-insensitivity is applied as a fold rather than as a comparison rule, so it takes effect
    /// the same way as the other options. The trade-off is that folded strings show lower-cased in
    /// the diff — acceptable, since the alternative is the two views disagreeing on what differs.
    /// </para>
    /// </summary>
    public static string CanonicalizeForDiff(string json, JsonCompareOptions options)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (IsIdentity(options))
                return JsonSerializer.Serialize(doc.RootElement, IndentedOptions);

            // JsonElement is immutable, so any rewrite means rebuilding the tree as JsonNode.
            var transformed = Transform(doc.RootElement, options, "$");
            return transformed?.ToJsonString(IndentedOptions) ?? string.Empty;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    // Deliberately computed here from the core fields rather than via a convenience
    // property on the options type, so this stays valid however that type is shaped.
    private static bool IsIdentity(JsonCompareOptions o) =>
        !o.IgnoreKeyOrder && !o.IgnoreArrayOrder && !o.CaseInsensitiveStrings
        && !o.IgnoreNullAndEmpty && o.IgnoredPaths is not { Count: > 0 };

    private static JsonNode? Transform(JsonElement el, JsonCompareOptions options, string path)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var obj = new JsonObject();

                IEnumerable<JsonProperty> props = el.EnumerateObject();
                if (options.IgnoreKeyOrder)
                    props = props.OrderBy(p => p.Name, StringComparer.Ordinal);

                foreach (var prop in props)
                {
                    var childPath = $"{path}.{prop.Name}";
                    if (IsIgnored(childPath, options)) continue;
                    if (options.IgnoreNullAndEmpty && IsNullOrEmpty(prop.Value)) continue;

                    obj[prop.Name] = Transform(prop.Value, options, childPath);
                }

                return obj;
            }

            case JsonValueKind.Array:
            {
                var items = new List<JsonNode?>();
                var index = 0;

                foreach (var item in el.EnumerateArray())
                {
                    var childPath = $"{path}[{index++}]";
                    if (IsIgnored(childPath, options)) continue;
                    if (options.IgnoreNullAndEmpty && IsNullOrEmpty(item)) continue;

                    items.Add(Transform(item, options, childPath));
                }

                // Sorting by serialized form makes arrays compare as sets: same elements in a
                // different order end up in the same order on both sides.
                if (options.IgnoreArrayOrder)
                    items = items.OrderBy(n => n?.ToJsonString() ?? string.Empty, StringComparer.Ordinal).ToList();

                var arr = new JsonArray();
                foreach (var item in items) arr.Add(item);
                return arr;
            }

            case JsonValueKind.String when options.CaseInsensitiveStrings:
                // Case is folded rather than compared case-insensitively, so the difference is gone
                // by the time either diff sees the text.
                return JsonValue.Create(el.GetString()?.ToLowerInvariant());

            default:
                return JsonNode.Parse(el.GetRawText());
        }
    }

    private static bool IsNullOrEmpty(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrEmpty(el.GetString()),
        JsonValueKind.Array => el.GetArrayLength() == 0,
        JsonValueKind.Object => !el.EnumerateObject().Any(),
        _ => false
    };

    /// <summary>
    /// A path is ignored when it matches an entry exactly or sits underneath one, so
    /// <c>$.meta</c> also drops <c>$.meta.updatedAt</c>. Array indices are tolerated on either
    /// side: <c>$.items.id</c> matches <c>$.items[3].id</c>, which is what people expect when they
    /// type a path by hand.
    /// </summary>
    private static bool IsIgnored(string path, JsonCompareOptions options)
    {
        if (options.IgnoredPaths is not { Count: > 0 }) return false;

        var normalized = StripIndices(path);

        foreach (var raw in options.IgnoredPaths!)
        {
            var pattern = StripIndices(raw.Trim());
            if (pattern.Length == 0) continue;

            // Tolerate a pattern typed without the "$." root, e.g. "items.id" for "$.items[0].id".
            if (!pattern.StartsWith("$", StringComparison.Ordinal))
                pattern = "$." + pattern;

            if (normalized.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static string StripIndices(string path)
    {
        if (path.IndexOf('[') < 0) return path;

        var sb = new StringBuilder(path.Length);
        var depth = 0;
        foreach (var c in path)
        {
            if (c == '[') depth++;
            else if (c == ']') depth--;
            else if (depth == 0) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Longest line an AvaloniaEdit surface is given. A minified document is one enormous line,
    /// and text shaping falls over well before the file size alone would be a problem — the same
    /// failure that made "minify" crash on large files.
    /// </summary>
    public const int MaxSafeEditorLineLength = 10_000;

    /// <summary>Scans for a line longer than <paramref name="maxLineLength"/> without allocating a
    /// split of the whole document.</summary>
    public static bool HasOverlongLine(string text, int maxLineLength = MaxSafeEditorLineLength)
    {
        if (string.IsNullOrEmpty(text)) return false;

        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i - lineStart > maxLineLength) return true;
            lineStart = i + 1;
        }

        return text.Length - lineStart > maxLineLength;
    }

    /// <summary>
    /// Makes text safe to hand to an editor: a minified document gets pretty-printed so no single
    /// line is pathologically long. Text that's already reasonably wrapped is returned untouched,
    /// as is anything that doesn't parse (nothing to reformat, and the user shouldn't lose input).
    /// </summary>
    public static string PrepareForEditor(string text) =>
        HasOverlongLine(text) ? CanonicalizeForDiff(text) : text;

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
