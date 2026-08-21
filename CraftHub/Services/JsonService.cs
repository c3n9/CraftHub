using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.Services;

public class JsonService : IJsonService
{
    private const int MaxDepth = 50;

    /// <summary>
    /// Detects fields as a TREE: a nested object stays one field and keeps its own
    /// properties in <see cref="JsonFieldMapping.Children"/>. The import dialog decides
    /// whether it becomes a single Object column or is expanded into one column per child.
    /// </summary>
    public List<JsonFieldMapping> DetectFields(string json)
    {
        // Fields keep first-seen order at every level; the by-path lookup exists only so
        // that fields present in SOME rows are merged into the node they belong to.
        var byPath = new Dictionary<string, JsonFieldMapping>(StringComparer.Ordinal);
        var roots = new List<JsonFieldMapping>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
                if (element.ValueKind == JsonValueKind.Object)
                    DetectObjectFields(element, "", null, roots, byPath);
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            DetectObjectFields(root, "", null, roots, byPath);
        }

        return roots;
    }

    private void DetectObjectFields(JsonElement element, string prefix, JsonFieldMapping? parent,
        List<JsonFieldMapping> roots, Dictionary<string, JsonFieldMapping> byPath, int depth = 0)
    {
        if (depth > MaxDepth) return;

        foreach (var prop in element.EnumerateObject())
        {
            var name = string.IsNullOrEmpty(prefix)
                ? prop.Name
                : $"{prefix}{JsonFieldMapping.PathSeparator}{prop.Name}";

            var mapping = MergeFieldMapping(prop.Value, name, parent, roots, byPath);

            // Objects carry their properties as children so the user can expand them.
            // Arrays are always a single column: expanding them by index used to produce
            // <0>, <1>, ... columns and a union of all indices across rows.
            if (prop.Value.ValueKind == JsonValueKind.Object)
                DetectObjectFields(prop.Value, name, mapping, roots, byPath, depth + 1);
        }
    }

    /// <summary>
    /// Adds a field to the tree on first encounter and returns it.
    /// If the field was previously recorded as null/String-from-null and we now see
    /// a concrete value, upgrades the detected type.
    /// </summary>
    private JsonFieldMapping MergeFieldMapping(JsonElement el, string name, JsonFieldMapping? parent,
        List<JsonFieldMapping> roots, Dictionary<string, JsonFieldMapping> byPath)
    {
        var isNull = el.ValueKind == JsonValueKind.Null;
        var detected = InferType(el);
        var sample = isNull ? "" : (el.ToString() ?? "");

        if (byPath.TryGetValue(name, out var existing))
        {
            if (!isNull && string.IsNullOrEmpty(existing.SampleValue))
            {
                // Upgrade from null placeholder to the first concrete value we find.
                existing.DetectedType = detected;
                existing.SelectedType = detected;
                existing.SampleValue = sample;
            }

            return existing;
        }

        var mapping = new JsonFieldMapping
        {
            FieldName = name,
            DetectedType = detected,
            SelectedType = detected,
            SampleValue = sample
        };

        byPath[name] = mapping;
        if (parent == null) roots.Add(mapping);
        else parent.Children.Add(mapping);

        return mapping;
    }

    private static JsonFieldType InferType(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => JsonFieldType.String,
        JsonValueKind.True or JsonValueKind.False => JsonFieldType.Bool,
        JsonValueKind.Object => JsonFieldType.Object,
        JsonValueKind.Array => JsonFieldType.Array,
        JsonValueKind.Number => el.TryGetInt32(out _) ? JsonFieldType.Int : JsonFieldType.Double,
        _ => JsonFieldType.String
    };

    public List<DynamicDataRow> ParseJsonData(string json, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        var rows = new List<DynamicDataRow>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        IEnumerable<JsonElement> elements;
        if (root.ValueKind == JsonValueKind.Array)
            elements = root.EnumerateArray();
        else if (root.ValueKind == JsonValueKind.Object)
            elements = new[] { root };
        else
            return rows;

        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var row = new DynamicDataRow();
            foreach (var prop in properties)
            {
                string value = "";
                CellKind kind;
                var current = ResolvePath(element, prop.Name);

                if (current == null)
                {
                    // The key wasn't present on this particular object — distinct from an explicit
                    // JSON null, which ISBLANK/ISNULL (and SUM/AVERAGE skipping) both depend on.
                    kind = CellKind.Missing;
                }
                else if (current.Value.ValueKind == JsonValueKind.Null)
                {
                    kind = CellKind.Null;
                }
                else
                {
                    var el = current.Value;
                    value = el.ValueKind switch
                    {
                        JsonValueKind.Object => el.GetRawText(),
                        JsonValueKind.Array => el.GetRawText(),
                        _ => el.ToString() ?? ""
                    };
                    kind = value.Length == 0 ? CellKind.Empty : CellKind.Value;
                }

                row.InitializeProperty(prop.Name, value, kind);
            }

            rows.Add(row);
        }

        return rows;
    }

    // Single-segment paths are the common case now that nesting is preserved; multi-segment
    // paths come from fields the user expanded in the import dialog (and from schemas saved
    // before nesting was supported, which could also contain <n> array-index segments).
    private JsonElement? ResolvePath(JsonElement root, string path)
    {
        var parts = path.Split(JsonFieldMapping.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        JsonElement current = root;

        foreach (var p in parts)
        {
            if (p.StartsWith("<") && p.EndsWith(">"))
            {
                if (current.ValueKind != JsonValueKind.Array) return null;
                if (!int.TryParse(p.Trim('<', '>'), out int idx)) return null;
                if (idx < 0 || idx >= current.GetArrayLength()) return null;
                current = current[idx];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object) return null;
                if (!current.TryGetProperty(p, out current)) return null;
            }
        }

        return current;
    }

    public string SerializeToJson(IReadOnlyList<DynamicDataRow> rows, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        var arrayNode = new JsonArray();
        foreach (var row in rows)
        {
            arrayNode.Add(ConvertRowToJsonNode(row, properties));
        }

        return SerializeNode(arrayNode);
    }

    public string SerializeSingleRowToJson(DynamicDataRow row, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        var rowNode = ConvertRowToJsonNode(row, properties);
        return SerializeNode(rowNode);
    }

    private JsonObject ConvertRowToJsonNode(DynamicDataRow row, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        var rowNode = new JsonObject();
        foreach (var prop in properties)
        {
            // Missing means the key wasn't present in the source object — round-trip that as the
            // key being absent from the output too, rather than inventing a null for it. Skipping
            // the call entirely (not just the leaf) also means a parent object made up entirely of
            // Missing fields never gets created, at any nesting depth.
            var kind = row.GetKind(prop.Name);
            if (kind == CellKind.Missing) continue;

            var val = row[prop.Name];
            SetNestedNode(rowNode, prop.Name, val, prop.FieldType, kind);
        }

        return rowNode;
    }

    private string SerializeNode(JsonNode node)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        return node.ToJsonString(options);
    }

    private static void SetNestedNode(JsonObject root, string path, string val, JsonFieldType type, CellKind kind)
    {
        var parts = path.Split(JsonFieldMapping.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        JsonNode current = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            var nextP = parts[i + 1];
            bool nextIsArray = nextP.StartsWith("<") && nextP.EndsWith(">");

            if (p.StartsWith("<") && p.EndsWith(">"))
            {
                if (current is not JsonArray array) return;
                int idx = int.TryParse(p.Trim('<', '>'), out var j) ? j : 0;
                while (array.Count <= idx) array.Add(null);
                if (array[idx] == null)
                {
                    array[idx] = nextIsArray ? new JsonArray() : new JsonObject();
                }

                current = array[idx];
            }
            else
            {
                if (current is not JsonObject obj) return;
                if (!obj.ContainsKey(p) || obj[p] == null)
                {
                    obj[p] = nextIsArray ? new JsonArray() : new JsonObject();
                }

                current = obj[p];
            }
        }

        var leaf = parts[^1];
        var leafNode = ParsePrimitive(val, type, kind);

        if (leaf.StartsWith("<") && leaf.EndsWith(">"))
        {
            if (current is not JsonArray array) return;
            int idx = int.TryParse(leaf.Trim('<', '>'), out var j) ? j : 0;
            while (array.Count <= idx) array.Add(null);
            array[idx] = leafNode;
        }
        else
        {
            if (current is not JsonObject obj) return;
            obj[leaf] = leafNode;
        }
    }

    private static JsonNode? ParsePrimitive(string val, JsonFieldType type, CellKind kind)
    {
        // An explicit JSON null always stays null, regardless of type.
        if (kind == CellKind.Null) return null;

        if (kind == CellKind.Empty)
        {
            // JSON has no "empty number" or "empty bool" — only a String column can faithfully
            // hold a blank as "" rather than falling back to null.
            return type == JsonFieldType.String ? JsonValue.Create("") : null;
        }

        if (string.IsNullOrEmpty(val)) return null; // defensive: Value implies non-empty text

        switch (type)
        {
            case JsonFieldType.Int when TryInt(val, out var i): return i;
            case JsonFieldType.Float when TryFloat(val, out var f): return f;
            case JsonFieldType.Double when TryDouble(val, out var d): return d;
            case JsonFieldType.Decimal when TryDecimal(val, out var m): return m;
            case JsonFieldType.Bool when bool.TryParse(val, out var b): return b;
            case JsonFieldType.Byte when TryByte(val, out var by): return by;
            case JsonFieldType.Short when TryShort(val, out var s): return s;
            case JsonFieldType.Object or JsonFieldType.Array:
                try
                {
                    return JsonNode.Parse(val);
                }
                catch
                {
                    return val;
                }
            default:
                return val;
        }
    }

    private static bool TryInt(string s, out int v) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ||
        int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v);

    private static bool TryShort(string s, out short v) =>
        short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ||
        short.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v);

    private static bool TryByte(string s, out byte v) =>
        byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ||
        byte.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v);

    private static bool TryFloat(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ||
        float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);

    private static bool TryDouble(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ||
        double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v);

    private static bool TryDecimal(string s, out decimal v) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v) ||
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out v);

    public string SanitizeJson(string json) => SanitizeRawNewlinesInStrings(json);

    /// <summary>
    /// Scans raw JSON text char by char, tracking whether we're inside a string
    /// literal (respecting existing backslash escapes), and escapes any literal
    /// CR/LF/Tab found there. This fixes files exported with unescaped newlines
    /// inside "Content" fields, which JsonDocument.Parse would otherwise reject.
    /// </summary>
    private static string SanitizeRawNewlinesInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 64);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (escaped)
                {
                    sb.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    sb.Append(c);
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                    sb.Append(c);
                    continue;
                }

                if (c == '\n')
                {
                    sb.Append("\\n");
                    continue;
                }

                if (c == '\r')
                {
                    if (i + 1 >= json.Length || json[i + 1] != '\n')
                        sb.Append("\\n");
                    continue;
                }

                if (c == '\t')
                {
                    sb.Append("\\t");
                    continue;
                }

                sb.Append(c);
            }
            else
            {
                if (c == '"') inString = true;
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}