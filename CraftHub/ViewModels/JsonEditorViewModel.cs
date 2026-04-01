using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Models;
using CraftHub.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace CraftHub.ViewModels;

public partial class JsonEditorViewModel : ViewModelBase
{
    private readonly IJsonService _jsonService;
    private readonly IDialogService _dialogService;
    private readonly JsonFieldType _expectedType;

    [ObservableProperty] private string _propertyNameInput = string.Empty;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private bool _isObjectMode;
    [ObservableProperty] private string _statusText = "Ready";

    public ObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public ObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    public event EventHandler<string>? JsonSubmitted;
    public event EventHandler? Cancelled;

    public JsonEditorViewModel(string initialJson, JsonFieldType expectedType, IJsonService jsonService, IDialogService dialogService)
    {
        _jsonService = jsonService;
        _dialogService = dialogService;
        _expectedType = expectedType;
        IsObjectMode = expectedType == JsonFieldType.Object;
        
        // Initialize from JSON
        if (!string.IsNullOrWhiteSpace(initialJson))
        {
            try
            {
                // Ensure it looks like object or array
                if (!initialJson.TrimStart().StartsWith("{") && !initialJson.TrimStart().StartsWith("["))
                {
                    if (expectedType == JsonFieldType.Array) initialJson = "[]";
                    else initialJson = "{}";
                }

                var detectedFields = _jsonService.DetectFields(initialJson);
                foreach (var field in detectedFields)
                {
                    Properties.Add(new JsonPropertyDefinition
                    {
                        Name = field.FieldName,
                        FieldType = field.SelectedType
                    });
                }
                
                var dataRows = _jsonService.ParseJsonData(initialJson, Properties);
                foreach (var r in dataRows) Rows.Add(r);
            }
            catch { }
        }

        if (Rows.Count == 0 && IsObjectMode)
        {
            // Object mode must have 1 row!
            Rows.Add(new DynamicDataRow());
        }
    }

    [RelayCommand]
    private void AddProperty()
    {
        if (string.IsNullOrWhiteSpace(PropertyNameInput)) return;
        if (Properties.Any(p => p.Name == PropertyNameInput)) return;

        var prop = new JsonPropertyDefinition
        {
            Name = PropertyNameInput,
            FieldType = SelectedType
        };
        Properties.Add(prop);
        foreach (var row in Rows) row.InitializeProperty(prop.Name);
        PropertyNameInput = string.Empty;
    }

    [RelayCommand]
    private void RemoveProperty(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;
        Properties.Remove(prop);
        foreach (var row in Rows) row.RemoveProperty(prop.Name);
    }

    [RelayCommand]
    private void AddRow()
    {
        if (IsObjectMode) return; // Cannot add rows to an object
        var row = new DynamicDataRow();
        foreach (var prop in Properties) row.InitializeProperty(prop.Name);
        Rows.Add(row);
    }

    [RelayCommand]
    private void Submit()
    {
        try
        {
            // Serialize
            // Note: SerializeToJson expects an array output currently. We need to handle object vs array correctly.
            var json = _jsonService.SerializeToJson(Rows, Properties);
            
            // SerializeToJson always returns an array `[ { ... } ]`. If we expected an object, we need to extract the first element!
            if (IsObjectMode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    json = doc.RootElement[0].GetRawText(); // The actual `{ ... }`
                }
                else
                {
                    json = "{}";
                }
            }

            JsonSubmitted?.Invoke(this, json);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    public async System.Threading.Tasks.Task EditJsonCellAsync(DynamicDataRow row, string propertyName, JsonFieldType type)
    {
        var currentValue = row[propertyName];
        var newValue = await _dialogService.ShowJsonEditorDialogAsync($"Edit {propertyName}", currentValue, type, _jsonService);
        if (newValue != null && newValue != currentValue)
        {
            var newRow = new DynamicDataRow();
            foreach (var kvp in row.GetAllValues())
            {
                newRow.InitializeProperty(kvp.Key, kvp.Value);
            }
            newRow[propertyName] = newValue;
            
            // Force Avalonia DataGrid to refresh the row entirely
            var idx = Rows.IndexOf(row);
            if (idx >= 0) 
            {
                Rows[idx] = newRow;
            }

            StatusText = $"✓ Updated recursive '{propertyName}'";
        }
    }
}
