namespace CraftHub.Models;

/// <summary>
/// Supported JSON field types including nested types for complex structures.
/// </summary>
public enum JsonFieldType
{
    String,
    Int,
    Float,
    Double,
    Decimal,
    Bool,
    Byte,
    Short,
    Char,
    Object,
    Array,
}
