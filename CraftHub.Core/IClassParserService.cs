using CraftHub.Domain.Models;
using CraftHub.Models;

namespace CraftHub.Core;

public interface IClassParserService
{
    /// <summary>Parse a C# class file and extract property definitions.</summary>
    (string className, List<JsonPropertyDefinition> properties) ParseClassFile(string code);

    /// <summary>Generate C# class code from property definitions.</summary>
    string GenerateClassCode(string className, IReadOnlyList<JsonPropertyDefinition> properties);
}
