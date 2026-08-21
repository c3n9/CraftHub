using System.Collections.Generic;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Ast;

/// <summary>Base of every node the parser produces. Every node carries its source <see cref="Span"/>
/// so the formula bar can highlight it and F4 can rewrite exactly the right characters.</summary>
public abstract record FormulaAst(TextSpan Span);

// ---- Literals ----

public sealed record NumberLiteral(TextSpan Span, decimal Value) : FormulaAst(Span);
public sealed record TextLiteral(TextSpan Span, string Value) : FormulaAst(Span);
public sealed record BoolLiteral(TextSpan Span, bool Value) : FormulaAst(Span);
public sealed record ErrorLiteral(TextSpan Span, FormulaErrorCode Code) : FormulaAst(Span);

// ---- Operators ----

public enum UnaryOp { Negate, Plus }
public sealed record UnaryExpr(TextSpan Span, UnaryOp Op, FormulaAst Operand) : FormulaAst(Span);

/// <summary>Excel-style postfix percent: <c>50%</c> means <c>50 * 0.01</c>.</summary>
public sealed record PercentExpr(TextSpan Span, FormulaAst Operand) : FormulaAst(Span);

public enum BinaryOp { Add, Subtract, Multiply, Divide, Power, Concat, Eq, Ne, Lt, Gt, Le, Ge }
public sealed record BinaryExpr(TextSpan Span, BinaryOp Op, FormulaAst Left, FormulaAst Right) : FormulaAst(Span);

public sealed record CallExpr(TextSpan Span, string FunctionName, IReadOnlyList<FormulaAst> Arguments) : FormulaAst(Span);

// ---- References: raw syntax only. Resolving these against a table (A1 -> JsonPath) is
// CraftHub.Formulas.Addressing's job, not this project's — see that project's A1Translator.
// Keeping the split here is what lets the evaluator stay ignorant of A1 notation entirely.

/// <summary>A cell's row coordinate as written in the formula text — literal display row number,
/// never yet turned into a stored offset or absolute index (that translation needs to know which
/// row the formula itself lives in, which the parser doesn't).</summary>
/// <param name="DisplayRow">1-based row number exactly as typed, e.g. 5 for both <c>A5</c> and <c>A$5</c>.</param>
/// <param name="IsFixed">True for the <c>$</c>-prefixed form (<c>A$5</c>, <c>$A$5</c>) — doesn't shift on fill-down.</param>
public sealed record RowSyntax(int DisplayRow, bool IsFixed);

public sealed record CellRefSyntax(TextSpan Span, string Column, bool ColumnFixed, RowSyntax Row) : FormulaAst(Span);
public sealed record RangeRefSyntax(TextSpan Span, CellRefSyntax From, CellRefSyntax To) : FormulaAst(Span);
public sealed record ColumnBandSyntax(TextSpan Span, string FromColumn, bool FromFixed, string ToColumn, bool ToFixed) : FormulaAst(Span);
public sealed record RowBandSyntax(TextSpan Span, RowSyntax From, RowSyntax To) : FormulaAst(Span);

/// <summary>Whole-column reference <c>[price]</c>, stable across column reordering because it
/// addresses by key, not by letter.</summary>
public sealed record ColumnRefSyntax(TextSpan Span, string ColumnKey) : FormulaAst(Span);

/// <summary>Current-row value of a column, <c>@[price]</c> — the day-to-day way to write a
/// column formula.</summary>
public sealed record CurrentColumnRefSyntax(TextSpan Span, string ColumnKey) : FormulaAst(Span);

// ---- JSON path: the SAME syntax is used both for user-facing absolute paths ($.settings.tax)
// and for the row-based form the sidecar stores formulas in ($[r+1].price) — there is exactly one
// grammar, not two dialects; Addressing only chooses which spelling to print back into the UI.

/// <summary>An index segment's row-position meaning: a literal array index, an offset relative to
/// the formula's own row (only meaningful as the table's outer index — nothing deeper), or the
/// column-formula wildcard.</summary>
public abstract record PathIndexSyntax
{
    public sealed record Literal(int Value) : PathIndexSyntax;
    public sealed record RelativeRow(int Offset) : PathIndexSyntax; // [r] -> Offset 0, [r+1] -> 1, [r-1] -> -1
    public sealed record Wildcard : PathIndexSyntax;                // [*]
}

public abstract record PathSegmentSyntax
{
    public sealed record Key(string Name) : PathSegmentSyntax;      // .foo or ['foo']
    public sealed record Index(PathIndexSyntax Value) : PathSegmentSyntax; // [2] / [r+1] / [*]
}

public sealed record JsonPathSyntax(TextSpan Span, IReadOnlyList<PathSegmentSyntax> Segments) : FormulaAst(Span);
