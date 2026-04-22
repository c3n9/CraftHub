using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.ViewModels;

public partial class JsonFieldMappingViewModel : ViewModelBase
{
    public ObservableCollection<JsonFieldMapping> Fields { get; }
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    [ObservableProperty] private bool _confirmed;

    /// <summary>File name shown in the dialog header to avoid confusion during multi-file import.</summary>
    public string? FileName { get; }

    public JsonFieldMappingViewModel(List<JsonFieldMapping> fields, string? fileName = null)
    {
        Fields = new ObservableCollection<JsonFieldMapping>(fields);
        FileName = fileName;
    }
}
