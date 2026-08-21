using System;
using System.Collections.Generic;
using System.Globalization;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Lexing;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Parsing;

/// <summary>
/// Recursive-descent parser matching the grammar in docs/GRAMMAR.md one production per method, so
/// each precedence level (comparison, concat, additive, multiplicative, power, unary, percent) is
/// trivially traceable back to the EBNF. References are parsed as raw syntax (<c>A1</c>,
/// <c>[price]</c>, <c>$.settings.tax</c>, and the sidecar's own <c>$[r+1].price</c> form all use
/// the same <c>reference</c> production) — turning those into resolved paths is
/// CraftHub.Formulas.Addressing's job, not this parser's.
/// </summary>
public sealed class FormulaParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;

    private FormulaParser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    /// <summary>Parses a full formula, including its mandatory leading <c>=</c>. Spans in the
    /// returned tree (and in any <see cref="FormulaParseException"/>) are relative to the text
    /// *after* the <c>=</c>, not to <paramref name="formulaText"/> itself.</summary>
    public static FormulaAst ParseFormula(string formulaText)
    {
        if (string.IsNullOrEmpty(formulaText) || formulaText[0] != '=')
            throw new FormulaParseException("A formula must start with '='.", new TextSpan(0, formulaText?.Length ?? 0));

        return ParseExpressionText(formulaText[1..]);
    }

    /// <summary>Parses a bare expression with no leading <c>=</c> — the grammar's <c>expression</c>
    /// production directly. Exists mainly so tests (and the formula bar's live-typing feedback)
    /// don't have to prepend a character that means nothing to the grammar itself.</summary>
    public static FormulaAst ParseExpressionText(string expressionText)
    {
        IReadOnlyList<Token> tokens;
        try
        {
            tokens = FormulaLexer.Tokenize(expressionText);
        }
        catch (FormulaLexException ex)
        {
            throw new FormulaParseException(ex.Message, ex.Span);
        }

        var parser = new FormulaParser(tokens);
        var expr = parser.ParseExpression();
        if (parser.Current.Kind != TokenKind.Eof)
            throw parser.Error(parser.Current.Span, $"Unexpected '{parser.Current.Text}' after the end of the expression.");

        return expr;
    }

    // ---- token stream helpers ----

    private Token Current => _tokens[_pos];
    private Token Peek(int ahead) => _tokens[Math.Min(_pos + ahead, _tokens.Count - 1)];

    private Token Advance() => _tokens[_pos < _tokens.Count - 1 ? _pos++ : _pos];

    private Token Expect(TokenKind kind)
    {
        if (Current.Kind != kind)
            throw Error(Current.Span, $"Expected {Describe(kind)}, got {Describe(Current)}.");
        return Advance();
    }

    private bool TryConsume(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        Advance();
        return true;
    }

    private FormulaParseException Error(TextSpan span, string message) => new(message, span);

    private static string Describe(TokenKind kind) => kind switch
    {
        TokenKind.RParen => "')'",
        TokenKind.LParen => "'('",
        TokenKind.RBracket => "']'",
        TokenKind.Comma => "','",
        TokenKind.Eof => "end of formula",
        _ => kind.ToString()
    };

    private static string Describe(Token t) => t.Kind == TokenKind.Eof ? "end of formula" : $"'{t.Text}'";

    private TextSpan SpanFrom(int start) => new(start, _tokens[Math.Max(0, _pos - 1)].Span.End - start);
    private static TextSpan SpanFrom(int start, int end) => new(start, end - start);

    // ---- expression, by precedence, loosest to tightest ----

    private FormulaAst ParseExpression() => ParseComparison();

    private FormulaAst ParseComparison()
    {
        var left = ParseConcat();
        while (TryMapComparisonOp(Current.Kind, out var op))
        {
            Advance();
            var right = ParseConcat();
            left = new BinaryExpr(SpanFrom(left.Span.Start, right.Span.End), op, left, right);
        }
        return left;
    }

    private static bool TryMapComparisonOp(TokenKind kind, out BinaryOp op)
    {
        (bool ok, op) = kind switch
        {
            TokenKind.Eq => (true, BinaryOp.Eq),
            TokenKind.Ne => (true, BinaryOp.Ne),
            TokenKind.Lt => (true, BinaryOp.Lt),
            TokenKind.Gt => (true, BinaryOp.Gt),
            TokenKind.Le => (true, BinaryOp.Le),
            TokenKind.Ge => (true, BinaryOp.Ge),
            _ => (false, default)
        };
        return ok;
    }

    private FormulaAst ParseConcat()
    {
        var left = ParseAdditive();
        while (Current.Kind == TokenKind.Ampersand)
        {
            Advance();
            var right = ParseAdditive();
            left = new BinaryExpr(SpanFrom(left.Span.Start, right.Span.End), BinaryOp.Concat, left, right);
        }
        return left;
    }

    private FormulaAst ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var op = Advance().Kind == TokenKind.Plus ? BinaryOp.Add : BinaryOp.Subtract;
            var right = ParseMultiplicative();
            left = new BinaryExpr(SpanFrom(left.Span.Start, right.Span.End), op, left, right);
        }
        return left;
    }

    private FormulaAst ParseMultiplicative()
    {
        var left = ParsePower();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var op = Advance().Kind == TokenKind.Star ? BinaryOp.Multiply : BinaryOp.Divide;
            var right = ParsePower();
            left = new BinaryExpr(SpanFrom(left.Span.Start, right.Span.End), op, left, right);
        }
        return left;
    }

    // Left-associative: 2^3^2 parses as (2^3)^2 = 64, matching Excel rather than mathematical
    // convention (which would make it 512). Each operand is a full `unary` per the grammar, so the
    // sign binds tighter than '^' — that's what makes -2^2 equal 4, not -4.
    private FormulaAst ParsePower()
    {
        var left = ParseUnary();
        while (Current.Kind == TokenKind.Caret)
        {
            Advance();
            var right = ParseUnary();
            left = new BinaryExpr(SpanFrom(left.Span.Start, right.Span.End), BinaryOp.Power, left, right);
        }
        return left;
    }

    private FormulaAst ParseUnary()
    {
        if (Current.Kind == TokenKind.Minus)
        {
            var t = Advance();
            var operand = ParseUnary();
            return new UnaryExpr(SpanFrom(t.Span.Start, operand.Span.End), UnaryOp.Negate, operand);
        }
        if (Current.Kind == TokenKind.Plus)
        {
            var t = Advance();
            var operand = ParseUnary();
            return new UnaryExpr(SpanFrom(t.Span.Start, operand.Span.End), UnaryOp.Plus, operand);
        }
        return ParsePercent();
    }

    private FormulaAst ParsePercent()
    {
        var operand = ParsePrimary();
        while (Current.Kind == TokenKind.Percent)
        {
            var t = Advance();
            operand = new PercentExpr(SpanFrom(operand.Span.Start, t.Span.End), operand);
        }
        return operand;
    }

    // ---- primary: literals, calls, parens, references ----

    private FormulaAst ParsePrimary()
    {
        var tok = Current;

        switch (tok.Kind)
        {
            case TokenKind.LParen:
            {
                Advance();
                var inner = ParseExpression();
                Expect(TokenKind.RParen);
                return inner;
            }

            case TokenKind.Number:
                // "1:1" (a row band) starts with what looks like a numeric literal — the colon
                // right after it, with nothing else that ever legally follows a number, is the
                // unambiguous signal to hand off to the row-band parser instead.
                if (Peek(1).Kind == TokenKind.Colon)
                    return ParseRowBandTail(tok.Span.Start, fromFixed: false);
                Advance();
                return new NumberLiteral(tok.Span, tok.NumberValue);

            case TokenKind.String:
                Advance();
                return new TextLiteral(tok.Span, tok.StringValue!);

            case TokenKind.ErrorLiteral:
            {
                Advance();
                var code = FormulaError.CodeForSymbol(tok.Text)
                    ?? throw Error(tok.Span, $"Unknown error literal '{tok.Text}'.");
                return new ErrorLiteral(tok.Span, code);
            }

            case TokenKind.At:
            {
                Advance();
                Expect(TokenKind.LBracket);
                var key = ParseBracketKeyName();
                var close = Expect(TokenKind.RBracket);
                return new CurrentColumnRefSyntax(SpanFrom(tok.Span.Start, close.Span.End), key);
            }

            case TokenKind.LBracket:
            {
                Advance();
                var key = ParseBracketKeyName();
                var close = Expect(TokenKind.RBracket);
                return new ColumnRefSyntax(SpanFrom(tok.Span.Start, close.Span.End), key);
            }

            case TokenKind.Dollar:
                return ParseDollarPrefixed(Advance().Span.Start);

            case TokenKind.Word:
                return ParseWordPrimary();

            default:
                throw Error(tok.Span, $"Unexpected {Describe(tok)}.");
        }
    }

    private FormulaAst ParseWordPrimary()
    {
        var word = Advance(); // TokenKind.Word

        if (Current.Kind == TokenKind.LParen)
            return ParseCall(word);

        if (string.Equals(word.Text, "TRUE", StringComparison.OrdinalIgnoreCase))
            return new BoolLiteral(word.Span, true);
        if (string.Equals(word.Text, "FALSE", StringComparison.OrdinalIgnoreCase))
            return new BoolLiteral(word.Span, false);

        // "A1" — the lexer's identifier rule matches letters *and* digits, so a bare (no '$')
        // column+row comes through as ONE Word token ("A1", not "A" then "1"); splitting it back
        // apart is this parser's job, not the lexer's.
        if (TryMatchColumnAndRow(word.Text, out var bareColumn, out var bareRow))
        {
            var cell = new CellRefSyntax(word.Span, bareColumn, false, new RowSyntax(bareRow, false));
            return ParseCellTail(word.Span.Start, cell);
        }

        if (IsAllLetters(word.Text))
        {
            // "A$1" — the '$' isn't part of the word charset, so it stops the lexer's scan; the
            // row (still fixed-marked) is genuinely a separate token here.
            if (TryParseFixedRowPart(word, out var row))
            {
                var cell = new CellRefSyntax(SpanFrom(word.Span.Start), word.Text.ToUpperInvariant(), false, row);
                return ParseCellTail(word.Span.Start, cell);
            }

            if (Current.Kind == TokenKind.Colon)
                return ParseColumnBandTail(word.Span.Start, word.Text.ToUpperInvariant(), fromFixed: false);
        }

        throw Error(word.Span, $"'{word.Text}' is not a known function, and not a valid reference.");
    }

    // Entry point right after consuming a leading '$': either a JSON path root, an absolute-column
    // cell/range/column-band, or an absolute row-band.
    private FormulaAst ParseDollarPrefixed(int dollarStart)
    {
        if (Current.Kind is TokenKind.Dot or TokenKind.LBracket)
            return ParseJsonPath(dollarStart);

        if (Current.Kind == TokenKind.Number)
            return ParseRowBandTail(dollarStart, fromFixed: true);

        if (Current.Kind == TokenKind.Word)
        {
            var colWord = Advance();

            // "$A1" — column fixed, row relative; row digits are glued onto the word (same reason
            // as the bare case above).
            if (TryMatchColumnAndRow(colWord.Text, out var col, out var rowNum))
            {
                var cell = new CellRefSyntax(SpanFrom(dollarStart), col, true, new RowSyntax(rowNum, false));
                return ParseCellTail(dollarStart, cell);
            }

            if (IsAllLetters(colWord.Text))
            {
                // "$A$1" — both fixed; the row's own '$' is still a separate token ahead.
                if (TryParseFixedRowPart(colWord, out var row))
                {
                    var cell = new CellRefSyntax(SpanFrom(dollarStart), colWord.Text.ToUpperInvariant(), true, row);
                    return ParseCellTail(dollarStart, cell);
                }

                // "$A:$C" — column band, first column fixed.
                if (Current.Kind == TokenKind.Colon)
                    return ParseColumnBandTail(dollarStart, colWord.Text.ToUpperInvariant(), fromFixed: true);
            }

            throw Error(colWord.Span, $"Expected a row number or ':' after '${colWord.Text}'.");
        }

        throw Error(Current.Span, "Expected a JSON path ('.' or '['), a column letter, or a row number after '$'.");
    }

    // If a ':' follows, extends a cell ref into an a1-range; otherwise returns the cell as-is.
    private FormulaAst ParseCellTail(int refStart, CellRefSyntax from)
    {
        if (Current.Kind != TokenKind.Colon) return from;

        Advance(); // ':'
        var toFixed = TryConsume(TokenKind.Dollar);
        var to = ParseCellAfterColumnFixedness(toFixed);
        return new RangeRefSyntax(SpanFrom(refStart), from, to);
    }

    // The "to" side of a range is always a plain cell — never itself a band — so this doesn't need
    // the column-band fallback ParseWordPrimary/ParseDollarPrefixed have.
    private CellRefSyntax ParseCellAfterColumnFixedness(bool columnFixed)
    {
        if (Current.Kind != TokenKind.Word)
            throw Error(Current.Span, "Expected a cell reference.");

        var word = Advance();

        if (TryMatchColumnAndRow(word.Text, out var col, out var rowNum))
            return new CellRefSyntax(word.Span, col, columnFixed, new RowSyntax(rowNum, false));

        if (IsAllLetters(word.Text) && TryParseFixedRowPart(word, out var row))
            return new CellRefSyntax(SpanFrom(word.Span.Start), word.Text.ToUpperInvariant(), columnFixed, row);

        throw Error(word.Span, $"Expected a row number after '{word.Text}' to complete the reference.");
    }

    private FormulaAst ParseColumnBandTail(int refStart, string fromColumn, bool fromFixed)
    {
        Advance(); // ':'
        var toFixed = TryConsume(TokenKind.Dollar);
        if (!(Current.Kind == TokenKind.Word && IsAllLetters(Current.Text)))
            throw Error(Current.Span, "Expected a column letter after ':'.");
        var toWord = Advance();
        return new ColumnBandSyntax(SpanFrom(refStart), fromColumn, fromFixed, toWord.Text.ToUpperInvariant(), toFixed);
    }

    private FormulaAst ParseRowBandTail(int refStart, bool fromFixed)
    {
        var fromNum = Expect(TokenKind.Number);
        Expect(TokenKind.Colon);
        var toFixed = TryConsume(TokenKind.Dollar);
        var toNum = Expect(TokenKind.Number);
        var from = new RowSyntax((int)fromNum.NumberValue, fromFixed);
        var to = new RowSyntax((int)toNum.NumberValue, toFixed);
        return new RowBandSyntax(SpanFrom(refStart), from, to);
    }

    // Splits a Word token's own text into a leading run of letters and a trailing run of digits
    // that together consume the WHOLE token — e.g. "A1" -> ("A","1"), "AA10" -> ("AA","10"). Used
    // for the no-'$' case, where the lexer has already glued column and row into one token because
    // to it "A1" is indistinguishable from any other identifier. Returns false for a pure-letter
    // word (a candidate function name / TRUE-FALSE / column-band start instead) or anything that
    // doesn't cleanly split (there's trailing junk after the digits).
    private static bool TryMatchColumnAndRow(string text, out string letters, out int row)
    {
        var i = 0;
        while (i < text.Length && char.IsAsciiLetter(text[i])) i++;

        if (i == 0 || i == text.Length)
        {
            letters = "";
            row = 0;
            return false;
        }

        var j = i;
        while (j < text.Length && char.IsAsciiDigit(text[j])) j++;

        if (j != text.Length)
        {
            letters = "";
            row = 0;
            return false;
        }

        letters = text[..i].ToUpperInvariant();
        row = int.Parse(text[i..j], CultureInfo.InvariantCulture);
        return true;
    }

    // Looks for a '$' + number directly adjacent to `columnWord`, with no gap between either —
    // that adjacency is what tells "A$1" apart from the column letters meaning something else
    // entirely (a function name, TRUE/FALSE, a column-band start). Only reachable when
    // `columnWord`'s own text is letters-only, since '$' always stops the lexer's word scan.
    private bool TryParseFixedRowPart(Token columnWord, out RowSyntax row)
    {
        if (Current.Kind == TokenKind.Dollar && Current.IsAdjacentTo(columnWord)
            && Peek(1).Kind == TokenKind.Number && Peek(1).IsAdjacentTo(Current))
        {
            Advance(); // '$'
            var num = Advance(); // number
            row = new RowSyntax((int)num.NumberValue, true);
            return true;
        }

        row = default!;
        return false;
    }

    private FormulaAst ParseJsonPath(int dollarStart)
    {
        var segments = new List<PathSegmentSyntax>();

        while (Current.Kind is TokenKind.Dot or TokenKind.LBracket)
        {
            if (Current.Kind == TokenKind.Dot)
            {
                Advance();
                var name = Expect(TokenKind.Word);
                segments.Add(new PathSegmentSyntax.Key(name.Text));
            }
            else
            {
                Advance(); // '['
                segments.Add(ParseIndexSegment());
                Expect(TokenKind.RBracket);
            }
        }

        if (segments.Count == 0)
            throw Error(Current.Span, "Expected '.' or '[' to start a JSON path segment.");

        return new JsonPathSyntax(SpanFrom(dollarStart), segments);
    }

    private PathSegmentSyntax ParseIndexSegment()
    {
        if (Current.Kind == TokenKind.String)
            return new PathSegmentSyntax.Key(Advance().StringValue!);

        if (Current.Kind == TokenKind.Star)
        {
            Advance();
            return new PathSegmentSyntax.Index(new PathIndexSyntax.Wildcard());
        }

        if (Current.Kind == TokenKind.Word && string.Equals(Current.Text, "r", StringComparison.OrdinalIgnoreCase))
        {
            Advance();
            var offset = 0;
            if (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var sign = Advance().Kind == TokenKind.Plus ? 1 : -1;
                offset = sign * (int)Expect(TokenKind.Number).NumberValue;
            }
            return new PathSegmentSyntax.Index(new PathIndexSyntax.RelativeRow(offset));
        }

        if (Current.Kind == TokenKind.Number)
            return new PathSegmentSyntax.Index(new PathIndexSyntax.Literal((int)Advance().NumberValue));

        throw Error(Current.Span, "Expected an index, a quoted key, 'r' (this row), or '*' inside '[...]'.");
    }

    private string ParseBracketKeyName()
    {
        if (Current.Kind == TokenKind.String) return Advance().StringValue!;
        if (Current.Kind == TokenKind.Word) return Advance().Text;
        if (Current.Kind == TokenKind.Number) return Advance().Text;
        throw Error(Current.Span, "Expected a column name inside '[...]'.");
    }

    private FormulaAst ParseCall(Token nameTok)
    {
        Expect(TokenKind.LParen);
        var args = new List<FormulaAst>();

        if (Current.Kind != TokenKind.RParen)
        {
            args.Add(ParseExpression());
            while (Current.Kind == TokenKind.Comma)
            {
                Advance();
                if (Current.Kind == TokenKind.RParen)
                    throw Error(Current.Span, "Unexpected trailing comma before ')'.");
                args.Add(ParseExpression());
            }
        }

        var close = Expect(TokenKind.RParen);
        return new CallExpr(SpanFrom(nameTok.Span.Start, close.Span.End), nameTok.Text.ToUpperInvariant(), args);
    }

    private static bool IsAllLetters(string s)
    {
        foreach (var c in s)
            if (!char.IsAsciiLetter(c))
                return false;
        return s.Length > 0;
    }
}
