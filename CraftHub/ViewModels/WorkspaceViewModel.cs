using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.Services.Actions;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CraftHub.ViewModels;

public partial class WorkspaceViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IJsonService _jsonService;
    private readonly IClassParserService _classParserService;
    private readonly IDialogService _dialogService;
    private readonly NotificationService _notificationService;

    [ObservableProperty] private string _header = "Tab";
    [ObservableProperty] private string _propertyName = string.Empty;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private DynamicDataRow? _selectedRow;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _selectedRowsCount = 0;
    [ObservableProperty] private bool _isJsonEditorMode = false;
    [ObservableProperty] private string _rawJsonText = string.Empty;
    [ObservableProperty] private string _jsonEditorError;
    [ObservableProperty] private bool _isJsonEditorErrorVisible;
    [ObservableProperty] private bool _hasClipboardContent;

    /// <summary>
    /// True while a DataGrid cell editor (TextBox) is active.
    /// Disables the row-level clipboard commands so Ctrl+C/V/X fall through to the TextBox.
    /// </summary>
    [ObservableProperty] private bool _isCellEditing;

    public bool IsTableEditorMode => !IsJsonEditorMode;

    partial void OnIsJsonEditorModeChanged(bool value) => OnPropertyChanged(nameof(IsTableEditorMode));

    partial void OnSelectedRowsCountChanged(int value)
    {
        CopyRowsToJsonCommand.NotifyCanExecuteChanged();
        CopyRowsToJsonAsObjectsCommand.NotifyCanExecuteChanged();
        CutRowsToDataGridCommand.NotifyCanExecuteChanged();
        DuplicateRowsCommand.NotifyCanExecuteChanged();
        DuplicateAfterRowsCommand.NotifyCanExecuteChanged();
        RemoveRowsCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasClipboardContentChanged(bool value)
        => PasteRowsToDataGridCommand.NotifyCanExecuteChanged();

    partial void OnIsCellEditingChanged(bool value)
    {
        // Clipboard commands must yield to the cell TextBox while editing.
        CopyRowsToJsonCommand.NotifyCanExecuteChanged();
        CopyRowsToJsonAsObjectsCommand.NotifyCanExecuteChanged();
        CutRowsToDataGridCommand.NotifyCanExecuteChanged();
        PasteRowsToDataGridCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Called from the View when the context menu opens to refresh clipboard state.</summary>
    internal async Task RefreshClipboardStateAsync()
    {
        var text = await _dialogService.GetFromClipboardAsync();
        HasClipboardContent = !string.IsNullOrWhiteSpace(text);
    }

    private bool HasSelection(object? _) => SelectedRowsCount > 0;

    // Row-level clipboard commands must not fire while a cell TextBox is active,
    // so that Ctrl+C/V/X fall through to the editor's built-in handling.
    private bool CanCopyOrCut(object? _) => SelectedRowsCount > 0 && !IsCellEditing;
    private bool CanPaste() => HasClipboardContent && !IsCellEditing;

    public ObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public ObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));
    public event EventHandler? CloseRequested;
    public event EventHandler? ColumnsChanged;

    /// <summary>
    /// Set by MainWindowViewModel. Called when ImportJsonAsync needs a fresh workspace
    /// for an additional file. Returns the new WorkspaceViewModel, or null if the tab
    /// limit (15) has been reached.
    /// </summary>
    public Func<WorkspaceViewModel?>? RequestNewWorkspace { get; set; }

    public int TotalRows => Rows.Count;

    private string _dataSizeKb = "0.0 KB";
    public string DataSizeKb => _dataSizeKb;

    // -----------------------------------------------------------------------
    //  Undo / Redo
    // -----------------------------------------------------------------------

    public UndoRedoService UndoRedo { get; } = new();

    private bool CanUndo => UndoRedo.CanUndo;
    private bool CanRedo => UndoRedo.CanRedo;

    /// <summary>Dynamic tooltip: "Undo: Add row" or just "Undo" when stack is empty.</summary>
    public string UndoTooltip => UndoRedo.UndoDescription is { } d
        ? $"{Localizer.Get("UndoTip")}: {d}"
        : Localizer.Get("UndoTip");

    /// <summary>Dynamic tooltip: "Redo: Add row" or just "Redo" when stack is empty.</summary>
    public string RedoTooltip => UndoRedo.RedoDescription is { } d
        ? $"{Localizer.Get("RedoTip")}: {d}"
        : Localizer.Get("RedoTip");

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private void NotifySuccess(string message) => _notificationService.Publish(NotificationType.Success, message);
    private void NotifyWarning(string message) => _notificationService.Publish(NotificationType.Warning, message);
    private void NotifyError(string message) => _notificationService.Publish(NotificationType.Error, message);

    private void FireColumnsChanged() => ColumnsChanged?.Invoke(this, EventArgs.Empty);

    // -----------------------------------------------------------------------
    //  Constructor
    // -----------------------------------------------------------------------

    public WorkspaceViewModel(
        IFileDialogService fileDialogService,
        IJsonService jsonService,
        IClassParserService classParserService,
        IDialogService dialogService,
        NotificationService notificationService)
    {
        _fileDialogService = fileDialogService;
        _jsonService = jsonService;
        _classParserService = classParserService;
        _dialogService = dialogService;
        _notificationService = notificationService;

        Rows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalRows));
            UpdateDataSize();
        };
        Properties.CollectionChanged += (_, _) => UpdateDataSize();

        // Keep Undo/Redo buttons and tooltips in sync
        UndoRedo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UndoRedoService.CanUndo) or null)
            {
                UndoCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(UndoTooltip));
            }
            if (e.PropertyName is nameof(UndoRedoService.CanRedo) or null)
            {
                RedoCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RedoTooltip));
            }
        };
    }

    // -----------------------------------------------------------------------
    //  Data size
    // -----------------------------------------------------------------------

    private void UpdateDataSize()
    {
        try
        {
            if (Rows.Count == 0 || Properties.Count == 0)
            {
                _dataSizeKb = "0.0 KB";
            }
            else
            {
                var json = _jsonService.SerializeToJson(Rows, Properties);
                _dataSizeKb = $"{Encoding.UTF8.GetByteCount(json) / 1024.0:F1} KB";
            }
        }
        catch
        {
            _dataSizeKb = "? KB";
        }
        OnPropertyChanged(nameof(DataSizeKb));
    }

    // -----------------------------------------------------------------------
    //  Schema (property) commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddProperty()
    {
        if (string.IsNullOrWhiteSpace(PropertyName))
        {
            NotifyWarning(Localizer.Get("EnterPropertyName"));
            return;
        }

        if (Properties.Any(p => p.Name == PropertyName))
        {
            NotifyWarning(Localizer.Get("PropertyAlreadyExists"));
            return;
        }

        var prop = new JsonPropertyDefinition
        {
            Name = PropertyName,
            FieldType = SelectedType,
        };

        Properties.Add(prop);
        foreach (var row in Rows)
            row.InitializeProperty(prop.Name);

        UndoRedo.Push(new AddPropertyAction(Properties, Rows, prop, FireColumnsChanged));

        PropertyName = string.Empty;
        NotifySuccess(Localizer.Get("PropertyAdded", prop.Name));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task RemovePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;

        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemovePropertyTitle"),
            Localizer.Get("RemovePropertyMsg", prop.Name));
        if (!confirmed) return;

        // Capture state before removal for undo
        var propIndex = Properties.IndexOf(prop);
        var savedValues = Rows.ToDictionary(r => r, r => r[prop.Name]);

        Properties.Remove(prop);
        foreach (var row in Rows)
            row.RemoveProperty(prop.Name);

        UndoRedo.Push(new RemovePropertyAction(Properties, Rows, prop, propIndex, savedValues, FireColumnsChanged));

        NotifySuccess(Localizer.Get("PropertyRemoved", prop.Name));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task RenamePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;

        var newName = await _dialogService.ShowInputDialogAsync(
            Localizer.Get("RenamePropertyTitle"),
            Localizer.Get("RenamePropertyPrompt"),
            prop.Name,
            Localizer.Get("PropertyNameWatermark"));

        if (newName == null) return;

        newName = newName.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            NotifyWarning(Localizer.Get("EnterPropertyName"));
            return;
        }

        if (newName == prop.Name) return;

        if (Properties.Any(p => p.Name == newName))
        {
            NotifyWarning(Localizer.Get("PropertyAlreadyExists"));
            return;
        }

        var oldName = prop.Name;

        prop.Name = newName;
        foreach (var row in Rows)
            row.RenameProperty(oldName, newName);

        UndoRedo.Push(new RenamePropertyAction(prop, oldName, newName, Rows, FireColumnsChanged));

        NotifySuccess(Localizer.Get("PropertyRenamed", oldName, newName));
        FireColumnsChanged();
    }

    // -----------------------------------------------------------------------
    //  Row commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddRow()
    {
        var row = new DynamicDataRow();
        foreach (var prop in Properties)
            row.InitializeProperty(prop.Name);

        Rows.Add(row);
        UndoRedo.Push(new AddRowAction(Rows, row));
        NotifySuccess(Localizer.Get("RowAdded", Rows.Count));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateRows(object? parameter)
    {
        var source = ResolveSelectedRows(parameter);
        if (source == null || source.Count == 0) return;

        var duplicated = source.Select(CreateDuplicateRow).ToList();

        // Append to the end.
        foreach (var row in duplicated)
            Rows.Add(row);

        UndoRedo.Push(new DuplicateRowsAction(Rows, duplicated));
        NotifySuccess(Localizer.Get("RowsDuplicatedMsg", source.Count));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateAfterRows(object? parameter)
    {
        var source = ResolveSelectedRows(parameter);
        if (source == null || source.Count == 0) return;

        // Insert point = one position after the last selected row.
        var insertAfter = source.OrderByDescending(r => Rows.IndexOf(r)).First();
        int insertIdx = Rows.IndexOf(insertAfter) + 1;

        var duplicated = source
            .OrderBy(r => Rows.IndexOf(r))   // preserve original order
            .Select(CreateDuplicateRow)
            .ToList();

        for (int i = 0; i < duplicated.Count; i++)
            Rows.Insert(insertIdx + i, duplicated[i]);

        UndoRedo.Push(new DuplicateRowsAction(Rows, duplicated, insertAfter));
        NotifySuccess(Localizer.Get("RowsDuplicatedMsg", source.Count));
    }

    /// <summary>Creates a copy of <paramref name="row"/> without adding it to <see cref="Rows"/>.</summary>
    private DynamicDataRow CreateDuplicateRow(DynamicDataRow row)
    {
        var newRow = new DynamicDataRow();
        foreach (var prop in Properties)
            newRow.InitializeProperty(prop.Name, row[prop.Name]);
        return newRow;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveRowsAsync(object? parameter)
    {
        if (parameter is not IList items || items.Count == 0)
        {
            if (SelectedRow != null)
                await RemoveSingleRowAsync(SelectedRow);
            return;
        }

        var toRemove = items.Cast<DynamicDataRow>().ToList();
        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemoveRowsTitle"),
            Localizer.Get("RemoveRowsMsg", toRemove.Count));
        if (!confirmed) return;

        // Capture indices BEFORE removal so undo can restore positions
        var withIndices = toRemove
            .Select(r => (Index: Rows.IndexOf(r), Row: r))
            .Where(x => x.Index >= 0)
            .ToList();

        foreach (var item in withIndices)
            Rows.Remove(item.Row);

        UndoRedo.Push(new RemoveRowsAction(Rows, withIndices.Select(x => (x.Index, x.Row))));
        NotifySuccess(Localizer.Get("RowsRemovedMsg", withIndices.Count));
    }

    private async Task RemoveSingleRowAsync(DynamicDataRow row)
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("RemoveRowTitle"),
            Localizer.Get("RemoveRowMsg"));
        if (!confirmed) return;

        var idx = Rows.IndexOf(row);
        Rows.Remove(row);
        UndoRedo.Push(new RemoveRowsAction(Rows, new[] { (idx, row) }));
        NotifySuccess(Localizer.Get("RowRemovedMsg"));
    }

    // -----------------------------------------------------------------------
    //  Cell edit (complex types — called from code-behind)
    // -----------------------------------------------------------------------

    public async Task EditJsonCellAsync(DynamicDataRow row, string propertyName, JsonFieldType type)
    {
        var currentValue = row[propertyName];

        // Build merged schema from all rows in this column so every cell shares the same fields.
        var merged = new List<JsonPropertyDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in Rows)
        {
            var val = r[propertyName];
            if (string.IsNullOrWhiteSpace(val)) continue;
            try
            {
                foreach (var f in _jsonService.DetectFields(val))
                {
                    if (seen.Add(f.FieldName))
                        merged.Add(new JsonPropertyDefinition { Name = f.FieldName, FieldType = f.SelectedType });
                }
            }
            catch { }
        }

        var newValue = await _dialogService.ShowJsonEditorDialogAsync($"Edit {propertyName}", currentValue, type, _jsonService,
            merged.Count > 0 ? merged : null);

        if (newValue == null || newValue == currentValue) return;

        var newRow = new DynamicDataRow();
        foreach (var kvp in row.GetAllValues())
            newRow.InitializeProperty(kvp.Key, kvp.Value);
        newRow[propertyName] = newValue;

        var idx = Rows.IndexOf(row);
        if (idx < 0) return;

        Rows[idx] = newRow;
        UndoRedo.Push(new EditJsonCellAction(Rows, row, newRow, propertyName));
        NotifySuccess(Localizer.Get("UpdatedCellMsg", propertyName));
    }

    // -----------------------------------------------------------------------
    //  Copy commands (read-only, no undo needed)
    // -----------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task CopyRowsToJsonAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : _jsonService.SerializeToJson(selectedRows, Properties);

        await _dialogService.CopyToClipboardAsync(json);
        HasClipboardContent = true;   // enable Paste immediately, no context-menu refresh needed
        NotifySuccess(Localizer.Get("RowsCopiedMsg", selectedRows.Count));
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private async Task PasteRowsToDataGridAsync(object? parameter)
    {
        var json = await _dialogService.GetFromClipboardAsync();
        if (string.IsNullOrWhiteSpace(json)) return;
        var pasteData = _jsonService.ParseJsonData(json, Properties);
        foreach (var row in pasteData)
        {
            Rows.Add(row);
        }
        if (pasteData == null || pasteData.Count <= 0)
        {
            NotifyError(Localizer.Get("RowsPasteErrorMsg", pasteData.Count));
        }
        else
        {
            UndoRedo.Push(new PasteRowsAction(Rows, pasteData));
            NotifySuccess(Localizer.Get("RowsPasteMsg", pasteData.Count));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task CutRowsToDataGridAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var withIndices = selectedRows
            .Select(r => (Index: Rows.IndexOf(r), Row: r))
            .Where(x => x.Index >= 0)
            .ToList();

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : _jsonService.SerializeToJson(selectedRows, Properties);

        await _dialogService.CopyToClipboardAsync(json);
        HasClipboardContent = true;   // enable Paste immediately

        foreach (var row in selectedRows)
            Rows.Remove(row);

        UndoRedo.Push(new RemoveRowsAction(Rows, withIndices.Select(x => (x.Index, x.Row))));

        NotifySuccess(Localizer.Get("RowsCutMsg", selectedRows.Count));
    }

    [RelayCommand(CanExecute = nameof(CanCopyOrCut))]
    private async Task CopyRowsToJsonAsObjectsAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : string.Join(", ", selectedRows.Select(r => _jsonService.SerializeSingleRowToJson(r, Properties)));

        await _dialogService.CopyToClipboardAsync(json);
        NotifySuccess(Localizer.Get("RowsCopiedMsg", selectedRows.Count));
    }

    private List<DynamicDataRow>? ResolveSelectedRows(object? parameter)
    {
        if (parameter is IList { Count: > 0 } list)
            return list.Cast<DynamicDataRow>().ToList();
        if (SelectedRow != null)
            return new List<DynamicDataRow> { SelectedRow };
        return null;
    }

    // -----------------------------------------------------------------------
    //  Import / Export  (imports clear undo history — state changes completely)
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        var filters = new List<FileFilter> { new("JSON files", new[] { "*.json" }) };
        var paths = await _fileDialogService.OpenMultipleFilesAsync(Localizer.Get("ImportJson"), filters);
        if (paths.Count == 0) return;

        // First file goes into the current workspace (reuse it if empty, otherwise it will
        // overwrite — consistent with the previous single-file behaviour).
        await ImportFromPathAsync(paths[0]);

        // Remaining files each get their own new workspace tab.
        for (int i = 1; i < paths.Count; i++)
        {
            var newVm = RequestNewWorkspace?.Invoke();
            if (newVm == null)
            {
                // Tab limit reached — notify and stop.
                NotifyError(Localizer.Get("TabLimitReachedMsg", 15));
                break;
            }
            await newVm.ImportFromPathAsync(paths[i]);
        }
    }

    /// <summary>
    /// Imports a single JSON file into this workspace.
    /// Shows the field mapping dialog if the schema is not yet defined.
    /// Returns false if the user cancelled the mapping dialog.
    /// </summary>
    public async Task<bool> ImportFromPathAsync(string path)
    {
        if (Properties.Count > 0 || Rows.Count > 0)
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                Localizer.Get("ImportOverwriteTitle"),
                Localizer.Get("ImportOverwriteMsg"));
            if (!confirmed) return false;
        }

        var json = await File.ReadAllTextAsync(path);

        if (Properties.Count == 0)
        {
            var detectedFields = _jsonService.DetectFields(json);
            if (detectedFields.Count == 0)
            {
                await _dialogService.ShowMessageAsync(Localizer.Get("ImportTitle"), Localizer.Get("NoFieldsDetectedMsg"));
                return false;
            }

            // Loop until the user either cancels or picks compatible types for every field.
            // JsonFieldMapping items are shared references so SelectedType changes made inside
            // the dialog are preserved when we reopen it after showing an error.
            List<JsonFieldMapping>? mappedFields;
            while (true)
            {
                mappedFields = await _dialogService.ShowFieldMappingDialogAsync(detectedFields, Path.GetFileName(path));
                if (mappedFields == null) return false;

                // Validate that Array/Object fields actually contain valid JSON of the correct kind.
                // Numbers, booleans and plain strings are valid JSON but cannot be opened in the
                // nested editor, which expects '[' or '{' as the first character.
                var typeErrors = new List<string>();
                foreach (var field in mappedFields)
                {
                    if (field.SelectedType is JsonFieldType.Object or JsonFieldType.Array
                        && !string.IsNullOrEmpty(field.SampleValue))
                    {
                        var typeName = field.SelectedType == JsonFieldType.Array ? "Array" : "Object";
                        var expectedKind = field.SelectedType == JsonFieldType.Array
                            ? JsonValueKind.Array
                            : JsonValueKind.Object;
                        try
                        {
                            using var doc = JsonDocument.Parse(field.SampleValue);
                            if (doc.RootElement.ValueKind != expectedKind)
                                typeErrors.Add($"  • '{field.FieldName}': \"{field.SampleValue}\" → не {typeName}");
                        }
                        catch (JsonException)
                        {
                            typeErrors.Add($"  • '{field.FieldName}': \"{field.SampleValue}\" → не {typeName}");
                        }
                    }
                }

                if (typeErrors.Count == 0) break;   // all good, proceed

                // Show error and reopen the dialog so the user can fix the types.
                var msg = Localizer.Get("ImportTypeMismatchMsg") + "\n\n" + string.Join("\n", typeErrors);
                await _dialogService.ShowMessageAsync(Localizer.Get("ImportTitle"), msg);
            }

            foreach (var field in mappedFields)
                Properties.Add(new JsonPropertyDefinition { Name = field.FieldName, FieldType = field.SelectedType });
        }

        var rows = _jsonService.ParseJsonData(json, Properties);
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);

        Header = Path.GetFileNameWithoutExtension(path);
        UndoRedo.Clear();   // destructive — clear history
        NotifySuccess(Localizer.Get("ImportedMsg", Rows.Count, Properties.Count));
        FireColumnsChanged();

        if (IsJsonEditorMode)
            RawJsonText = _jsonService.SerializeToJson(Rows, Properties);

        return true;
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(Localizer.Get("ExportTitle"), Localizer.Get("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter> { new("JSON files", new[] { "*.json" }) };
        var path = await _fileDialogService.SaveFileAsync("Export JSON", filters, Header);
        if (path == null) return;

        var json = _jsonService.SerializeToJson(Rows, Properties);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        NotifySuccess(Localizer.Get("ExportedMsg", Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task ImportClassAsync()
    {
        var filters = new List<FileFilter> { new("C# files", new[] { "*.cs" }) };
        var paths = await _fileDialogService.OpenMultipleFilesAsync("Import C# Class", filters);
        if (paths.Count == 0) return;

        await ImportClassFromPathAsync(paths[0]);

        for (int i = 1; i < paths.Count; i++)
        {
            var newVm = RequestNewWorkspace?.Invoke();
            if (newVm == null)
            {
                NotifyError(Localizer.Get("TabLimitReachedMsg", 15));
                break;
            }
            await newVm.ImportClassFromPathAsync(paths[i]);
        }
    }

    /// <summary>
    /// Импортирует один C# файл в этот workspace.
    /// Возвращает false, если импорт был отменён или не найден ни один класс.
    /// </summary>
    public async Task<bool> ImportClassFromPathAsync(string path)
    {
        if (Properties.Count > 0 || Rows.Count > 0)
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                Localizer.Get("ImportOverwriteTitle"),
                Localizer.Get("ImportOverwriteMsg"));
            if (!confirmed) return false;
        }

        var code = await File.ReadAllTextAsync(path);
        var allClasses = _classParserService.ParseAllClasses(code);
        var fileName = Path.GetFileName(path);

        if (allClasses.Count == 0)
        {
            await _dialogService.ShowMessageAsync(
                Localizer.Get("ImportTitle"),
                Localizer.Get("NoClassesFoundMsg"));
            return false;
        }

        string className;
        List<JsonPropertyDefinition> parsedProps;

        if (allClasses.Count == 1)
        {
            (className, parsedProps) = allClasses[0];
        }
        else
        {
            var classNames = allClasses.ConvertAll(c => c.className);
            var selected = await _dialogService.ShowSelectDialogAsync(
                Localizer.Get("SelectClassTitle"),
                Localizer.Get("SelectClassMsg"),
                fileName,
                classNames);
            if (selected == null) return false;
            (className, parsedProps) = allClasses.Find(c => c.className == selected);
        }

        if (parsedProps.Count == 0)
        {
            await _dialogService.ShowMessageAsync(
                Localizer.Get("ImportTitle"),
                Localizer.Get("NoPropsFoundMsg"));
            return false;
        }

        Properties.Clear();
        foreach (var prop in parsedProps)
            Properties.Add(prop);

        Rows.Clear();
        Header = className;
        UndoRedo.Clear();   // destructive — clear history
        NotifySuccess(Localizer.Get("ImportedClassMsg", className, Properties.Count));
        FireColumnsChanged();

        return true;
    }

    [RelayCommand]
    private async Task ExportClassAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(Localizer.Get("ExportTitle"), Localizer.Get("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter> { new("C# files", new[] { "*.cs" }) };
        var path = await _fileDialogService.SaveFileAsync("Export C# Class", filters, Header);
        if (path == null) return;

        var className = Path.GetFileNameWithoutExtension(path);
        var code = _classParserService.GenerateClassCode(className, Properties);
        await File.WriteAllTextAsync(path, code, Encoding.UTF8);
        Header = className;
        NotifySuccess(Localizer.Get("ExportedClassMsg", className));
    }

    // -----------------------------------------------------------------------
    //  JSON editor mode toggle
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void SwitchToJsonEditor()
    {
        RawJsonText = Rows.Count > 0 && Properties.Count > 0
            ? _jsonService.SerializeToJson(Rows, Properties)
            : Properties.Count > 0 ? "[]" : "{}";
        JsonEditorError = string.Empty;
        IsJsonEditorErrorVisible = false;
        IsJsonEditorMode = true;
    }

    [RelayCommand]
    private void SwitchToTableEditor()
    {
        if (string.IsNullOrWhiteSpace(RawJsonText))
        {
            IsJsonEditorMode = false;
            return;
        }

        try
        {
            JsonDocument.Parse(RawJsonText);

            // Always detect fields from JSON and add any that are not yet in the schema.
            // This covers both the "empty schema" case and the "user added new fields in JSON mode" case.
            var detected = _jsonService.DetectFields(RawJsonText);
            var existingNames = Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var field in detected)
            {
                if (!existingNames.Contains(field.FieldName))
                    Properties.Add(new JsonPropertyDefinition { Name = field.FieldName, FieldType = field.SelectedType });
            }

            var rows = _jsonService.ParseJsonData(RawJsonText, Properties);
            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(row);

            UndoRedo.Clear();
            JsonEditorError = string.Empty;
            IsJsonEditorErrorVisible = false;
            IsJsonEditorMode = false;
            FireColumnsChanged();
            NotifySuccess(Localizer.Get("JsonAppliedMsg"));
        }
        catch (JsonException ex)
        {
            IsJsonEditorErrorVisible = true;
            JsonEditorError = $"{Localizer.Get("InvalidJsonError")}: {ex.Message}";
        }
    }

    // -----------------------------------------------------------------------
    //  Other commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task Close()
    {
        var mainWindowViewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        if(mainWindowViewModel.Workspaces.Count == 1)
        {
            await _dialogService.ShowMessageAsync(Localizer.Get("CloseWorkspaceErrorTitle"), Localizer.Get("CloseWorkspaceErrorMsg"));
            return;
        }
        var result = await _dialogService.ShowConfirmAsync(Localizer.Get("CloseWorkspaceTitle"), Localizer.Get("CloseWorkspaceMsg"));
        if (result)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        var newName = await _dialogService.ShowInputDialogAsync(
            Localizer.Get("RenameWorkspaceTitle"), Localizer.Get("RenameWorkspacePrompt"), Header, Localizer.Get("WorkspaceNameLabel"));

        if (newName == null) return;

        if (string.IsNullOrWhiteSpace(newName))
        {
            NotifyWarning(Localizer.Get("WorkspaceNameEmpty"));
            return;
        }

        var trimmed = newName.Trim();
        if (trimmed == Header) return;

        Header = trimmed;
        NotifySuccess(Localizer.Get("WorkspaceRenamedMsg", Header));
    }
}
