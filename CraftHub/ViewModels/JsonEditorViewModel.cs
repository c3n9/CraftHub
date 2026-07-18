using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.Services.Actions;
using System;
using System.Collections;
using System.Collections.Generic;
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
    [ObservableProperty] private DynamicDataRow? _selectedRow;

    public ObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public ObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));

    public event EventHandler<string>? JsonSubmitted;
    public event EventHandler? Cancelled;

    public UndoRedoService UndoRedo { get; } = new();

    private bool CanUndo => UndoRedo.CanUndo;
    private bool CanRedo => UndoRedo.CanRedo;

    [ObservableProperty] private string _undoTooltip = string.Empty;
    [ObservableProperty] private string _redoTooltip = string.Empty;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    private void RefreshUndoRedoState()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        UndoTooltip = UndoRedo.UndoDescription is { } u
            ? $"{Localizer.Get("UndoTip")}: {u}" : Localizer.Get("UndoTip");
        RedoTooltip = UndoRedo.RedoDescription is { } r
            ? $"{Localizer.Get("RedoTip")}: {r}" : Localizer.Get("RedoTip");
    }

    public JsonEditorViewModel(string initialJson, JsonFieldType expectedType, IJsonService jsonService, IDialogService dialogService, NotificationService notificationService,
        IReadOnlyList<JsonPropertyDefinition>? sharedProperties = null)
    {
        _jsonService = jsonService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _expectedType = expectedType;
        IsObjectMode = expectedType == JsonFieldType.Object;

        // Seed schema from shared properties first (other rows' merged schema).
        if (sharedProperties != null)
        {
            foreach (var p in sharedProperties)
                Properties.Add(new JsonPropertyDefinition { Name = p.Name, FieldType = p.FieldType });
        }

        if (!string.IsNullOrWhiteSpace(initialJson))
        {
            try
            {
                if (!initialJson.TrimStart().StartsWith("{") && !initialJson.TrimStart().StartsWith("["))
                {
                    initialJson = expectedType == JsonFieldType.Array ? "[]" : "{}";
                }

                // Detect any extra fields in this cell not yet in the shared schema.
                var detectedFields = _jsonService.DetectFields(initialJson);
                foreach (var field in detectedFields)
                {
                    if (!Properties.Any(p => p.Name == field.FieldName))
                    {
                        Properties.Add(new JsonPropertyDefinition
                        {
                            Name = field.FieldName,
                            FieldType = field.SelectedType
                        });
                    }
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
            var emptyRow = new DynamicDataRow();
            foreach (var p in Properties) emptyRow.InitializeProperty(p.Name);
            Rows.Add(emptyRow);
        }

        UndoRedo.PropertyChanged += (_, _) => RefreshUndoRedoState();
        RefreshUndoRedoState();
    }

    [RelayCommand]
    private void AddProperty()
    {
        if (string.IsNullOrWhiteSpace(PropertyNameInput))
        {
            _notificationService.Publish(NotificationType.Warning, Localizer.Get("EnterPropertyName"));
            return;
        }
        if (Properties.Any(p => p.Name == PropertyNameInput))
        {
            _notificationService.Publish(NotificationType.Warning, Localizer.Get("PropertyAlreadyExists"));
            return;
        }

        var prop = new JsonPropertyDefinition
        {
            Name = PropertyNameInput,
            FieldType = SelectedType
        };

        Properties.Add(prop);
        foreach (var row in Rows) row.InitializeProperty(prop.Name);

        // Columns are patched incrementally off Properties.CollectionChanged, so the action's
        // "columns changed" callback is a no-op here.
        UndoRedo.Push(new AddPropertyAction(Properties, Rows, prop, () => { }));

        PropertyNameInput = string.Empty;
    }

    [RelayCommand]
    private async Task RemovePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;
        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemovePropertyTitle"),
            Localizer.Get("RemovePropertyMsg", prop.Name));
        if (!confirmed)
        {
            return;
        }

        var propIndex = Properties.IndexOf(prop);
        var savedValues = Rows.ToDictionary(r => r, r => r[prop.Name]);

        Properties.Remove(prop);
        foreach (var row in Rows) row.RemoveProperty(prop.Name);

        UndoRedo.Push(new RemovePropertyAction(Properties, Rows, prop, propIndex, savedValues, () => { }));
    }

    [RelayCommand]
    private void AddRow()
    {
        if (IsObjectMode) return;
        var row = new DynamicDataRow();
        foreach (var prop in Properties) row.InitializeProperty(prop.Name);
        Rows.Add(row);
        UndoRedo.Push(new AddRowAction(Rows, row));
    }

    // ---- Row operations (context menu) ----

    [RelayCommand]
    private void DuplicateRows(object? parameter)
    {
        var source = ResolveSelectedRows(parameter);
        if (source == null || source.Count == 0) return;

        var duplicated = source.Select(CreateDuplicateRow).ToList();
        foreach (var r in duplicated) Rows.Add(r);

        UndoRedo.Push(new DuplicateRowsAction(Rows, duplicated));
        _notificationService.Publish(NotificationType.Success, Localizer.Get("RowsDuplicatedMsg", source.Count));
    }

    [RelayCommand]
    private async Task CopyRowsToJsonAsync(object? parameter)
    {
        var rows = ResolveSelectedRows(parameter);
        if (rows == null || rows.Count == 0) return;

        var json = rows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(rows[0], Properties)
            : _jsonService.SerializeToJson(rows, Properties);

        await _dialogService.CopyToClipboardAsync(json);
        _notificationService.Publish(NotificationType.Success, Localizer.Get("RowsCopiedMsg", rows.Count));
    }

    [RelayCommand]
    private async Task CopyRowsToJsonAsObjectsAsync(object? parameter)
    {
        var rows = ResolveSelectedRows(parameter);
        if (rows == null || rows.Count == 0) return;

        var json = rows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(rows[0], Properties)
            : string.Join(", ", rows.Select(r => _jsonService.SerializeSingleRowToJson(r, Properties)));

        await _dialogService.CopyToClipboardAsync(json);
        _notificationService.Publish(NotificationType.Success, Localizer.Get("RowsCopiedMsg", rows.Count));
    }

    [RelayCommand]
    private async Task RemoveRowsAsync(object? parameter)
    {
        var toRemove = ResolveSelectedRows(parameter);
        if (toRemove == null || toRemove.Count == 0) return;

        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemoveRowsTitle"),
            Localizer.Get("RemoveRowsMsg", toRemove.Count));
        if (!confirmed) return;

        // Capture indices before removal so undo can restore positions.
        var withIndices = toRemove
            .Select(r => (Index: Rows.IndexOf(r), Row: r))
            .Where(x => x.Index >= 0)
            .ToList();

        foreach (var item in withIndices) Rows.Remove(item.Row);

        UndoRedo.Push(new RemoveRowsAction(Rows, withIndices.Select(x => (x.Index, x.Row))));
        _notificationService.Publish(NotificationType.Success, Localizer.Get("RowsRemovedMsg", withIndices.Count));
    }

    private List<DynamicDataRow>? ResolveSelectedRows(object? parameter)
    {
        if (parameter is IList { Count: > 0 } list)
            return list.Cast<DynamicDataRow>().ToList();
        if (SelectedRow != null)
            return new List<DynamicDataRow> { SelectedRow };
        return null;
    }

    private DynamicDataRow CreateDuplicateRow(DynamicDataRow row)
    {
        var newRow = new DynamicDataRow();
        foreach (var prop in Properties)
            newRow.InitializeProperty(prop.Name, row[prop.Name]);
        return newRow;
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
            _notificationService.Publish(NotificationType.Error, Localizer.Get("EditorErrorMsg", ex.Message));
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
        // Merge schema from all rows in this nested column.
        var merged = new System.Collections.Generic.List<JsonPropertyDefinition>();
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var r in Rows)
        {
            var val = r[propertyName];
            if (string.IsNullOrWhiteSpace(val)) continue;
            try
            {
                foreach (var f in _jsonService.DetectFields(val))
                    if (seen.Add(f.FieldName))
                        merged.Add(new JsonPropertyDefinition { Name = f.FieldName, FieldType = f.SelectedType });
            }
            catch { }
        }

        var newValue = await _dialogService.ShowJsonEditorDialogAsync(
            Localizer.Get("EditCellTitle", propertyName), currentValue, type, _jsonService, merged.Count > 0 ? merged : null);
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
                UndoRedo.Push(new EditJsonCellAction(Rows, row, newRow, propertyName));
            }

            _notificationService.Publish(NotificationType.Success, Localizer.Get("CellUpdatedMsg", propertyName));
        }
    }
}
