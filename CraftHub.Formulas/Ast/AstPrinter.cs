using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CraftHub.Formulas.Ast;

/// <summary>
/// Renders a <see cref="FormulaAst"/> back to formula text. <see cref="Print"/> is the "natural"
/// form — minimal parens, matching what a user would type — used for F4 reference cycling and for
/// rewriting formulas after a column rename, both of which replace one small span of the original
/// text rather than regenerating the whole formula. <see cref="ToCanonical"/> parenthesizes every
/// binary operation explicitly; it exists so tests can assert operator precedence by comparing
/// strings instead of hand-building expected trees.
/// </summary>
public static class AstPrinter
{
    public static string Print(FormulaAst node) => PrintNode(node, canonical: false);

    public static string ToCanonical(FormulaAst node) => PrintNode(node, canonical: true);

    private static string PrintNode(FormulaAst node, bool canonical) => node switch
    {
        NumberLiteral n => n.Value.ToString(CultureInfo.InvariantCulture),
        TextLiteral t => "\"" + t.Value.Replace("\"", "\"\"") + "\"",
        BoolLiteral b => b.Value ? "TRUE" : "FALSE",
        ErrorLiteral e => SymbolFor(e.Code),

        UnaryExpr u => (u.Op == UnaryOp.Negate ? "-" : "+") + PrintNode(u.Operand, canonical),
        PercentExpr p => PrintNode(p.Operand, canonical) + "%",

        BinaryExpr b => canonical
            ? $"({PrintNode(b.Left, true)}{OpText(b.Op)}{PrintNode(b.Right, true)})"
            : $"{PrintNode(b.Left, false)}{OpText(b.Op)}{PrintNode(b.Right, false)}",

        CallExpr c => $"{c.FunctionName}({string.Join(",", c.Arguments.Select(a => PrintNode(a, canonical)))})",

        CellRefSyntax cell => PrintCell(cell),
        RangeRefSyntax range => $"{PrintCell(range.From)}:{PrintCell(range.To)}",
        ColumnBandSyntax band => $"{(band.FromFixed ? "$" : "")}{band.FromColumn}:{(band.ToFixed ? "$" : "")}{band.ToColumn}",
        RowBandSyntax rowBand => $"{PrintRow(rowBand.From)}:{PrintRow(rowBand.To)}",
        ColumnRefSyntax col => $"[{BracketKey(col.ColumnKey)}]",
        CurrentColumnRefSyntax cur => $"@[{BracketKey(cur.ColumnKey)}]",

        JsonPathSyntax path => PrintPath(path),

        _ => throw new NotSupportedException($"Unknown AST node type: {node.GetType().Name}")
    };

    private static string PrintCell(CellRefSyntax cell) =>
        $"{(cell.ColumnFixed ? "$" : "")}{cell.Column}{PrintRow(cell.Row)}";

    private static string PrintRow(RowSyntax row) =>
        (row.IsFixed ? "$" : "") + row.DisplayRow.ToString(CultureInfo.InvariantCulture);

    private static string PrintPath(JsonPathSyntax path)
    {
        var sb = new StringBuilder("$");
        foreach (var seg in path.Segments)
        {
            switch (seg)
            {
                case PathSegmentSyntax.Key k when IsBareIdentifier(k.Name):
                    sb.Append('.').Append(k.Name);
                    break;
                case PathSegmentSyntax.Key k:
                    sb.Append("[\"").Append(k.Name.Replace("\"", "\"\"")).Append("\"]");
                    break;
                case PathSegmentSyntax.Index { Value: PathIndexSyntax.Literal lit }:
                    sb.Append('[').Append(lit.Value).Append(']');
                    break;
                case PathSegmentSyntax.Index { Value: PathIndexSyntax.RelativeRow rel }:
                    sb.Append('[').Append('r');
                    if (rel.Offset > 0) sb.Append('+').Append(rel.Offset);
                    else if (rel.Offset < 0) sb.Append(rel.Offset); // '-' comes from the number itself
                    sb.Append(']');
                    break;
                case PathSegmentSyntax.Index { Value: PathIndexSyntax.Wildcard }:
                    sb.Append("[*]");
                    break;
            }
        }
        return sb.ToString();
    }

    // A [name]/@[name] key prints bare only if the parser would read it back as a single Word or
    // Number token — anything else (a dot from an expanded nested field's display path, a space, a
    // quote) has to be quoted, matching PrintPath's quoted-key form.
    private static string BracketKey(string key)
    {
        foreach (var c in key)
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
                return $"\"{key.Replace("\"", "\"\"")}\"";
        return key.Length == 0 ? "\"\"" : key;
    }

    private static bool IsBareIdentifier(string s)
    {
        if (s.Length == 0 || !(char.IsAsciiLetter(s[0]) || s[0] == '_')) return false;
        foreach (var c in s)
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
                return false;
        return true;
    }

    private static string SymbolFor(Values.FormulaErrorCode code) => code switch
    {
        Values.FormulaErrorCode.Ref => "#REF!",
        Values.FormulaErrorCode.Value => "#VALUE!",
        Values.FormulaErrorCode.DivZero => "#DIV/0!",
        Values.FormulaErrorCode.Name => "#NAME?",
        Values.FormulaErrorCode.NA => "#N/A",
        Values.FormulaErrorCode.Cycle => "#CYCLE!",
        Values.FormulaErrorCode.Type => "#TYPE!",
        _ => "#ERROR!"
    };

    private static string OpText(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+",
        BinaryOp.Subtract => "-",
        BinaryOp.Multiply => "*",
        BinaryOp.Divide => "/",
        BinaryOp.Power => "^",
        BinaryOp.Concat => "&",
        BinaryOp.Eq => "=",
        BinaryOp.Ne => "<>",
        BinaryOp.Lt => "<",
        BinaryOp.Gt => ">",
        BinaryOp.Le => "<=",
        BinaryOp.Ge => ">=",
        _ => "?"
    };
}
