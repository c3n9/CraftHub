using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Models;
using CraftHub.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace CraftHub.ViewModels;

public partial class JsonEditorViewModel : ViewModelBase
{
    private readonly IJsonService _jsonService;
    private readonly IDialogService _dialogService;
    private readonly NotificationService _notificationService;
    private readonly JsonFieldType _expectedType;

    [ObservableProperty] private string _propertyNameInput = string.Empty;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private bool _isObjectMode;

    public ObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public ObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    public event EventHandler<string>? JsonSubmitted;
    public event EventHandler? Cancelled;

    public JsonEditorViewModel(string initialJson, JsonFieldType expectedType, IJsonService jsonService, IDialogService dialogService, NotificationService notificationService)
    {
        _jsonService = jsonService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _expectedType = expectedType;
        IsObjectMode = expectedType == JsonFieldType.Object;

        if (!string.IsNullOrWhiteSpace(initialJson))
        {
            try
            {
                if (!initialJson.TrimStart().StartsWith("{") && !initialJson.TrimStart().StartsWith("["))
                {
                    initialJson = expectedType == JsonFieldType.Array ? "[]" : "{}";
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
                foreach (var row in dataRows)
                {
                    Rows.Add(row);
                }
            }
            catch
            {
                // best-effort parsing, silently continue
            }
        }

        if (Rows.Count == 0 && IsObjectMode)
        {
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
    private async Task RemovePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;
        var confirmed = await _dialogService.ShowConfirmAsync(
            "Remove property",
            $"Remove '{prop.Name}' from the schema?");
        if (!confirmed)
        {
            return;
        }
        Properties.Remove(prop);
        foreach (var row in Rows) row.RemoveProperty(prop.Name);
    }

    [RelayCommand]
    private void AddRow()
    {
        if (IsObjectMode) return;
        var row = new DynamicDataRow();
        foreach (var prop in Properties) row.InitializeProperty(prop.Name);
        Rows.Add(row);
    }

    [RelayCommand]
    private void Submit()
    {
        try
        {
            var json = _jsonService.SerializeToJson(Rows, Properties);

            if (IsObjectMode)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    json = doc.RootElement[0].GetRawText();
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
            _notificationService.Publish(NotificationType.Error, $"Editor error: {ex.Message}");
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
            var idx = Rows.IndexOf(row);
            if (idx >= 0)
            {
                Rows[idx] = newRow;
            }

            _notificationService.Publish(NotificationType.Success, $"Updated '{propertyName}'");
        }
    }
}
