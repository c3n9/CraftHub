using System;
using System.Collections.Generic;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Addressing;

/// <summary>
/// Turns a reference AST node (A1 form, <c>[col]</c>/<c>@[col]</c>, or the JSON-path form the
/// sidecar itself uses) into the path(s) it actually points at, given the table's current shape.
///
/// One thing worth being explicit about: <c>A5</c> and <c>A$5</c> resolve to the exact same path
/// right now — "row 5" always means row 5, whichever cell is asking. The <c>$</c> only matters
/// when a formula gets copied to a different cell (fill-down, paste), which is when the reference
/// text itself gets rewritten (unfixed parts shift, fixed parts don't) — that rewriting is
/// <see cref="RefShifter"/>'s job, not this class's. This class only ever answers "what does this
/// reference mean right here, right now".
/// </summary>
public sealed class A1Translator
{
    public ReferenceResolution Resolve(FormulaAst reference, ITableShape shape, CellAddress current) => reference switch
    {
        CellRefSyntax cell => ResolveCell(cell, shape),
        RangeRefSyntax range => ResolveRange(range, shape),
        ColumnBandSyntax band => ResolveColumnBand(band, shape),
        RowBandSyntax rowBand => ResolveRowBand(rowBand, shape),
        ColumnRefSyntax col => ResolveColumnRef(col, shape),
        CurrentColumnRefSyntax cur => ResolveCurrentColumn(cur, shape, current),
        JsonPathSyntax path => ResolveJsonPath(path, shape, current),
        _ => throw new ArgumentException($"'{reference.GetType().Name}' is not a reference node.", nameof(reference))
    };

    // ---- A1 forms ----

    private static ReferenceResolution ResolveCell(CellRefSyntax cell, ITableShape shape)
    {
        var colIndex = ColumnLetters.ToIndex(cell.Column);
        if (colIndex < 0 || colIndex >= shape.ColumnKeysInDisplayOrder.Count)
            return Fail($"Column '{cell.Column}' does not exist.");

        var rowIndex = cell.Row.DisplayRow - 1;
        if (rowIndex < 0 || rowIndex >= shape.RowCount)
            return Fail($"Row {cell.Row.DisplayRow} does not exist.");

        return PathOrFail(shape, rowIndex, shape.ColumnKeysInDisplayOrder[colIndex]);
    }

    private static ReferenceResolution ResolveRange(RangeRefSyntax range, ITableShape shape)
    {
        var fromCol = ColumnLetters.ToIndex(range.From.Column);
        var toCol = ColumnLetters.ToIndex(range.To.Column);
        var fromRow = range.From.Row.DisplayRow - 1;
        var toRow = range.To.Row.DisplayRow - 1;

        var minCol = Math.Min(fromCol, toCol);
        var maxCol = Math.Max(fromCol, toCol);
        var minRow = Math.Min(fromRow, toRow);
        var maxRow = Math.Max(fromRow, toRow);

        if (minCol < 0 || maxCol >= shape.ColumnKeysInDisplayOrder.Count)
            return Fail("Range extends past the last column.");
        if (minRow < 0 || maxRow >= shape.RowCount)
            return Fail("Range extends past the last row.");

        var paths = new List<JsonPath>();
        for (var r = minRow; r <= maxRow; r++)
        for (var c = minCol; c <= maxCol; c++)
        {
            var resolved = PathOrFail(shape, r, shape.ColumnKeysInDisplayOrder[c]);
            if (resolved is ReferenceResolution.Failed f) return f;
            paths.Add(((ReferenceResolution.Single)resolved).Path);
        }

        return new ReferenceResolution.Multiple(paths);
    }

    private static ReferenceResolution ResolveColumnBand(ColumnBandSyntax band, ITableShape shape)
    {
        var fromCol = ColumnLetters.ToIndex(band.FromColumn);
        var toCol = ColumnLetters.ToIndex(band.ToColumn);
        var minCol = Math.Min(fromCol, toCol);
        var maxCol = Math.Max(fromCol, toCol);

        if (minCol < 0 || maxCol >= shape.ColumnKeysInDisplayOrder.Count)
            return Fail("Column band extends past the last column.");

        var paths = new List<JsonPath>();
        for (var c = minCol; c <= maxCol; c++)
        for (var r = 0; r < shape.RowCount; r++)
        {
            var resolved = PathOrFail(shape, r, shape.ColumnKeysInDisplayOrder[c]);
            if (resolved is ReferenceResolution.Failed f) return f;
            paths.Add(((ReferenceResolution.Single)resolved).Path);
        }

        return new ReferenceResolution.Multiple(paths);
    }

    private static ReferenceResolution ResolveRowBand(RowBandSyntax rowBand, ITableShape shape)
    {
        var fromRow = rowBand.From.DisplayRow - 1;
        var toRow = rowBand.To.DisplayRow - 1;
        var minRow = Math.Min(fromRow, toRow);
        var maxRow = Math.Max(fromRow, toRow);

        if (minRow < 0 || maxRow >= shape.RowCount)
            return Fail("Row band extends past the last row.");

        var paths = new List<JsonPath>();
        for (var r = minRow; r <= maxRow; r++)
        for (var c = 0; c < shape.ColumnKeysInDisplayOrder.Count; c++)
        {
            var resolved = PathOrFail(shape, r, shape.ColumnKeysInDisplayOrder[c]);
            if (resolved is ReferenceResolution.Failed f) return f;
            paths.Add(((ReferenceResolution.Single)resolved).Path);
        }

        return new ReferenceResolution.Multiple(paths);
    }

