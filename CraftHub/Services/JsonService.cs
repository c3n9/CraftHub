using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.Services;

public class JsonService : IJsonService
{
    public List<JsonFieldMapping> DetectFields(string json)
    {
        var fields = new List<JsonFieldMapping>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement firstObj;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0) return fields;
                firstObj = root[0];
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                firstObj = root;
            }
            else return fields;

            DetectFieldsRecursive(firstObj, "", fields);
        }
        catch { /* Invalid JSON, return empty */ }
        return fields;
    }

    private void DetectFieldsRecursive(JsonElement element, string prefix, List<JsonFieldMapping> fields)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var name = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}_{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                {
                    DetectFieldsRecursive(prop.Value, name, fields);
                }
                else
                {
                    AddFieldMapping(prop.Value, name, fields);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var item in element.EnumerateArray())
            {
                var name = string.IsNullOrEmpty(prefix) ? $"<{i}>" : $"{prefix}_<{i}>";
                if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                {
                    DetectFieldsRecursive(item, name, fields);
                }
                else
                {
                    AddFieldMapping(item, name, fields);
                }
                i++;
            }
        }
    }

    private void AddFieldMapping(JsonElement el, string name, List<JsonFieldMapping> fields)
    {
        var detected = InferType(el);
        var mapping = new JsonFieldMapping
        {
            FieldName = name,
            DetectedType = detected,
            SelectedType = detected,
            SampleValue = el.ToString() ?? ""
        };
    
        fields.Add(mapping);
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
        try
        {
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
                    var current = ResolvePath(element, prop.Name);
                    
                    if (current != null)
                    {
                        var el = current.Value;
                        value = el.ValueKind switch
                        {
                            JsonValueKind.Object => el.GetRawText(),
                            JsonValueKind.Array => el.GetRawText(),
                            JsonValueKind.Null => "",
                            _ => el.ToString() ?? ""
                        };
                    }
                    row.InitializeProperty(prop.Name, value);
                }
                rows.Add(row);
            }
        }
        catch { /* Return whatever was parsed */ }
        return rows;
    }

    private JsonElement? ResolvePath(JsonElement root, string path)
    {
        var parts = path.Split('_', StringSplitOptions.RemoveEmptyEntries);
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
            var val = row[prop.Name];
            SetNestedNode(rowNode, prop.Name, val, prop.FieldType);
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

    private static void SetNestedNode(JsonObject root, string path, string val, JsonFieldType type)
    {
        var parts = path.Split('_', StringSplitOptions.RemoveEmptyEntries);
        JsonNode current = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            var nextP = parts[i + 1];
            bool nextIsArray = nextP.StartsWith("<") && nextP.EndsWith(">");

            if (p.StartsWith("<") && p.EndsWith(">"))
            {
                int idx = int.TryParse(p.Trim('<', '>'), out var j) ? j : 0;
                var array = (JsonArray)current;
                while (array.Count <= idx) array.Add(null);
                if (array[idx] == null)
                {
                    array[idx] = nextIsArray ? new JsonArray() : new JsonObject();
                }
                current = array[idx];
            }
            else
            {
                var obj = (JsonObject)current;
                if (!obj.ContainsKey(p) || obj[p] == null)
                {
                    obj[p] = nextIsArray ? new JsonArray() : new JsonObject();
                }
                current = obj[p];
            }
        }

        var leaf = parts[^1];
        var leafNode = ParsePrimitive(val, type);

        if (leaf.StartsWith("<") && leaf.EndsWith(">"))
        {
            int idx = int.TryParse(leaf.Trim('<', '>'), out var j) ? j : 0;
            var array = (JsonArray)current;
            while (array.Count <= idx) array.Add(null);
            array[idx] = leafNode;
        }
        else
        {
            var obj = (JsonObject)current;
            obj[leaf] = leafNode;
        }
    }

    private static JsonNode? ParsePrimitive(string val, JsonFieldType type)
    {
        if (string.IsNullOrEmpty(val)) return null;

        switch (type)
        {
            case JsonFieldType.Int when int.TryParse(val, out var i): return i;
            case JsonFieldType.Float when float.TryParse(val, out var f): return f;
            case JsonFieldType.Double when double.TryParse(val, out var d): return d;
            case JsonFieldType.Decimal when decimal.TryParse(val, out var m): return m;
            case JsonFieldType.Bool when bool.TryParse(val, out var b): return b;
            case JsonFieldType.Byte when byte.TryParse(val, out var by): return by;
            case JsonFieldType.Short when short.TryParse(val, out var s): return s;
            case JsonFieldType.Object or JsonFieldType.Array:
                try { return JsonNode.Parse(val); }
                catch { return val; }
            default:
                return val;
        }
    }
}
