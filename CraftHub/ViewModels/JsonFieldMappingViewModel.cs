using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Models;

namespace CraftHub.ViewModels;

public partial class JsonFieldMappingViewModel : ViewModelBase
{
    public ObservableCollection<JsonFieldMapping> Fields { get; }
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    [ObservableProperty] private bool _confirmed;

    public JsonFieldMappingViewModel(List<JsonFieldMapping> fields)
    {
        Fields = new ObservableCollection<JsonFieldMapping>(fields);
    }

    // Commands are bound by dialog code-behind to close with result
}
