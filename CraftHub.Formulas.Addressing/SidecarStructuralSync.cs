using System;
using System.Collections.Generic;
using System.Linq;
using CraftHub.Formulas.Sidecar;

namespace CraftHub.Formulas.Addressing;

/// <summary>
/// Applies one structural mutation (row insert/delete, column rename/remove) across an entire
/// sidecar's <c>cellFormulas</c>/<c>state</c> dictionaries at once, using <see cref="StructureRewriter"/>
/// per entry. Bridges Addressing's path-level rewriting rules with the Formulas-level
/// <see cref="FormulaSidecar"/> model — everything here is pure dictionary manipulation, no file
/// I/O, so the caller (Step 6's session service) decides when to persist the result.
///
/// <see cref="FormulaSidecar.ColumnFormulas"/> is keyed by bare column name, not by path, so row
/// mutations never touch it — a <c>[*]</c> template means "every row," which insert/delete doesn't
/// change the meaning of. Column rename/remove act on it directly by key.
///
/// Sorting and filtering need no equivalent method here — see <see cref="StructureRewriter"/>'s own
/// doc comment for why neither one invalidates a path.
/// </summary>
public static class SidecarStructuralSync
{
    public static void OnRowInserted(FormulaSidecar sidecar, int insertIndex)
    {
        RewritePaths(sidecar.CellFormulas, path => StructureRewriter.OnRowInserted(path, insertIndex));
        RewritePaths(sidecar.State, path => StructureRewriter.OnRowInserted(path, insertIndex));
        RewritePaths(sidecar.ExcludedCells, path => StructureRewriter.OnRowInserted(path, insertIndex));
    }

    /// <summary>Returns the keys (formula/state paths) that pointed at the removed row and were
    /// dropped — the caller may want to know, e.g. to tell the user "N formulas were removed with
    /// this row" or to capture them for undo.</summary>
    public static IReadOnlyList<string> OnRowRemoved(FormulaSidecar sidecar, int removedIndex)
    {
        var droppedFormulas = RewritePaths(sidecar.CellFormulas, path => StructureRewriter.OnRowRemoved(path, removedIndex));
        var droppedState = RewritePaths(sidecar.State, path => StructureRewriter.OnRowRemoved(path, removedIndex));
        RewritePaths(sidecar.ExcludedCells, path => StructureRewriter.OnRowRemoved(path, removedIndex));
        return droppedFormulas.Concat(droppedState).ToList();
    }

    /// <summary><paramref name="oldKeySegments"/>/<paramref name="newKeySegments"/> are the column
    /// key split into path segments — one for a flat column, several for an expanded nested field
    /// (see CLAUDE.md's "Nested JSON paths"). <paramref name="oldKey"/>/<paramref name="newKey"/>
    /// are the joined forms, still used verbatim as <see cref="FormulaSidecar.ColumnFormulas"/>
    /// keys.</summary>
    public static void OnColumnRenamed(FormulaSidecar sidecar, string oldKey, string newKey,
        IReadOnlyList<string> oldKeySegments, IReadOnlyList<string> newKeySegments)
    {
        if (sidecar.ColumnFormulas.Remove(oldKey, out var entry))
            sidecar.ColumnFormulas[newKey] = entry;

        RewritePaths(sidecar.CellFormulas, path => StructureRewriter.OnColumnRenamed(path, oldKeySegments, newKeySegments));
        RewritePaths(sidecar.State, path => StructureRewriter.OnColumnRenamed(path, oldKeySegments, newKeySegments));
        RewritePaths(sidecar.ExcludedCells, path => StructureRewriter.OnColumnRenamed(path, oldKeySegments, newKeySegments));
    }

    /// <summary>Returns the same "what got dropped" list as <see cref="OnRowRemoved"/>, plus the
    /// column formula key itself if that column had one. <paramref name="removedKeySegments"/> is
    /// the column key split into path segments; <paramref name="removedKey"/> is the joined form
    /// used as the <see cref="FormulaSidecar.ColumnFormulas"/> key.</summary>
    public static IReadOnlyList<string> OnColumnRemoved(FormulaSidecar sidecar, string removedKey,
        IReadOnlyList<string> removedKeySegments)
    {
        var dropped = new List<string>();
        if (sidecar.ColumnFormulas.Remove(removedKey))
            dropped.Add(removedKey);

        var droppedFormulas = RemoveReferencing(sidecar.CellFormulas, removedKeySegments);
        var droppedState = RemoveReferencing(sidecar.State, removedKeySegments);
        RemoveReferencing(sidecar.ExcludedCells, removedKeySegments);
        dropped.AddRange(droppedFormulas);
        dropped.AddRange(droppedState);
        return dropped;
    }

    /// <summary>Set flavour of <see cref="RewritePaths{TValue}"/> — same rules, no values.</summary>
    private static void RewritePaths(HashSet<string> set, Func<JsonPath, JsonPath?> rewrite)
    {
        var replacements = new List<(string OldKey, string? NewKey)>();
        foreach (var key in set)
        {
            JsonPath parsed;
            try { parsed = JsonPath.Parse(key); }
            catch (FormatException) { continue; }

            var newKey = rewrite(parsed)?.ToCanonicalString();
            if (newKey != key) replacements.Add((key, newKey));
        }

        foreach (var (oldKey, newKey) in replacements)
        {
            set.Remove(oldKey);
            if (newKey is not null) set.Add(newKey);
        }
    }

    // Re-keys every entry in `dict` via `rewrite`; an entry whose path rewrites to null (the
    // mutation invalidated it — e.g. it pointed at a now-removed row) is dropped, and its old key
    // is returned so the caller can report what was lost.
    private static List<string> RewritePaths<TValue>(Dictionary<string, TValue> dict, Func<JsonPath, JsonPath?> rewrite)
    {
        var dropped = new List<string>();
        var replacements = new List<(string OldKey, string? NewKey, TValue Value)>();

        foreach (var (key, value) in dict)
        {
            JsonPath parsed;
            try { parsed = JsonPath.Parse(key); }
            catch (FormatException) { continue; } // not a concrete path (shouldn't happen for these dictionaries) — leave it alone

            var rewritten = rewrite(parsed);
            var newKey = rewritten?.ToCanonicalString();
            if (newKey != key)
                replacements.Add((key, newKey, value));
        }

        foreach (var (oldKey, newKey, value) in replacements)
        {
            dict.Remove(oldKey);
            if (newKey is null) dropped.Add(oldKey);
            else dict[newKey] = value;
        }

        return dropped;
    }

    /// <summary>Set flavour of <see cref="RemoveReferencing{TValue}"/>.</summary>
    private static void RemoveReferencing(HashSet<string> set, IReadOnlyList<string> columnKeySegments)
    {
        var toRemove = new List<string>();
        foreach (var key in set)
        {
            JsonPath parsed;
            try { parsed = JsonPath.Parse(key); }
            catch (FormatException) { continue; }

            if (StructureRewriter.ReferencesColumn(parsed, columnKeySegments)) toRemove.Add(key);
        }

        foreach (var key in toRemove) set.Remove(key);
    }

    private static List<string> RemoveReferencing<TValue>(Dictionary<string, TValue> dict, IReadOnlyList<string> columnKeySegments)
    {
        var toRemove = new List<string>();
        foreach (var key in dict.Keys)
        {
            JsonPath parsed;
            try { parsed = JsonPath.Parse(key); }
            catch (FormatException) { continue; }

            if (StructureRewriter.ReferencesColumn(parsed, columnKeySegments))
                toRemove.Add(key);
        }

        foreach (var key in toRemove)
            dict.Remove(key);

        return toRemove;
    }
}
