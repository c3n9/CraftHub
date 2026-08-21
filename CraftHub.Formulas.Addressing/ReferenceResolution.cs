using System.Collections.Generic;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Addressing;

/// <summary>What a reference AST node resolves to, given a table shape and current cell: exactly
/// one path, several (a range/band/whole column), or a reason it couldn't be resolved at all
/// (out-of-bounds row/column, unknown column key — always #REF!, since Addressing has no way to
/// tell "never existed" apart from "used to exist and was removed").</summary>
public abstract record ReferenceResolution
{
    public sealed record Single(JsonPath Path) : ReferenceResolution;
    public sealed record Multiple(IReadOnlyList<JsonPath> Paths) : ReferenceResolution;
    public sealed record Failed(FormulaError Error) : ReferenceResolution;
}
