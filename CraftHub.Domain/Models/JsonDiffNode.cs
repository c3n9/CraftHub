using System.Collections.Generic;

namespace CraftHub.Domain.Models
{
    public enum JsonDiffChangeType
    {
        Unchanged,
        Added,
        Removed,
        Replaced,
        TypeChanged
    }

    /// <summary>One node of a structural JSON comparison, keyed by its JSON path
    /// (<c>$.users[0].name</c>). Container nodes carry <see cref="Children"/>; leaf nodes carry the
    /// before/after values.</summary>
    public sealed class JsonDiffNode
    {
        public required string Path { get; init; }

        /// <summary>
        /// RFC 6901 JSON Pointer for this node (<c>/users/0/name</c>), carried alongside the display
        /// <see cref="Path"/> because deriving one from the other is lossy — a key containing
        /// <c>.</c> or <c>[</c> is ambiguous in the display form. Required for JSON Patch export.
        /// </summary>
        public string Pointer { get; init; } = string.Empty;

        /// <summary>Last path segment (property name or <c>[i]</c>) — what a tree view shows per row.</summary>
        public required string Name { get; init; }

        public JsonDiffChangeType ChangeType { get; init; }
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }

        public List<JsonDiffNode> Children { get; } = new();

        /// <summary>True when this node, or anything below it, actually differs — lets a caller
        /// prune whole unchanged subtrees while keeping the ancestors of a deep change.</summary>
        public bool HasChanges => ChangeType != JsonDiffChangeType.Unchanged || Children.Exists(c => c.HasChanges);
    }
}
