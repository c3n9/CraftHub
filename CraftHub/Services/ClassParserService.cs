using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.Services;

public class ClassParserService : IClassParserService
{
    private static readonly Regex ClassNameRegex = new(
        @"class\s+(\w+)", RegexOptions.Compiled);

    private static readonly Regex PropertyRegex = new(
        @"public\s+(?:virtual\s+)?(\w+(?:<[^>]+>)?(?:\[\])?(?:\?)?)\s+(\w+)\s*\{\s*get\s*;",
        RegexOptions.Compiled);

    public (string className, List<JsonPropertyDefinition> properties) ParseClassFile(string code)
    {
        var all = ParseAllClasses(code);
        return all.Count > 0 ? all[0] : ("Unknown", new List<JsonPropertyDefinition>());
    }

    public List<(string className, List<JsonPropertyDefinition> properties)> ParseAllClasses(string code)
    {
        var classMatches = ClassNameRegex.Matches(code);
        if (classMatches.Count == 0)
            return new List<(string, List<JsonPropertyDefinition>)>();

        var propMatches = PropertyRegex.Matches(code);
        var result = new List<(string, List<JsonPropertyDefinition>)>();

        for (var i = 0; i < classMatches.Count; i++)
        {
            var classMatch = classMatches[i];
            var className = classMatch.Groups[1].Value;
            var classStart = classMatch.Index;
            var classEnd = i + 1 < classMatches.Count ? classMatches[i + 1].Index : code.Length;

            var properties = new List<JsonPropertyDefinition>();
            foreach (Match propMatch in propMatches)
            {
                if (propMatch.Index < classStart || propMatch.Index >= classEnd) continue;
                var typeName = propMatch.Groups[1].Value;
                var propName = propMatch.Groups[2].Value;
                properties.Add(new JsonPropertyDefinition
                {
                    Name = propName,
                    FieldType = MapCSharpType(typeName),
                });
            }

            result.Add((className, properties));
        }

        return result;
    }

    private static JsonFieldType MapCSharpType(string csharpType)
    {
        var normalized = csharpType.TrimEnd('?');
        return normalized.ToLowerInvariant() switch
        {
            "int" or "int32" or "system.int32" => JsonFieldType.Int,
            "float" or "single" or "system.single" => JsonFieldType.Float,
            "double" or "system.double" => JsonFieldType.Double,
            "decimal" or "system.decimal" => JsonFieldType.Decimal,
            "bool" or "boolean" or "system.boolean" => JsonFieldType.Bool,
            "byte" or "system.byte" => JsonFieldType.Byte,
            "short" or "int16" or "system.int16" => JsonFieldType.Short,
            "char" or "system.char" => JsonFieldType.Char,
            "string" or "system.string" => JsonFieldType.String,
            _ when normalized.Contains("[]") || normalized.StartsWith("List<")
                || normalized.StartsWith("IList<") || normalized.StartsWith("IEnumerable<")
                => JsonFieldType.Array,
            _ => JsonFieldType.Object,
        };
    }

    public string GenerateClassCode(string className, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace YourNamespace;");
        sb.AppendLine();
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");

        foreach (var prop in properties)
        {
            var csType = MapToCSharpType(prop);
            var safeName = prop.Name.Replace(".", "_");
            sb.AppendLine($"    public {csType} {safeName} {{ get; set; }}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string MapToCSharpType(JsonPropertyDefinition prop) => prop.FieldType switch
    {
        JsonFieldType.Int => "int",
        JsonFieldType.Float => "float",
        JsonFieldType.Double => "double",
        JsonFieldType.Decimal => "decimal",
        JsonFieldType.Bool => "bool",
        JsonFieldType.Byte => "byte",
        JsonFieldType.Short => "short",
        JsonFieldType.Char => "char",
        JsonFieldType.String => "string",
        JsonFieldType.Array => $"List<{MapToCSharpType(new JsonPropertyDefinition { FieldType = prop.ArrayElementType })}>",
        JsonFieldType.Object => "object",
        _ => "string"
    };
}
