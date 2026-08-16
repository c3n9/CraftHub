using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CraftHub.Formulas.Sidecar;

/// <summary>
/// Reads and writes the sidecar's JSON text. This is the only place that knows the on-disk detail
/// that <see cref="FormulaSidecar.ColumnFormulas"/> is keyed by bare column name in memory but by
/// <c>$[*].&lt;key&gt;</c> text in the file — everything else treats that as an implementation
/// detail of the file format, not of the model.
/// </summary>
public static class SidecarJsonSerializer
{
    private const string SchemaUrl = "https://crafthub.dev/schema/formulas-1.json";
    private const string ColumnFormulaKeyPrefix = "$[*].";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(FormulaSidecar sidecar)
    {
        var root = sidecar.UnknownFields is { } unknown ? unknown.DeepClone()!.AsObject() : new JsonObject();

        root["$schema"] = SchemaUrl;
        root["schemaVersion"] = sidecar.SchemaVersion;
        root["generator"] = new JsonObject
        {
            ["app"] = sidecar.Generator.App,
            ["version"] = sidecar.Generator.Version
        };
        root["target"] = new JsonObject
        {
            ["fileName"] = sidecar.Target.FileName,
            ["hash"] = sidecar.Target.Hash,
            ["hashInput"] = sidecar.Target.HashInput,
            ["savedAtUtc"] = sidecar.Target.SavedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        root["options"] = new JsonObject
        {
            ["recalcOnOpen"] = RecalcPolicyToString(sidecar.Options.RecalcOnOpen),
            ["errorPolicy"] = "writeNull",
            ["limits"] = new JsonObject
            {
                ["maxRangeCells"] = sidecar.Options.Limits.MaxRangeCells,
                ["maxDepth"] = sidecar.Options.Limits.MaxDepth,
                ["formulaTimeoutMs"] = sidecar.Options.Limits.FormulaTimeoutMs
            }
        };

        var columnFormulas = new JsonObject();
        foreach (var (key, entry) in sidecar.ColumnFormulas)
            columnFormulas[ColumnFormulaKeyPrefix + key] = new JsonObject { ["formula"] = entry.Formula };
        root["columnFormulas"] = columnFormulas;

        var cellFormulas = new JsonObject();
        foreach (var (path, entry) in sidecar.CellFormulas)
            cellFormulas[path] = new JsonObject { ["formula"] = entry.Formula };
        root["cellFormulas"] = cellFormulas;

        var state = new JsonObject();
        foreach (var (path, cell) in sidecar.State)
            state[path] = new JsonObject
            {
                ["error"] = cell.ErrorCode,
                ["message"] = cell.Message,
                ["computedAtUtc"] = cell.ComputedAtUtc.ToString("O", CultureInfo.InvariantCulture)
            };
        root["state"] = state;

        return root.ToJsonString(WriteOptions);
    }

    public static FormulaSidecar Deserialize(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json)?.AsObject()
                ?? throw new FormulaSidecarFormatException("The sidecar's root value is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new FormulaSidecarFormatException($"The sidecar is not valid JSON: {ex.Message}");
        }

        var targetNode = root["target"]?.AsObject()
            ?? throw new FormulaSidecarFormatException("The sidecar is missing its required \"target\" section.");

        var sidecar = new FormulaSidecar
        {
            SchemaVersion = (int?)root["schemaVersion"] ?? 1,
            Generator = new GeneratorInfo(
                (string?)root["generator"]?["app"] ?? "",
                (string?)root["generator"]?["version"] ?? ""),
            Target = new TargetInfo(
                RequireString(targetNode, "fileName"),
                RequireString(targetNode, "hash"),
                (string?)targetNode["hashInput"] ?? TargetHash.HashInputId,
                ParseDateOr(targetNode["savedAtUtc"], DateTime.UtcNow)),
            Options = ParseOptions(root["options"]?.AsObject())
        };

        foreach (var (key, node) in AsObjectOrEmpty(root["columnFormulas"]))
        {
            if (!key.StartsWith(ColumnFormulaKeyPrefix, StringComparison.Ordinal))
                throw new FormulaSidecarFormatException($"columnFormulas key \"{key}\" doesn't start with \"{ColumnFormulaKeyPrefix}\".");
            var columnKey = key[ColumnFormulaKeyPrefix.Length..];
            sidecar.ColumnFormulas[columnKey] = ParseEntry(node, key);
        }

        foreach (var (path, node) in AsObjectOrEmpty(root["cellFormulas"]))
            sidecar.CellFormulas[path] = ParseEntry(node, path);

        foreach (var (path, node) in AsObjectOrEmpty(root["state"]))
        {
            var obj = node?.AsObject() ?? throw new FormulaSidecarFormatException($"state[\"{path}\"] is not an object.");
            sidecar.State[path] = new CellState(
                RequireString(obj, "error"),
                (string?)obj["message"] ?? "",
                ParseDateOr(obj["computedAtUtc"], DateTime.UtcNow));
        }

        sidecar.UnknownFields = ExtractUnknownFields(root);
        return sidecar;
    }

    private static FormulaEntry ParseEntry(JsonNode? node, string key)
    {
        var obj = node?.AsObject() ?? throw new FormulaSidecarFormatException($"\"{key}\" is not an object.");
        return new FormulaEntry(RequireString(obj, "formula"));
    }

    private static SidecarOptions ParseOptions(JsonObject? node)
    {
        if (node is null) return SidecarOptions.Default;

        var recalc = (string?)node["recalcOnOpen"] switch
        {
            "never" => RecalcOnOpenPolicy.Never,
            "always" => RecalcOnOpenPolicy.Always,
            _ => RecalcOnOpenPolicy.IfHashMismatch
        };

        var limitsNode = node["limits"]?.AsObject();
        var limits = limitsNode is null
            ? SidecarLimits.Default
            : new SidecarLimits(
                (int?)limitsNode["maxRangeCells"] ?? SidecarLimits.Default.MaxRangeCells,
                (int?)limitsNode["maxDepth"] ?? SidecarLimits.Default.MaxDepth,
                (int?)limitsNode["formulaTimeoutMs"] ?? SidecarLimits.Default.FormulaTimeoutMs);

        return new SidecarOptions(recalc, limits);
    }

    private static string RequireString(JsonObject obj, string key) =>
        (string?)obj[key] ?? throw new FormulaSidecarFormatException($"Missing or non-string field \"{key}\".");

    private static DateTime ParseDateOr(JsonNode? node, DateTime fallback)
    {
        var text = (string?)node;
        return text is not null && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : fallback;
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> AsObjectOrEmpty(JsonNode? node) =>
        node?.AsObject() ?? new JsonObject();

    private static string RecalcPolicyToString(RecalcOnOpenPolicy policy) => policy switch
    {
        RecalcOnOpenPolicy.Never => "never",
        RecalcOnOpenPolicy.Always => "always",
        _ => "ifHashMismatch"
    };

    private static readonly HashSet<string> KnownTopLevelKeys = new(StringComparer.Ordinal)
    {
        "$schema", "schemaVersion", "generator", "target", "options", "columnFormulas", "cellFormulas", "state"
    };

    private static JsonObject? ExtractUnknownFields(JsonObject root)
    {
        JsonObject? unknown = null;
        foreach (var (key, _) in root)
        {
            if (KnownTopLevelKeys.Contains(key)) continue;
            unknown ??= new JsonObject();
            unknown[key] = root[key]?.DeepClone();
        }
        return unknown;
    }
}
