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

        // An array, not an object: this is a set of paths with nothing to say about each.
        var excluded = new JsonArray();
        foreach (var path in sidecar.ExcludedCells) excluded.Add(path);
        root["excludedCells"] = excluded;

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
            SchemaVersion = ReadInt(root["schemaVersion"], 1),
            Generator = new GeneratorInfo(
                ReadString(root["generator"]?["app"]) ?? "",
                ReadString(root["generator"]?["version"]) ?? ""),
            Target = new TargetInfo(
                RequireString(targetNode, "fileName"),
                RequireString(targetNode, "hash"),
                ReadString(targetNode["hashInput"]) ?? TargetHash.HashInputId,
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
                ReadString(obj["message"]) ?? "",
                ParseDateOr(obj["computedAtUtc"], DateTime.UtcNow));
        }

        if (root["excludedCells"] is JsonArray excludedArray)
            foreach (var node in excludedArray)
                if (ReadString(node) is { Length: > 0 } path)
                    sidecar.ExcludedCells.Add(path);

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

        var recalc = ReadString(node["recalcOnOpen"]) switch
        {
            "never" => RecalcOnOpenPolicy.Never,
            "always" => RecalcOnOpenPolicy.Always,
            _ => RecalcOnOpenPolicy.IfHashMismatch
        };

        var limitsNode = node["limits"]?.AsObject();
        var limits = limitsNode is null
            ? SidecarLimits.Default
            // Clamped on the way in: the file is untrusted, and these numbers are the runaway
            // guards themselves, so a sidecar must not be able to raise its own ceiling.
            : new SidecarLimits(
                ReadInt(limitsNode["maxRangeCells"], SidecarLimits.Default.MaxRangeCells),
                ReadInt(limitsNode["maxDepth"], SidecarLimits.Default.MaxDepth),
                ReadInt(limitsNode["formulaTimeoutMs"], SidecarLimits.Default.FormulaTimeoutMs)).Clamped();

        return new SidecarOptions(recalc, limits);
    }

    /// <summary>Reads a JSON string, treating a value of any other type as absent. A plain
    /// <c>(string?)</c> cast throws <see cref="InvalidOperationException"/> when the file holds a
    /// number or an object where a string belongs — and that exception is not a
    /// <see cref="FormulaSidecarFormatException"/>, so it escapes the policy that a corrupt sidecar
    /// is quarantined while the document still opens, and surfaces as a crash instead.</summary>
    private static string? ReadString(JsonNode? node)
    {
        if (node is null) return null;
        try
        {
            return (string?)node;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Reads an integer option, falling back to <paramref name="fallback"/> for anything
    /// that isn't one. A plain cast throws <see cref="InvalidOperationException"/> when the file
    /// holds a string or an object there, and that escapes the "corrupt sidecar is quarantined, the
    /// document still opens" policy by not being a <see cref="FormulaSidecarFormatException"/>.</summary>
    private static int ReadInt(JsonNode? node, int fallback)
    {
        if (node is null) return fallback;
        try
        {
            return (int?)node ?? fallback;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            return fallback;
        }
    }

    private static string RequireString(JsonObject obj, string key) =>
        ReadString(obj[key]) ?? throw new FormulaSidecarFormatException($"Missing or non-string field \"{key}\".");

    private static DateTime ParseDateOr(JsonNode? node, DateTime fallback)
    {
        var text = ReadString(node);
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
        "$schema", "schemaVersion", "generator", "target", "options", "columnFormulas", "cellFormulas", "state",
        "excludedCells"
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
