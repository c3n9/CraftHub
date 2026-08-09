using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CraftHub.Domain.Models;

namespace CraftHub.Helpers;

/// <summary>
/// Structural (path-aware) JSON comparison: walks both trees in parallel and reports what changed
/// at each JSON path, as opposed to <see cref="DiffEngine"/>'s line-by-line text diff.
/// </summary>
public static class StructuralJsonDiff
{
    /// <summary>Compares two documents and returns the root node. Unchanged subtrees are pruned,
    /// so only changed leaves and the ancestors leading to them survive.</summary>
    public static JsonDiffNode Compare(JsonElement oldEl, JsonElement newEl)
    {
        var root = Build(oldEl, newEl, "$", "$", string.Empty);
        Prune(root);
        return root;
    }

    /// <summary>Escapes a property name for use in a JSON Pointer segment (RFC 6901).</summary>
    private static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

    private static JsonDiffNode Build(
        JsonElement oldEl, JsonElement newEl, string path, string name, string pointer)
    {
        if (oldEl.ValueKind != newEl.ValueKind)
        {
            return new JsonDiffNode
            {
                Path = path,
                Pointer = pointer,
                Name = name,
                ChangeType = JsonDiffChangeType.TypeChanged,
                OldValue = Stringify(oldEl),
                NewValue = Stringify(newEl)
            };
        }

        switch (oldEl.ValueKind)
        {
            case JsonValueKind.Object:
                return BuildObject(oldEl, newEl, path, name, pointer);
            case JsonValueKind.Array:
                return BuildArray(oldEl, newEl, path, name, pointer);
            default:
                var same = Stringify(oldEl) == Stringify(newEl);
                return new JsonDiffNode
                {
                    Path = path,
                    Pointer = pointer,
                    Name = name,
                    ChangeType = same ? JsonDiffChangeType.Unchanged : JsonDiffChangeType.Replaced,
                    OldValue = Stringify(oldEl),
                    NewValue = Stringify(newEl)
                };
        }
    }

    private static JsonDiffNode BuildObject(
        JsonElement oldEl, JsonElement newEl, string path, string name, string pointer)
    {
        var node = new JsonDiffNode
        {
            Path = path, Pointer = pointer, Name = name, ChangeType = JsonDiffChangeType.Unchanged
        };

        var oldProps = oldEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        var newProps = newEl.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

        // Union in old-first order so a diff reads in roughly document order rather than alphabetically.
        var keys = oldProps.Keys.Concat(newProps.Keys.Where(k => !oldProps.ContainsKey(k)));

        foreach (var key in keys)
        {
            var childPath = $"{path}.{key}";
            var childPointer = $"{pointer}/{Escape(key)}";
            var inOld = oldProps.TryGetValue(key, out var oldChild);
            var inNew = newProps.TryGetValue(key, out var newChild);

            if (inOld && inNew)
                node.Children.Add(Build(oldChild, newChild, childPath, key, childPointer));
            else if (inOld)
                node.Children.Add(Leaf(childPath, childPointer, key, JsonDiffChangeType.Removed, Stringify(oldChild), null));
            else
                node.Children.Add(Leaf(childPath, childPointer, key, JsonDiffChangeType.Added, null, Stringify(newChild)));
        }

        return node;
    }

    private static JsonDiffNode BuildArray(
        JsonElement oldEl, JsonElement newEl, string path, string name, string pointer)
    {
        var node = new JsonDiffNode
        {
            Path = path, Pointer = pointer, Name = name, ChangeType = JsonDiffChangeType.Unchanged
        };

        var oldItems = oldEl.EnumerateArray().ToList();
        var newItems = newEl.EnumerateArray().ToList();

        // Index-aligned: element i is compared with element i. Matches the "was/now by path" spec;
        // order-insensitive array comparison is a separate opt-in mode added later.
        for (var i = 0; i < Math.Max(oldItems.Count, newItems.Count); i++)
        {
            var childPath = $"{path}[{i}]";
            var childName = $"[{i}]";
            var childPointer = $"{pointer}/{i}";

            if (i < oldItems.Count && i < newItems.Count)
                node.Children.Add(Build(oldItems[i], newItems[i], childPath, childName, childPointer));
            else if (i < oldItems.Count)
                node.Children.Add(Leaf(childPath, childPointer, childName, JsonDiffChangeType.Removed, Stringify(oldItems[i]), null));
            else
                node.Children.Add(Leaf(childPath, childPointer, childName, JsonDiffChangeType.Added, null, Stringify(newItems[i])));
        }

        return node;
    }

    private static JsonDiffNode Leaf(
        string path, string pointer, string name, JsonDiffChangeType type, string? oldValue, string? newValue)
        => new()
        {
            Path = path, Pointer = pointer, Name = name,
            ChangeType = type, OldValue = oldValue, NewValue = newValue
        };

    private static void Prune(JsonDiffNode node)
    {
        node.Children.RemoveAll(c => !c.HasChanges);
        foreach (var child in node.Children)
            Prune(child);
    }

    /// <summary>Compact rendering of a value for the "was"/"now" columns. Keeps raw JSON syntax so
    /// a type change is legible — the string "1" shows as <c>"1"</c>, the number as <c>1</c>.</summary>
    private static string Stringify(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        _ => el.GetRawText()
    };
}
