using CommunityToolkit.Mvvm.ComponentModel;

namespace CraftHub.Models;

/// <summary>
/// Represents a detected JSON field and user-selected type for import mapping.
/// </summary>
public partial class JsonFieldMapping : ObservableObject
{
    [ObservableProperty] private string _fieldName = string.Empty;
    [ObservableProperty] private JsonFieldType _detectedType = JsonFieldType.String;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private string _sampleValue = string.Empty;
}
