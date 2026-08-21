using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CraftHub.Formulas.Lexing;

/// <summary>Thrown for input that can't be tokenized at all (an unterminated string, or an unknown
/// <c>#...</c> error-literal spelling). Genuine grammar errors — unclosed parens, trailing commas —
/// are the parser's job; this only covers cases the lexer can't even hand off as a token.</summary>
public sealed class FormulaLexException(string message, TextSpan span) : System.Exception(message)
{
    public TextSpan Span { get; } = span;
}

/// <summary>
/// Turns formula source (the text after the leading <c>=</c>) into a flat token list. Deliberately
/// dumb about grammar — a bare word could be a function name, a column letter, or a JSON-path
/// identifier, and the lexer doesn't try to guess; <see cref="Parsing.FormulaParser"/> decides from
/// context. Reused as-is by the formula bar's syntax highlighter, which is why span accuracy on
/// every token (not just the ones the parser cares about) matters.
/// </summary>
public static class FormulaLexer
{
    public static IReadOnlyList<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        var len = source.Length;

        while (i < len)
        {
            var c = source[i];

            if (c is ' ' or '\t' or '\r' or '\n')
            {
                i++;
                continue;
            }

            var start = i;

            if (char.IsDigit(c))
            {
                i = ScanNumber(source, i, out var numberValue);
                tokens.Add(new Token(TokenKind.Number, new TextSpan(start, i - start), source[start..i], NumberValue: numberValue));
                continue;
            }

            if (c == '"')
            {
                i = ScanString(source, i, out var stringValue);
                tokens.Add(new Token(TokenKind.String, new TextSpan(start, i - start), source[start..i], stringValue));
                continue;
            }

            if (c == '#')
            {
                i = ScanErrorLiteral(source, i);
                tokens.Add(new Token(TokenKind.ErrorLiteral, new TextSpan(start, i - start), source[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                i++;
                while (i < len && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Word, new TextSpan(start, i - start), source[start..i]));
                continue;
            }

            var (kind, width) = c switch
            {
                '+' => (TokenKind.Plus, 1),
                '-' => (TokenKind.Minus, 1),
                '*' => (TokenKind.Star, 1),
                '/' => (TokenKind.Slash, 1),
                '^' => (TokenKind.Caret, 1),
                '%' => (TokenKind.Percent, 1),
                '&' => (TokenKind.Ampersand, 1),
                '(' => (TokenKind.LParen, 1),
                ')' => (TokenKind.RParen, 1),
                ',' => (TokenKind.Comma, 1),
                ':' => (TokenKind.Colon, 1),
                '.' => (TokenKind.Dot, 1),
                '[' => (TokenKind.LBracket, 1),
                ']' => (TokenKind.RBracket, 1),
                '$' => (TokenKind.Dollar, 1),
                '@' => (TokenKind.At, 1),
                '=' => (TokenKind.Eq, 1),
                '<' when i + 1 < len && source[i + 1] == '>' => (TokenKind.Ne, 2),
                '<' when i + 1 < len && source[i + 1] == '=' => (TokenKind.Le, 2),
                '<' => (TokenKind.Lt, 1),
                '>' when i + 1 < len && source[i + 1] == '=' => (TokenKind.Ge, 2),
                '>' => (TokenKind.Gt, 1),
                _ => (TokenKind.Invalid, 1)
            };

            tokens.Add(new Token(kind, new TextSpan(start, width), source.Substring(start, width)));
            i += width;
        }

        tokens.Add(new Token(TokenKind.Eof, new TextSpan(len, 0), ""));
        return tokens;
    }

    private static int ScanNumber(string s, int i, out decimal value)
    {
        var start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;

        if (i < s.Length && s[i] == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
        {
            i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
        }

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            var expStart = i;
            var j = i + 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
            if (j < s.Length && char.IsDigit(s[j]))
            {
                j++;
                while (j < s.Length && char.IsDigit(s[j])) j++;
                i = j;
            }
            else
            {
                // "1e" with no digits after — not part of the number, leave it for the next token
                // (which will fail to lex as anything useful and surface as a parse error).
                i = expStart;
            }
        }

        var text = s[start..i];
        // Decimal can't represent the full double exponent range, but formula literals are never
        // written at that scale; NumberStyles.Float + InvariantCulture keeps '.' as the decimal
        // point regardless of the host machine's locale, per the type rules.
        value = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return i;
    }

    private static int ScanString(string s, int i, out string value)
    {
        var start = i;
        i++; // opening quote
        var sb = new StringBuilder();

        while (true)
        {
            if (i >= s.Length)
                throw new FormulaLexException("Unterminated string literal.", new TextSpan(start, i - start));

            if (s[i] == '"')
            {
                if (i + 1 < s.Length && s[i + 1] == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }

                i++; // closing quote
                break;
            }

            sb.Append(s[i]);
            i++;
        }

        value = sb.ToString();
        return i;
    }

    private static readonly string[] KnownErrorSymbols =
    {
        "#REF!", "#VALUE!", "#DIV/0!", "#NAME?", "#N/A", "#CYCLE!", "#TYPE!"
    };

    private static int ScanErrorLiteral(string s, int i)
    {
        var start = i;
        // Longest-match against the fixed vocabulary — needed because "#N/A" is a prefix-free
        // trap for naive scanning (stopping at the first '!'/'?' would also work here since none
        // of the symbols share a prefix long enough to matter, but matching the known set directly
        // keeps this in lock-step with FormulaError if that set ever changes).
        foreach (var symbol in KnownErrorSymbols)
        {
            if (i + symbol.Length <= s.Length && string.CompareOrdinal(s, i, symbol, 0, symbol.Length) == 0)
                return i + symbol.Length;
        }

        // Not a recognized error literal — consume through the terminator so the exception's span
        // covers the whole malformed token, not just the '#'.
        var j = i + 1;
        while (j < s.Length && s[j] != '!' && s[j] != '?' && !char.IsWhiteSpace(s[j])) j++;
        if (j < s.Length && (s[j] == '!' || s[j] == '?')) j++;

        throw new FormulaLexException($"Unknown error literal '{s[start..j]}'.", new TextSpan(start, j - start));
    }
}
