using System.Collections.Generic;
using System.Linq;

namespace CraftHub.Formulas.Addressing;

/// <summary>
/// Keeps a <see cref="JsonPath"/> correct across the table mutations that change row indices or
/// column keys — used to re-key the sidecar's <c>cellFormulas</c>/<c>state</c> dictionaries (and,
/// in Step 5, to rewrite the paths a formula's own text embeds) after insert, delete, or rename.
/// Sorting and filtering need no equivalent here: neither one reorders or removes anything in the
/// underlying model (see WorkspaceViewModel), so no path a formula could hold is ever invalidated
/// by them.
/// </summary>
public static class StructureRewriter
{
    /// <summary>A row was inserted at <paramref name="insertIndex"/> (0-based, the new row's own
    /// index). Every path whose row is at or after that index shifts down by one; a path outside
    /// the table's row array (e.g. <c>$.settings.tax</c>) is untouched.</summary>
    public static JsonPath OnRowInserted(JsonPath path, int insertIndex) =>
        RewriteRootIndex(path, idx => idx >= insertIndex ? idx + 1 : idx);

    /// <summary>A row was removed at <paramref name="removedIndex"/>. A path pointing exactly at
    /// that row has nowhere to go — <c>null</c> means "this entry no longer applies" (the caller
    /// drops it, and the removed cell's own formula, if any, is gone with the row). Everything
    /// below shifts up by one.</summary>
    public static JsonPath? OnRowRemoved(JsonPath path, int removedIndex)
    {
        if (path.Segments.Count == 0 || path.Segments[0] is not JsonPathSegment.Index idx)
            return path;

        if (idx.Value == removedIndex) return null;
        var newIndex = idx.Value > removedIndex ? idx.Value - 1 : idx.Value;
        return ReplaceRootIndex(path, newIndex);
    }

    /// <summary>A column's key changed. Rewrites the run of segments that names it, starting at the
    /// second segment (<c>$[i].&lt;key…&gt;</c>) — one segment for a flat column, several for an
    /// expanded nested field whose key is a path. A same-named key appearing deeper (inside a
    /// nested Object/Array cell value, i.e. after the column's own segments) is a different thing
    /// and is correctly left alone.</summary>
    public static JsonPath OnColumnRenamed(JsonPath path, IReadOnlyList<string> oldKeySegments, IReadOnlyList<string> newKeySegments)
    {
        if (!MatchesColumn(path, oldKeySegments)) return path;

        var segments = path.Segments.ToList();
        segments.RemoveRange(1, oldKeySegments.Count);
        segments.InsertRange(1, newKeySegments.Select(JsonPathSegment (s) => new JsonPathSegment.Key(s)));
        return new JsonPath(segments);
    }

    /// <summary>A column was removed. <c>null</c> means "this entry addressed the removed column
    /// and no longer applies" (mirrors <see cref="OnRowRemoved"/>'s drop signal).</summary>
    public static JsonPath? OnColumnRemoved(JsonPath path, IReadOnlyList<string> removedKeySegments) =>
        ReferencesColumn(path, removedKeySegments) ? null : path;

    /// <summary>True when <paramref name="path"/>'s column segments are exactly
    /// <paramref name="keySegments"/> — used to find every reference (in
    /// cellFormulas/columnFormulas/state, or in another cell's formula text) that a column removal
    /// invalidates.</summary>
    public static bool ReferencesColumn(JsonPath path, IReadOnlyList<string> keySegments) =>
        MatchesColumn(path, keySegments);

    private static bool MatchesColumn(JsonPath path, IReadOnlyList<string> keySegments)
    {
        if (keySegments.Count == 0 || path.Segments.Count < 1 + keySegments.Count) return false;
        for (var i = 0; i < keySegments.Count; i++)
            if (path.Segments[1 + i] is not JsonPathSegment.Key k || k.Name != keySegments[i])
                return false;
        return true;
    }

    /// <summary>True when <paramref name="path"/>'s row segment is exactly <paramref name="rowIndex"/>
    /// — used to find every reference a row deletion invalidates.</summary>
    public static bool ReferencesRow(JsonPath path, int rowIndex) =>
        path.Segments.Count > 0 && path.Segments[0] is JsonPathSegment.Index idx && idx.Value == rowIndex;

    private static JsonPath RewriteRootIndex(JsonPath path, System.Func<int, int> shift)
    {
        if (path.Segments.Count == 0 || path.Segments[0] is not JsonPathSegment.Index idx)
            return path;

        return ReplaceRootIndex(path, shift(idx.Value));
    }

    private static JsonPath ReplaceRootIndex(JsonPath path, int newIndex)
    {
        var segments = path.Segments.ToList();
        segments[0] = new JsonPathSegment.Index(newIndex);
        return new JsonPath(segments);
    }
}
