namespace CraftHub.Domain.Enums;

/// <summary>
/// What a cell actually holds, distinct from its text representation — <see cref="DynamicDataRow"/>
/// stores every value as a string, so this is what tells apart a JSON <c>null</c>, an empty string,
/// a key that was never present in the source document, and a real value, all of which would
/// otherwise collapse onto the same empty string.
/// </summary>
public enum CellKind
{
    /// <summary>Ordinary value; the row's string holds its text representation.</summary>
    Value,

    /// <summary>User- or import-produced empty string. Not <see cref="Null"/>, not <see cref="Missing"/>.</summary>
    Empty,

    /// <summary>The source JSON had this key set to <c>null</c>.</summary>
    Null,

    /// <summary>The key was absent from the source object entirely (only produced by import, when a
    /// sibling row has the key but this one doesn't).</summary>
    Missing
}
