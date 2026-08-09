using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Domain.Models;

namespace CraftHub.Helpers;

/// <summary>
/// Emits RFC 6902 JSON Patch from a structural comparison. Unlike a unified patch, these operations
/// address values by JSON Pointer, so they survive reformatting and can be applied programmatically.
/// </summary>
public static class JsonPatchGenerator
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Returns an empty string when either side isn't valid JSON — there's no structure to
    /// address operations against.</summary>
    public static string Generate(string oldJson, string newJson)
    {
        if (!JsonDiffHelper.TryParseJson(oldJson, out var oldDoc, out _)) return string.Empty;

        using (oldDoc)
        {
            if (!JsonDiffHelper.TryParseJson(newJson, out var newDoc, out _)) return string.Empty;

            using (newDoc)
                return Generate(StructuralJsonDiff.Compare(oldDoc!.RootElement, newDoc!.RootElement));
        }
    }

    /// <summary>
    /// Operation order matters for arrays: removing element 0 shifts every later index, so removals
    /// are emitted last-index-first and additions first-index-first, keeping every pointer valid at
    /// the moment its operation is applied.
    /// <para>
    /// Each change is modelled independently — <c>move</c>/<c>copy</c> are never emitted, so a
    /// reordered array reads as a set of replaces. Correct, just more verbose than a hand-written
    /// patch would be.
    /// </para>
    /// </summary>
    public static string Generate(JsonDiffNode root)
    {
        var replaces = new List<JsonDiffNode>();
        var removes = new List<JsonDiffNode>();
        var adds = new List<JsonDiffNode>();
        Collect(root, replaces, removes, adds);

        removes.Sort((a, b) => string.CompareOrdinal(b.Pointer, a.Pointer));
        adds.Sort((a, b) => string.CompareOrdinal(a.Pointer, b.Pointer));

        var ops = new JsonArray();
        foreach (var node in replaces) ops.Add(Op("replace", node.Pointer, node.NewValue));
        foreach (var node in removes) ops.Add(Op("remove", node.Pointer, null));
        foreach (var node in adds) ops.Add(Op("add", node.Pointer, node.NewValue));

        return ops.Count == 0 ? string.Empty : ops.ToJsonString(IndentedOptions);
    }

    private static JsonObject Op(string op, string pointer, string? rawValue)
    {
        // An empty pointer denotes the document root, which is exactly what RFC 6901 specifies.
        var obj = new JsonObject { ["op"] = op, ["path"] = pointer };
        if (rawValue != null) obj["value"] = ParseRaw(rawValue);
        return obj;
    }

    /// <summary>Values arrive as raw JSON text, so they're re-parsed to embed as real JSON rather
    /// than as a quoted string.</summary>
    private static JsonNode? ParseRaw(string rawValue)
    {
        try
        {
            return JsonNode.Parse(rawValue);
        }
        catch (JsonException)
        {
            return JsonValue.Create(rawValue);
        }
    }

    private static void Collect(
        JsonDiffNode node, List<JsonDiffNode> replaces, List<JsonDiffNode> removes, List<JsonDiffNode> adds)
    {
        switch (node.ChangeType)
        {
            case JsonDiffChangeType.Replaced:
            case JsonDiffChangeType.TypeChanged:
                replaces.Add(node);
                return;
            case JsonDiffChangeType.Removed:
                removes.Add(node);
                return;
            case JsonDiffChangeType.Added:
                adds.Add(node);
                return;
        }

        // Unchanged container: only its changed descendants produce operations.
        foreach (var child in node.Children)
            Collect(child, replaces, removes, adds);
    }
}
