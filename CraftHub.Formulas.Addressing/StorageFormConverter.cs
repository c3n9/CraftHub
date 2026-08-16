using System;
using System.Collections.Generic;
using System.Linq;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;

namespace CraftHub.Formulas.Addressing;

/// <summary>
/// Converts between the two spellings of the same reference: A1 (what the formula bar shows and
/// accepts) and the row-relative path form the sidecar stores (<c>$[r+1].price</c>). Only a plain,
/// non-<c>$</c>-row cell reference (<c>B5</c>) actually needs converting — everything else
/// (<c>$</c>-fixed cells, <c>[col]</c>, <c>@[col]</c>, an already-path-form reference) is already
/// position-independent or key-addressed and passes through untouched in both directions.
///
/// Deliberately out of scope: ranges and bands (<c>A1:A10</c>, <c>A:A</c>, <c>1:1</c>) are stored
/// verbatim as literal A1 text and always behave as absolute — a range with a non-<c>$</c> row
/// corner used inside a column formula will not shift per row. Column reordering is unaffected
/// (columns are addressed by key once stored), but there is intentionally no "fill right": unlike
/// Excel's anonymous, homogeneous columns, this table's columns are named, differently-typed
/// fields, so shifting a formula sideways across them has no well-defined meaning the way shifting
/// down a homogeneous column of rows does.
/// </summary>
public sealed class StorageFormConverter
{
    /// <summary>A1 text (as typed/committed in a specific cell) -> storage form (as written to the
    /// sidecar). <paramref name="authoringCell"/> is the cell the formula is being committed
    /// into — relative-row offsets are computed against it.</summary>
    public FormulaAst ToStorageForm(FormulaAst node, ITableShape shape, CellAddress authoringCell) => node switch
    {
        NumberLiteral or TextLiteral or BoolLiteral or ErrorLiteral => node,
        UnaryExpr u => u with { Operand = ToStorageForm(u.Operand, shape, authoringCell) },
        PercentExpr p => p with { Operand = ToStorageForm(p.Operand, shape, authoringCell) },
        BinaryExpr b => b with
        {
            Left = ToStorageForm(b.Left, shape, authoringCell),
            Right = ToStorageForm(b.Right, shape, authoringCell)
        },
        CallExpr c => c with { Arguments = c.Arguments.Select(a => ToStorageForm(a, shape, authoringCell)).ToList() },

        // Column position always converts to a key (there's no "fill right", so a column
        // reference is effectively always fixed — see the class doc comment); the row becomes a
        // relative offset from the authoring cell, or a literal index if it was '$'-marked.
        CellRefSyntax cell => CellToPath(cell, shape, authoringCell),

        _ => node
    };

    /// <summary>Storage form -> A1 text for showing in the formula bar while looking at
    /// <paramref name="viewingCell"/> — the inverse of <see cref="ToStorageForm"/>, so a relative
    /// path reads back as the same kind of bare A1 ref a user would have typed.</summary>
    public FormulaAst ToDisplayForm(FormulaAst node, ITableShape shape, CellAddress viewingCell) => node switch
    {
        NumberLiteral or TextLiteral or BoolLiteral or ErrorLiteral => node,
        UnaryExpr u => u with { Operand = ToDisplayForm(u.Operand, shape, viewingCell) },
        PercentExpr p => p with { Operand = ToDisplayForm(p.Operand, shape, viewingCell) },
        BinaryExpr b => b with
        {
            Left = ToDisplayForm(b.Left, shape, viewingCell),
            Right = ToDisplayForm(b.Right, shape, viewingCell)
        },
        CallExpr c => c with { Arguments = c.Arguments.Select(a => ToDisplayForm(a, shape, viewingCell)).ToList() },

        JsonPathSyntax path when TryPathToCell(path, shape, viewingCell, out var cell) => cell!,

        _ => node
    };

    private static FormulaAst CellToPath(CellRefSyntax cell, ITableShape shape, CellAddress authoringCell)
    {
        var colIndex = ColumnLetters.ToIndex(cell.Column);
        if (colIndex < 0 || colIndex >= shape.ColumnKeysInDisplayOrder.Count)
            return new ErrorLiteral(cell.Span, Values.FormulaErrorCode.Ref);

        var key = shape.ColumnKeysInDisplayOrder[colIndex];
        var targetRow = cell.Row.DisplayRow - 1;

        PathIndexSyntax rowIndex = cell.Row.IsFixed
            ? new PathIndexSyntax.Literal(targetRow)
            : new PathIndexSyntax.RelativeRow(targetRow - authoringCell.RowIndex);

        IReadOnlyList<PathSegmentSyntax> segments =
        [
            new PathSegmentSyntax.Index(rowIndex),
            new PathSegmentSyntax.Key(key)
        ];
        return new JsonPathSyntax(cell.Span, segments);
    }

    // Recognizes exactly the shape CellToRelativePath produces (index segment + one key segment,
    // nothing more) and turns it back into a CellRefSyntax. Anything else (a path with more
    // segments, a wildcard, an absolute path outside the table) isn't a "cell reference in
    // disguise" and is left as JSON-path text — see docs/ADDRESSING.md.
    private static bool TryPathToCell(JsonPathSyntax path, ITableShape shape, CellAddress viewingCell, out CellRefSyntax? cell)
    {
        cell = null;
        if (path.Segments.Count != 2) return false;
        if (path.Segments[0] is not PathSegmentSyntax.Index { Value: var indexValue }) return false;
        if (path.Segments[1] is not PathSegmentSyntax.Key key) return false;

        var colIndex = shape.ColumnKeysInDisplayOrder.ToList().IndexOf(key.Name);
        if (colIndex < 0) return false;
        var column = ColumnLetters.ToLetters(colIndex);

        switch (indexValue)
        {
            case PathIndexSyntax.RelativeRow rel:
                cell = new CellRefSyntax(path.Span, column, false, new RowSyntax(viewingCell.RowIndex + rel.Offset + 1, false));
                return true;
            case PathIndexSyntax.Literal lit:
                cell = new CellRefSyntax(path.Span, column, false, new RowSyntax(lit.Value + 1, true));
                return true;
            default:
                return false; // wildcard alone (no further segments) isn't a single-cell reference
        }
    }
}
