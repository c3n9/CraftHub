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

    public JsonFieldMappingViewModel(List<JsonFieldMapping> fields)
    {
        Fields = new ObservableCollection<JsonFieldMapping>(fields);
    }
}
