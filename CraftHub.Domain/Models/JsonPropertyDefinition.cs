using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Domain.Enums;

namespace CraftHub.Domain.Models;

/// <summary>
/// Defines a single property (column) in the JSON schema.
/// Supports nested children for Object/Array types.
/// </summary>
public partial class JsonPropertyDefinition : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private JsonFieldType _fieldType = JsonFieldType.String;
    [ObservableProperty] private JsonFieldType _arrayElementType = JsonFieldType.String;

    public string TypeLabel => GetTypeDisplayName(FieldType);

    partial void OnFieldTypeChanged(JsonFieldType value)
    {
        OnPropertyChanged(nameof(TypeLabel));
    }

    /// <summary>Child properties for Object type fields.</summary>
    public ObservableCollection<JsonPropertyDefinition> Children { get; } = new();

    public static string GetTypeDisplayName(JsonFieldType type) => type switch
    {
        JsonFieldType.String  => "string",
        JsonFieldType.Int     => "int",
        JsonFieldType.Float   => "float",
        JsonFieldType.Double  => "double",
        JsonFieldType.Decimal => "decimal",
        JsonFieldType.Bool    => "bool",
        JsonFieldType.Byte    => "byte",
        JsonFieldType.Short   => "short",
        JsonFieldType.Char    => "char",
        JsonFieldType.Object  => "object",
        JsonFieldType.Array   => "array",
        _ => type.ToString()
    };

    public static string GetTypeDescription(JsonFieldType type) => type switch
    {
        JsonFieldType.String  => "A sequence of characters representing text.",
        JsonFieldType.Int     => "32-bit signed integer. Range: -2,147,483,648 to 2,147,483,647.",
        JsonFieldType.Float   => "Single-precision 32-bit floating-point number.",
        JsonFieldType.Double  => "Double-precision 64-bit floating-point number.",
        JsonFieldType.Decimal => "128-bit decimal for financial calculations.",
        JsonFieldType.Bool    => "Boolean value: true or false.",
        JsonFieldType.Byte    => "8-bit unsigned integer. Range: 0 to 255.",
        JsonFieldType.Short   => "16-bit signed integer. Range: -32,768 to 32,767.",
        JsonFieldType.Char    => "Single Unicode character.",
        JsonFieldType.Object  => "Nested JSON object with child properties.",
        JsonFieldType.Array   => "JSON array of values.",
        _ => "Unknown type"
    };
}
