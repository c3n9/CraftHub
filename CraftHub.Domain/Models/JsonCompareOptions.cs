using System.Collections.Generic;

namespace CraftHub.Domain.Models
{
    /// <summary>
    /// What counts as a difference. Applied by normalizing both documents before they are compared,
    /// so one set of options drives the text diff and the structural diff alike — the two views can
    /// never disagree about what was ignored.
    /// </summary>
    /// <param name="IgnoreKeyOrder">Rewrite objects with keys in ordinal order, so property order stops mattering.</param>
    /// <param name="IgnoreArrayOrder">Sort array elements by their canonical form — arrays compare as multisets.</param>
    /// <param name="CaseInsensitiveStrings">Fold string values to lower case before comparing.</param>
    /// <param name="IgnoreNullAndEmpty">Drop values that are null, "", [] or {}.</param>
    /// <param name="IgnoredPaths">
    /// JSON paths to exclude, e.g. <c>$.updatedAt</c>. Matching also covers everything underneath a
    /// listed path, and array indices are optional — <c>$.items.id</c> matches <c>$.items[3].id</c>.
    /// </param>
    public sealed record JsonCompareOptions(
        bool IgnoreKeyOrder = false,
        bool IgnoreArrayOrder = false,
        bool CaseInsensitiveStrings = false,
        bool IgnoreNullAndEmpty = false,
        IReadOnlyList<string>? IgnoredPaths = null)
    {
        /// <summary>Everything off: compare the documents exactly as written.</summary>
        public static JsonCompareOptions Default { get; } = new();

        /// <summary>True when normalization would leave the document unchanged, letting callers
        /// skip the whole rewrite and just re-indent.</summary>
        public bool IsDefault =>
            !IgnoreKeyOrder && !IgnoreArrayOrder && !CaseInsensitiveStrings
            && !IgnoreNullAndEmpty && !HasIgnoredPaths;

        /// <summary>Alias of <see cref="IsDefault"/>, named for the normalization side of the same
        /// question: nothing to ignore means the rewrite is a no-op.</summary>
        public bool IsIdentityTransform => IsDefault;

        public bool HasIgnoredPaths => IgnoredPaths is { Count: > 0 };
    }
}
