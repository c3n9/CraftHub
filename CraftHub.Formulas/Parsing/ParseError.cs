namespace CraftHub.Formulas.Parsing;

/// <summary>A formula that fails to parse — unclosed paren, trailing comma, bad reference syntax,
/// and so on. Distinct from a <see cref="Values.FormulaError"/>: that's a runtime value a valid
/// formula can *evaluate to*; this is thrown before evaluation ever starts, because the text isn't
/// a formula at all yet.</summary>
public sealed class FormulaParseException(string message, TextSpan span) : System.Exception(message)
{
    /// <summary>Span into the expression body — i.e. relative to the text *after* the leading
    /// <c>=</c> that <see cref="FormulaParser.ParseFormula"/> strips before tokenizing.</summary>
    public TextSpan Span { get; } = span;
}
