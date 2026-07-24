using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Domain.Enums;

namespace CraftHub.Domain.Models;

/// <summary>
/// Represents a detected JSON field and user-selected type for import mapping.
/// Fields form a tree: a nested object keeps its properties in <see cref="Children"/>
/// instead of being split into separate flat fields. By default a nested object is
/// imported as a single Object column; the user can expand it in the mapping dialog
/// to get one column per child instead.
/// </summary>
public partial class JsonFieldMapping : ObservableObject
{
    /// <summary>Separator between path segments of an expanded (flattened) field.</summary>
    public const char PathSeparator = '\x1E';

    /// <summary>Full path from the root, segments joined by <see cref="PathSeparator"/>.</summary>
    [ObservableProperty] private string _fieldName = string.Empty;

    [ObservableProperty] private JsonFieldType _detectedType;
    [ObservableProperty] private JsonFieldType _selectedType;
    [ObservableProperty] private string _sampleValue = string.Empty;

    /// <summary>
    /// True when the user chose to split this object into one column per child.
    /// </summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Properties of a nested object. Empty for leaves and for arrays.</summary>
    public List<JsonFieldMapping> Children { get; } = new();

    /// <summary>Last path segment — what the mapping dialog shows.</summary>
    public string DisplayName
    {
        get
        {
            var idx = FieldName.LastIndexOf(PathSeparator);
            return idx < 0 ? FieldName : FieldName[(idx + 1)..];
        }
    }

    /// <summary>Nesting level, used to indent the row in the mapping dialog.</summary>
    public int Depth
    {
        get
        {
            var depth = 0;
            foreach (var c in FieldName)
                if (c == PathSeparator)
                    depth++;
            return depth;
        }
    }

    /// <summary>Only objects with detected properties can be split into columns.</summary>
    public bool CanExpand => DetectedType == JsonFieldType.Object && Children.Count > 0;

    /// <summary>An expanded field is not a column itself, so its type cannot be chosen.</summary>
    public bool IsTypeSelectable => !IsExpanded;

    partial void OnFieldNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Depth));
    }

    partial void OnDetectedTypeChanged(JsonFieldType value) => OnPropertyChanged(nameof(CanExpand));

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(IsTypeSelectable));

    /// <summary>
    /// Turns a detected field tree into the flat list of columns to import:
    /// an expanded object is replaced by its children, everything else stays as-is.
    /// </summary>
    public static List<JsonFieldMapping> FlattenSelection(IEnumerable<JsonFieldMapping> roots)
    {
        var result = new List<JsonFieldMapping>();
        Walk(roots, result);
        return result;

        static void Walk(IEnumerable<JsonFieldMapping> nodes, List<JsonFieldMapping> acc)
        {
            foreach (var node in nodes)
            {
                if (node.IsExpanded && node.Children.Count > 0)
                    Walk(node.Children, acc);
                else
                    acc.Add(node);
            }
        }
    }
}
