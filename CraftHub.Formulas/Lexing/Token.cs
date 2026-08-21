using CraftHub.Formulas;

namespace CraftHub.Formulas.Lexing;

/// <summary>One lexeme. <see cref="Text"/> is the raw source slice (quotes/escapes for strings are
/// stripped into <see cref="StringValue"/> instead, since the parser and printer need both the
/// decoded value and — for round-tripping — the original span).</summary>
public readonly record struct Token(TokenKind Kind, TextSpan Span, string Text, string? StringValue = null, decimal NumberValue = default)
{
    /// <summary>True when this token starts exactly where <paramref name="previous"/> ends — no
    /// whitespace between them. Distinguishes <c>A1</c> (one reference) from <c>A 1</c> (which
    /// isn't valid syntax at all, since a bare word can't be followed by a bare number).</summary>
    public bool IsAdjacentTo(in Token previous) => Span.Start == previous.Span.End;

    public override string ToString() => $"{Kind} '{Text}' @{Span}";
}
