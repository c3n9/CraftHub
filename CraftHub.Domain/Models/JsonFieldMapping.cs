using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Domain.Enums;

namespace CraftHub.Domain.Models;

/// <summary>
/// Represents a detected JSON field and user-selected type for import mapping.
/// </summary>
public partial class JsonFieldMapping : ObservableObject
{
    [ObservableProperty] private string _fieldName = string.Empty;
    [ObservableProperty] private JsonFieldType _detectedType;
    [ObservableProperty] private JsonFieldType _selectedType;
    [ObservableProperty] private string _sampleValue = string.Empty;
}
