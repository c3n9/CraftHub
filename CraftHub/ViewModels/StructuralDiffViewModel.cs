using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.ViewModels;

/// <summary>One visible row of the structural diff tree, flattened for a columned grid. Depth is
/// rendered as indentation so the hierarchy still reads as a tree.</summary>
public sealed partial class StructuralDiffRowViewModel : ObservableObject
{
    private readonly JsonDiffNode _node;

    [ObservableProperty] private bool _isExpanded = true;

    public StructuralDiffRowViewModel(JsonDiffNode node, int depth)
    {
        _node = node;
        Depth = depth;
    }

    public int Depth { get; }
    public IReadOnlyList<JsonDiffNode> Children => _node.Children;
    public bool HasChildren => _node.Children.Count > 0;

    public string Name => _node.Name;
    public string Path => _node.Path;
    public string? OldValue => _node.OldValue;
    public string? NewValue => _node.NewValue;
    public string ChangeLabel => LabelFor(_node.ChangeType);

    // Style-class flags, so the colours come from the theme in XAML rather than from here.
    public bool IsAdded => _node.ChangeType == JsonDiffChangeType.Added;
    public bool IsRemoved => _node.ChangeType == JsonDiffChangeType.Removed;
    public bool IsChanged => _node.ChangeType is JsonDiffChangeType.Replaced or JsonDiffChangeType.TypeChanged;

    /// <summary>Indent for the first column; the chevron column is a fixed width on top of this.</summary>
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    private static string LabelFor(JsonDiffChangeType type) => type switch
    {
        JsonDiffChangeType.Added => Localizer.Get("StructChangeAdded"),
        JsonDiffChangeType.Removed => Localizer.Get("StructChangeRemoved"),
        JsonDiffChangeType.Replaced => Localizer.Get("StructChangeReplaced"),
        JsonDiffChangeType.TypeChanged => Localizer.Get("StructChangeTypeChanged"),
        _ => string.Empty
    };
}

/// <summary>
/// Path-aware comparison shown as a tree: which JSON paths were added, removed, replaced or changed
/// type. Complements <see cref="DiffViewModel"/>'s line-by-line text diff.
/// </summary>
public sealed partial class StructuralDiffViewModel : ObservableObject
{
    private JsonDiffNode? _root;

    /// <summary>The pruned comparison tree, or null while the JSON doesn't parse. Exposed so the
    /// export formats can be built from the same tree the grid is showing.</summary>
    public JsonDiffNode? Root => _root;

    public ObservableCollection<StructuralDiffRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _isIdentical;
    [ObservableProperty] private bool _isUnavailable;
    [ObservableProperty] private string? _unavailableReason;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Note there's no "ignore key order" option here: a structural comparison matches
    /// properties by name, so key order never affects the result to begin with.</summary>
    public async Task SetTextsAsync(string oldText, string newText)
    {
        IsBusy = true;
        try
        {
            await BuildAsync(oldText, newText);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BuildAsync(string oldText, string newText)
    {
        var result = await Task.Run(() =>
        {
            // Structural comparison needs both sides to actually parse; the text diff still works
            // when they don't, which is why that failure is reported rather than thrown.
            if (!JsonDiffHelper.TryParseJson(oldText ?? string.Empty, out var oldDoc, out var oldError))
                return (Root: (JsonDiffNode?)null, Error: oldError);

            using (oldDoc)
            {
                if (!JsonDiffHelper.TryParseJson(newText ?? string.Empty, out var newDoc, out var newError))
                    return (Root: (JsonDiffNode?)null, Error: newError);

                using (newDoc)
                    return (Root: StructuralJsonDiff.Compare(oldDoc!.RootElement, newDoc!.RootElement),
                            Error: (string?)null);
            }
        });

        Rows.Clear();
        _root = result.Root;

        if (result.Root == null)
        {
            IsUnavailable = true;
            UnavailableReason = result.Error;
            IsIdentical = false;
            return;
        }

        IsUnavailable = false;
        UnavailableReason = null;
        IsIdentical = result.Root.Children.Count == 0;

        // Expanded by default: everything left after pruning is a change or the path to one, so
        // there's no noise to hide.
        foreach (var child in result.Root.Children)
            AppendRecursive(child, depth: 0);
    }

    private void AppendRecursive(JsonDiffNode node, int depth)
    {
        Rows.Add(new StructuralDiffRowViewModel(node, depth));
        foreach (var child in node.Children)
            AppendRecursive(child, depth + 1);
    }

    /// <summary>Collapsing removes the row's whole subtree from the flat list; expanding splices it
    /// back in right after the row, which is what keeps a plain grid behaving like a tree.</summary>
    [RelayCommand]
    private void ToggleExpand(StructuralDiffRowViewModel? row)
    {
        if (row == null || !row.HasChildren) return;

        var index = Rows.IndexOf(row);
        if (index < 0) return;

        if (row.IsExpanded)
        {
            // Descendants are exactly the following rows that are deeper than this one.
            var removeCount = 0;
            for (var i = index + 1; i < Rows.Count && Rows[i].Depth > row.Depth; i++)
                removeCount++;

            for (var i = 0; i < removeCount; i++)
                Rows.RemoveAt(index + 1);

            row.IsExpanded = false;
        }
        else
        {
            var insertAt = index + 1;
            foreach (var child in row.Children)
                insertAt = InsertRecursive(child, row.Depth + 1, insertAt);

            row.IsExpanded = true;
        }
    }

    private int InsertRecursive(JsonDiffNode node, int depth, int index)
    {
        Rows.Insert(index++, new StructuralDiffRowViewModel(node, depth));
        foreach (var child in node.Children)
            index = InsertRecursive(child, depth + 1, index);
        return index;
    }
}
