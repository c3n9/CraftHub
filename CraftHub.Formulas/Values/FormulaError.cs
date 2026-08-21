namespace CraftHub.Formulas.Values;

/// <summary>An error as a first-class value: it flows through arithmetic and functions like any
/// other <see cref="FormulaValue"/> (propagates upward, doesn't throw), carrying both the fixed
/// Excel-style symbol and a human-readable explanation for the cell tooltip.</summary>
public readonly record struct FormulaError(FormulaErrorCode Code, string Message)
{
    public string Symbol => Code switch
    {
        FormulaErrorCode.Ref => "#REF!",
        FormulaErrorCode.Value => "#VALUE!",
        FormulaErrorCode.DivZero => "#DIV/0!",
        FormulaErrorCode.Name => "#NAME?",
        FormulaErrorCode.NA => "#N/A",
        FormulaErrorCode.Cycle => "#CYCLE!",
        FormulaErrorCode.Type => "#TYPE!",
        _ => "#ERROR!"
    };

    public static FormulaErrorCode? CodeForSymbol(string symbol) => symbol switch
    {
        "#REF!" => FormulaErrorCode.Ref,
        "#VALUE!" => FormulaErrorCode.Value,
        "#DIV/0!" => FormulaErrorCode.DivZero,
        "#NAME?" => FormulaErrorCode.Name,
        "#N/A" => FormulaErrorCode.NA,
        "#CYCLE!" => FormulaErrorCode.Cycle,
        "#TYPE!" => FormulaErrorCode.Type,
        _ => null
    };

    public override string ToString() => Symbol;
}
