namespace CraftHub.Formulas.Lexing;

public enum TokenKind
{
    Number,
    String,
    /// <summary>[A-Za-z_][A-Za-z0-9_]*  — function names, TRUE/FALSE, and the letters half of an
    /// A1 column (the parser tells these apart, not the lexer).</summary>
    Word,
    /// <summary>#REF! #VALUE! #DIV/0! #NAME? #N/A #CYCLE! #TYPE! — lexed whole so the parser only
    /// has to validate the spelling once, not reconstruct it from punctuation.</summary>
    ErrorLiteral,

    Plus, Minus, Star, Slash, Caret, Percent, Ampersand,
    Eq, Ne, Lt, Gt, Le, Ge,
    LParen, RParen, Comma, Colon, Dot,
    LBracket, RBracket,
    Dollar, At,

    Eof,
    /// <summary>An unrecognized character. Carried as a token (not thrown immediately) so the
    /// parser can report it with position info consistently with every other syntax error.</summary>
    Invalid
}
