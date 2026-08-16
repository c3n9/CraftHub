namespace CraftHub.Formulas.Values;

public enum FormulaValueKind
{
    /// <summary>The key was absent from the source document. Distinct from <see cref="Null"/> —
    /// see docs/TYPES.md.</summary>
    Missing,

    /// <summary>An explicit JSON <c>null</c>.</summary>
    Null,

    Number,
    Boolean,
    Text,
    Array,
    Object,
    Error
}