    private static ReferenceResolution ResolveColumnRef(ColumnRefSyntax colRef, ITableShape shape)
    {
        // A reference may be typed with the dotted display path of an expanded nested field
        // (@["a.b"]); resolve it to the real column key before addressing.
        var key = shape.ResolveColumnKey(colRef.ColumnKey);
        if (key is null)
            return Fail($"Column '{colRef.ColumnKey}' does not exist.");

        var paths = new List<JsonPath>();
        for (var r = 0; r < shape.RowCount; r++)
        {
            var resolved = PathOrFail(shape, r, key);
            if (resolved is ReferenceResolution.Failed f) return f;
            paths.Add(((ReferenceResolution.Single)resolved).Path);
        }

        return new ReferenceResolution.Multiple(paths);
    }

    private static ReferenceResolution ResolveCurrentColumn(CurrentColumnRefSyntax cur, ITableShape shape, CellAddress current)
    {
        if (current.RowIndex < 0 || current.RowIndex >= shape.RowCount)
            return Fail("The formula's own row no longer exists.");

        var key = shape.ResolveColumnKey(cur.ColumnKey);
        if (key is null)
            return Fail($"Column '{cur.ColumnKey}' does not exist.");

        return PathOrFail(shape, current.RowIndex, key, $"Column '{cur.ColumnKey}' does not exist.");
    }

    // ---- JSON path form (also what the sidecar stores formulas in) ----

    private static ReferenceResolution ResolveJsonPath(JsonPathSyntax syntax, ITableShape shape, CellAddress current)
    {
        var segments = syntax.Segments;

        // Only segment[0] can address the table's own root array — a relative offset or wildcard
        // any deeper than that has no defined meaning (fill-down only ever shifts the outer row).
        if (segments.Count > 0 && segments[0] is PathSegmentSyntax.Index { Value: var index0 })
        {
            switch (index0)
            {
                case PathIndexSyntax.Wildcard:
                {
                    var paths = new List<JsonPath>();
                    for (var r = 0; r < shape.RowCount; r++)
                    {
                        var full = AppendRemaining(JsonPath.RootRow(r), segments, 1);
                        if (full is null) return Fail("Path uses relative/wildcard indexing below the table row.");
                        paths.Add(full);
                    }
                    return new ReferenceResolution.Multiple(paths);
                }

                case PathIndexSyntax.RelativeRow rel:
                {
                    var row = current.RowIndex + rel.Offset;
                    if (row < 0 || row >= shape.RowCount) return Fail($"Row {row + 1} does not exist.");
                    var full = AppendRemaining(JsonPath.RootRow(row), segments, 1);
                    return full is null
                        ? Fail("Path uses relative/wildcard indexing below the table row.")
                        : new ReferenceResolution.Single(full);
                }

                case PathIndexSyntax.Literal lit:
                {
                    if (lit.Value < 0 || lit.Value >= shape.RowCount) return Fail($"Row {lit.Value + 1} does not exist.");
                    var full = AppendRemaining(JsonPath.RootRow(lit.Value), segments, 1);
                    return full is null
                        ? Fail("Path uses relative/wildcard indexing below the table row.")
                        : new ReferenceResolution.Single(full);
                }
            }
        }

        // No leading row-index segment — an absolute path outside the table entirely (e.g.
        // "$.settings.tax"), passed through as-is with no shape lookup needed.
        var literal = AppendRemaining(new JsonPath(Array.Empty<JsonPathSegment>()), segments, 0);
        return literal is null
            ? Fail("Relative row indexing ('r', '*') is only valid as the first path segment.")
            : new ReferenceResolution.Single(literal);
    }

    private static JsonPath? AppendRemaining(JsonPath basePath, IReadOnlyList<PathSegmentSyntax> segments, int fromIndex)
    {
        var result = basePath;
        for (var i = fromIndex; i < segments.Count; i++)
        {
            switch (segments[i])
            {
                case PathSegmentSyntax.Key k:
                    result = result.Append(new JsonPathSegment.Key(k.Name));
                    break;
                case PathSegmentSyntax.Index { Value: PathIndexSyntax.Literal lit }:
                    result = result.Append(new JsonPathSegment.Index(lit.Value));
                    break;
                default:
                    return null; // relative/wildcard indexing only supported as segment[0]
            }
        }
        return result;
    }

    // ---- helpers ----

    private static ReferenceResolution PathOrFail(ITableShape shape, int rowIndex, string columnKey, string? notFoundMessage = null)
    {
        var path = shape.PathForCell(rowIndex, columnKey);
        return path is null
            ? Fail(notFoundMessage ?? $"Column '{columnKey}' does not exist.")
            : new ReferenceResolution.Single(path);
    }

    private static ReferenceResolution.Failed Fail(string message) =>
        new(new FormulaError(FormulaErrorCode.Ref, message));
}
