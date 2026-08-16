namespace CraftHub.Formulas.Values;

/// <summary>The fixed set of formula errors, matching the error-literal tokens the grammar accepts
/// (<c>#REF!</c> etc.) so an error can be typed as a literal, produced by evaluation, and printed
/// back identically.</summary>
public enum FormulaErrorCode
{
    /// <summary>#REF! — a reference points at a row, column or path that no longer exists.</summary>
    Ref,

    /// <summary>#VALUE! — an operand has the wrong kind for the operation (e.g. text in arithmetic).</summary>
    Value,

    /// <summary>#DIV/0! — division, or an equivalent operation, by zero.</summary>
    DivZero,

    /// <summary>#NAME? — an unknown function name.</summary>
    Name,

    /// <summary>#N/A — a lookup found nothing (reserved for stage-2 lookup functions).</summary>
    NA,

    /// <summary>#CYCLE! — the dependency graph found a cycle reaching this cell.</summary>
    Cycle,

    /// <summary>#TYPE! — a value's kind is fundamentally incompatible with the operation (e.g. a
    /// boolean used directly in arithmetic), as distinct from #VALUE!'s "wrong shape of the right
    /// kind of thing".</summary>
    Type
}
