using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.ViewModels;

public partial class JsonFieldMappingViewModel : ViewModelBase
{
    /// <summary>
    /// Detected field tree. The instances are shared with the caller, so the expansion
    /// state survives reopening the dialog after a type-validation error.
    /// </summary>
    public List<JsonFieldMapping> Roots { get; }

    /// <summary>Rows currently shown: roots plus the children of every expanded field.</summary>
    public ObservableCollection<JsonFieldMapping> Fields { get; } = new();

    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    [ObservableProperty] private bool _confirmed;

    /// <summary>File name shown in the dialog header to avoid confusion during multi-file import.</summary>
    public string? FileName { get; }

    public JsonFieldMappingViewModel(List<JsonFieldMapping> fields, string? fileName = null)
    {
        Roots = fields;
        FileName = fileName;
        RebuildVisibleFields();
    }

    /// <summary>Splits a nested object into one column per child (or folds it back).</summary>
    [RelayCommand]
    private void ToggleExpand(JsonFieldMapping? field)
    {
        if (field is not { CanExpand: true }) return;
        field.IsExpanded = !field.IsExpanded;
        RebuildVisibleFields();
    }

    /// <summary>The fields that will actually become columns.</summary>
    public List<JsonFieldMapping> GetResultFields() => JsonFieldMapping.FlattenSelection(Roots);

    private void RebuildVisibleFields()
    {
        Fields.Clear();
        AddVisible(Roots);

        void AddVisible(IEnumerable<JsonFieldMapping> nodes)
        {
            foreach (var node in nodes)
            {
                Fields.Add(node);
                if (node.IsExpanded) AddVisible(node.Children);
            }
        }
    }
}
