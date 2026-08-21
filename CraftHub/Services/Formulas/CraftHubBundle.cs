using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Sidecar;

namespace CraftHub.Services.Formulas;

/// <summary>What a <c>.crhb</c> file unpacked into: the schema, the data as ordinary JSON, and the
/// formulas that produced it.</summary>
public sealed record CraftHubBundle(
    IReadOnlyList<JsonPropertyDefinition> Properties,
    string DataJson,
    FormulaSidecar? Sidecar);

public sealed class CraftHubBundleFormatException : Exception
{
    public CraftHubBundleFormatException(string message) : base(message) { }
}

/// <summary>
/// The <c>.crhb</c> bundle: one file carrying a document and its formulas together.
///
/// It exists because the normal pairing — plain JSON plus a <c>.formulas.json</c> sidecar — is two
/// files, and two files get separated when people mail, copy or commit them. The sidecar remains
/// the format for ordinary saving, precisely because it leaves the JSON a plain JSON file that
/// anyone can read without this app. A bundle is for handing the whole thing to someone in one
/// piece, and it is only ever written by an explicit export.
///
/// The schema travels with it, which is the other reason to prefer it over a bare JSON export:
/// importing plain JSON has to ask the user what type every column is, because JSON cannot tell
/// an Int column from a Decimal one that happens to hold whole numbers. A bundle already knows.
/// </summary>
public static class CraftHubBundleIO
{
    public const string Extension = ".crhb";

    /// <summary>Bumped only if the shape changes incompatibly; <see cref="Parse"/> refuses anything
    /// newer rather than guessing at fields it does not know.</summary>
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string Serialize(
        IReadOnlyList<JsonPropertyDefinition> properties,
        string dataJson,
        FormulaSidecar? sidecar,
        string appVersion)
    {
        var schema = new JsonArray();
        foreach (var property in properties)
            schema.Add(new JsonObject
            {
                ["name"] = property.Name,
                ["type"] = property.FieldType.ToString(),
                ["arrayElementType"] = property.ArrayElementType.ToString()
            });

        var bundle = new JsonObject
        {
            ["crhb"] = CurrentVersion,
            ["generator"] = new JsonObject { ["app"] = "CraftHub", ["version"] = appVersion },
            ["savedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["schema"] = schema,
            // Stored as real JSON rather than an escaped string, so the file stays readable and a
            // bundle can be inspected (or salvaged) with any JSON tool.
            ["data"] = JsonNode.Parse(string.IsNullOrWhiteSpace(dataJson) ? "[]" : dataJson),
            ["formulas"] = sidecar is null ? null : JsonNode.Parse(SidecarJsonSerializer.Serialize(sidecar))
        };

        return bundle.ToJsonString(WriteOptions);
    }

    public static CraftHubBundle Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CraftHubBundleFormatException(ex.Message);
        }

        if (root is not JsonObject obj)
            throw new CraftHubBundleFormatException("The file is not a CraftHub bundle.");

        if (obj["crhb"] is not { } versionNode)
            throw new CraftHubBundleFormatException("The file is not a CraftHub bundle.");

        var version = versionNode.GetValue<int>();
        if (version > CurrentVersion)
            throw new CraftHubBundleFormatException(
                $"This bundle was written by a newer version of CraftHub (format {version}).");

        var properties = new List<JsonPropertyDefinition>();
        if (obj["schema"] is JsonArray schema)
            foreach (var entry in schema.OfType<JsonObject>())
            {
                var name = entry["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name)) continue;
                properties.Add(new JsonPropertyDefinition
                {
                    Name = name,
                    FieldType = ParseType(entry["type"]?.GetValue<string>()),
                    ArrayElementType = ParseType(entry["arrayElementType"]?.GetValue<string>())
                });
            }

        var dataJson = obj["data"]?.ToJsonString(WriteOptions) ?? "[]";

        FormulaSidecar? sidecar = null;
        if (obj["formulas"] is JsonObject formulas)
        {
            try
            {
                sidecar = SidecarJsonSerializer.Deserialize(formulas.ToJsonString());
            }
            catch (FormulaSidecarFormatException ex)
            {
                // The data is the valuable half; unreadable formulas must not cost the user the
                // document too, so the import continues without them and the caller says so.
                throw new CraftHubBundleFormatException($"The bundle's formulas are unreadable: {ex.Message}");
            }
        }

        return new CraftHubBundle(properties, dataJson, sidecar);
    }

    private static JsonFieldType ParseType(string? name) =>
        Enum.TryParse<JsonFieldType>(name, out var parsed) ? parsed : JsonFieldType.String;
}
