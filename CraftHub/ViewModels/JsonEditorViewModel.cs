using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Sidecar;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.Services.Actions;
using CraftHub.Services.Formulas;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace CraftHub.ViewModels;

public partial class JsonEditorViewModel : ViewModelBase
{
    private readonly IJsonService _jsonService;
    private readonly IDialogService _dialogService;
    private readonly NotificationService _notificationService;
    private readonly JsonFieldType _expectedType;
    private readonly IJsonEditorFormulaBridge? _formulaBridge;

    /// <summary>Formula engine over this dialog's own sub-table. Null when the dialog was opened
    /// without a bridge (e.g. an Object/Array cell nested inside another one — formulas are only
    /// supported one level deep for now).</summary>
    public FormulaSessionService? FormulaSession { get; }

    public bool FormulasEnabled => FormulaSession != null;

    /// <summary>Raised after a formula edit so the view can rebuild its rows (the fx marker and the
    /// computed value are not live bindings).</summary>
    public event EventHandler? FormulaVisualsChanged;

    [ObservableProperty] private string _propertyNameInput = string.Empty;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private bool _isObjectMode;
    [ObservableProperty] private bool _isPrimitiveArrayMode;
    [ObservableProperty] private DynamicDataRow? _selectedRow;

    /// <summary>Synthetic column that holds one element of a non-object array. Shared with
    /// <see cref="FormulaSessionService"/>, which has to rebuild the same shape when it recomputes
    /// formulas stored inside such an array.</summary>
    private const string PrimitiveArrayColumn = FormulaSessionService.PrimitiveArrayColumn;

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
        IReadOnlyList<JsonPropertyDefinition>? sharedProperties = null, IJsonEditorFormulaBridge? formulaBridge = null)
    {
        _jsonService = jsonService;
        _dialogService = dialogService;
        _notificationService = notificationService;
        _expectedType = expectedType;
        _formulaBridge = formulaBridge;
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

        // An array of plain values ("tags": [1, 2, 3]) has no properties to detect, so the
        // grid would come up empty and saving would replace the array with []. Show one row
        // per element instead, using a single synthetic column.
        if (!IsObjectMode && Properties.Count == 0)
            SeedPrimitiveArray(initialJson);

        if (Rows.Count == 0 && IsObjectMode)
        {
            var emptyRow = new DynamicDataRow();
            foreach (var p in Properties) emptyRow.InitializeProperty(p.Name);
            Rows.Add(emptyRow);
        }

        UndoRedo.PropertyChanged += (_, _) => RefreshUndoRedoState();
        RefreshUndoRedoState();

        // Formulas: a session over this dialog's own sub-table, seeded from whatever the parent
        // document already stored for this cell. Primitive-array mode is included — its single
        // synthetic "value" column is a perfectly good formula target, and FormulaSessionService
        // knows how to rebuild that shape when it recomputes the cell later.
        if (_formulaBridge != null)
        {
            FormulaSession = new FormulaSessionService(Rows, Properties, jsonService);
            var seeded = new FormulaSidecar
            {
                Target = new TargetInfo("", "", "", DateTime.UtcNow)
            };
            foreach (var (localPath, formula) in _formulaBridge.LoadFormulas())
                seeded.CellFormulas[localPath] = new FormulaEntry(formula);
            if (seeded.CellFormulas.Count > 0)
                FormulaSession.AdoptSidecar(seeded);
            FormulaSession.Recalculated += (_, _) => FormulaVisualsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // -----------------------------------------------------------------------
    //  Formulas (delegates to FormulaSession; no-ops when formulas are disabled)
    // -----------------------------------------------------------------------

    public bool IsFormulaCell(int rowIndex, string columnKey) =>
        FormulaSession?.IsFormulaCell(rowIndex, columnKey) ?? false;

    public string? GetDisplayFormula(int rowIndex, string columnKey) =>
        FormulaSession?.GetDisplayFormula(rowIndex, columnKey);

    public CellState? GetFormulaErrorState(int rowIndex, string columnKey) =>
        FormulaSession?.GetErrorState(rowIndex, columnKey);

    /// <summary>What the cell editor should preload: the formula text if the cell is a formula,
    /// otherwise the stored value.</summary>
    public string GetEditableCellText(int rowIndex, string columnKey)
    {
        if (GetDisplayFormula(rowIndex, columnKey) is { } f) return f;
        return rowIndex >= 0 && rowIndex < Rows.Count ? Rows[rowIndex][columnKey] : "";
    }

    /// <summary>Commits what was typed into a cell editor: a leading '=' becomes a formula, anything
    /// else a plain value (dropping any formula the cell had). Mirrors WorkspaceViewModel.CommitCellEdit,
    /// trimmed to what this dialog needs.</summary>
    public void CommitCellText(DynamicDataRow row, string propName, string oldValue, string typed, DataGrid? grid)
    {
        var idx = Rows.IndexOf(row);
        if (idx < 0) return;

        if (FormulaSession != null && typed.StartsWith('='))
        {
            // Left unchanged (the editor just showed the existing formula text): restore the
            // computed value the live-write clobbered and stop.
            if (typed == GetEditableCellText(idx, propName)) { row[propName] = oldValue; return; }
            // The formula text is not the row's data — undo the editor's live write first.
            row[propName] = oldValue;
            var changeSet = FormulaSession.TrySetCellFormula(idx, propName, typed, out var error);
            if (changeSet is null)
            {
                _notificationService.Publish(NotificationType.Warning, error ?? Localizer.Get("FormulaInvalidMsg"));
                return;
            }
            UndoRedo.Push(new SetCellFormulaAction(FormulaSession, changeSet, grid));
            FormulaVisualsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (FormulaSession != null && FormulaSession.IsFormulaCell(idx, propName))
        {
            var removed = FormulaSession.TryRemoveCellFormula(idx, propName);
            if (removed != null) UndoRedo.Push(new SetCellFormulaAction(FormulaSession, removed, grid));
            oldValue = row[propName];
            FormulaVisualsChanged?.Invoke(this, EventArgs.Empty);
        }

        if (row[propName] != typed) row[propName] = typed;
        if (oldValue == typed) return;
        UndoRedo.Push(new EditCellAction(row, propName, oldValue, typed, grid));
    }

    private void SeedPrimitiveArray(string json)
    {
        IsPrimitiveArrayMode = true;

        var elementType = JsonFieldType.String;
        var values = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (values.Count == 0) elementType = InferElementType(el);
                    values.Add(el.ValueKind switch
                    {
                        JsonValueKind.Null => "",
                        JsonValueKind.Object or JsonValueKind.Array => el.GetRawText(),
                        _ => el.ToString() ?? ""
                    });
                }
            }
        }
        catch
        {
            // Empty or unparseable cell — start from a single empty string column.
        }

        Properties.Add(new JsonPropertyDefinition { Name = PrimitiveArrayColumn, FieldType = elementType });
        foreach (var value in values)
        {
            var row = new DynamicDataRow();
            row.InitializeProperty(PrimitiveArrayColumn, value);
            Rows.Add(row);
        }
    }

    private static JsonFieldType InferElementType(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => JsonFieldType.Bool,
        JsonValueKind.Number => el.TryGetInt32(out _) ? JsonFieldType.Int : JsonFieldType.Double,
        JsonValueKind.Object => JsonFieldType.Object,
        JsonValueKind.Array => JsonFieldType.Array,
        _ => JsonFieldType.String
    };

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
        var savedValues = Rows.ToDictionary(r => r, r => (r[prop.Name], r.GetKind(prop.Name)));

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
            newRow.InitializeProperty(prop.Name, row[prop.Name], row.GetKind(prop.Name));
        return newRow;
    }

    [RelayCommand]
    private void Submit()
    {
        try
        {
            // Persist formulas defined in this dialog back into the document's sidecar BEFORE
            // serializing, so the JSON we hand back carries their freshly computed values.
            if (FormulaSession != null && _formulaBridge != null)
            {
                var formulas = FormulaSession.Sidecar.CellFormulas
                    .ToDictionary(kv => kv.Key, kv => kv.Value.Formula);
                _formulaBridge.SaveFormulas(formulas);
            }

            var json = _jsonService.SerializeToJson(Rows, Properties);

            if (IsObjectMode)
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    json = doc.RootElement[0].GetRawText();
                }
                else
                {
                    json = "{}";
                }
            }
            else if (IsPrimitiveArrayMode)
            {
                // Rows are [{"value": 1}, ...] — unwrap them back into a bare array.
                var unwrapped = new JsonArray();
                if (JsonNode.Parse(json) is JsonArray rows)
                {
                    foreach (var item in rows)
                        unwrapped.Add(item?[PrimitiveArrayColumn]?.DeepClone());
                }

                json = unwrapped.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
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

        // Formulas keep working all the way down: the sub-dialog writes into THIS dialog's session
        // at paths inside the cell, and those travel up to the document's sidecar when this dialog
        // is submitted (see Submit / NestedFormulaBridge).
        var editIdx = Rows.IndexOf(row);
        var bridge = FormulaSession != null && editIdx >= 0
            ? new NestedFormulaBridge(FormulaSession, editIdx, propertyName, type == JsonFieldType.Array)
            : null;

        var newValue = await _dialogService.ShowJsonEditorDialogAsync(
            Localizer.Get("EditCellTitle", propertyName), currentValue, type, _jsonService,
            merged.Count > 0 ? merged : null, bridge);

        if (newValue == null || newValue == currentValue)
        {
            if (FormulaSession?.HasAnyFormulas == true) FormulaVisualsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        {
            var newRow = new DynamicDataRow();
            foreach (var kvp in row.GetAllValues())
            {
                newRow.InitializeProperty(kvp.Key, kvp.Value, row.GetKind(kvp.Key));
            }

            newRow[propertyName] = newValue;
            var idx = Rows.IndexOf(row);
            if (idx >= 0)
            {
                Rows[idx] = newRow;
                UndoRedo.Push(new EditJsonCellAction(Rows, row, newRow, propertyName));
            }

            if (FormulaSession?.HasAnyFormulas == true)
            {
                FormulaSession.RecalculateAll();
                FormulaVisualsChanged?.Invoke(this, EventArgs.Empty);
            }

            _notificationService.Publish(NotificationType.Success, Localizer.Get("CellUpdatedMsg", propertyName));
        }
    }
}
