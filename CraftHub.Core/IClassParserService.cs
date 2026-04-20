using CraftHub.Domain.Models;
using CraftHub.Models;

namespace CraftHub.Core;

public interface IClassParserService
{
    /// <summary>Parse the first class found in a C# file.</summary>
    (string className, List<JsonPropertyDefinition> properties) ParseClassFile(string code);

    /// <summary>Parse all classes found in a C# file.</summary>
    List<(string className, List<JsonPropertyDefinition> properties)> ParseAllClasses(string code);

    /// <summary>Generate C# class code from property definitions.</summary>
    string GenerateClassCode(string className, IReadOnlyList<JsonPropertyDefinition> properties);
}
