using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.Services.Actions;

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

    public ObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public ObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));
    public event EventHandler? CloseRequested;
    public event EventHandler? ColumnsChanged;

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
        ? $"{L("UndoTip")}: {d}"
        : L("UndoTip");

    /// <summary>Dynamic tooltip: "Redo: Add row" or just "Redo" when stack is empty.</summary>
    public string RedoTooltip => UndoRedo.RedoDescription is { } d
        ? $"{L("RedoTip")}: {d}"
        : L("RedoTip");

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => UndoRedo.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => UndoRedo.Redo();

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private void NotifySuccess(string message) => _notificationService.Publish(NotificationType.Success, message);
    private void NotifyWarning(string message) => _notificationService.Publish(NotificationType.Warning, message);
    private static string L(string key) => LanguageService.Instance.Get(key);
    private static string L(string key, params object[] args) => LanguageService.Instance.Get(key, args);

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
            NotifyWarning(L("EnterPropertyName"));
            return;
        }

        if (Properties.Any(p => p.Name == PropertyName))
        {
            NotifyWarning(L("PropertyAlreadyExists"));
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
        NotifySuccess(L("PropertyAdded", prop.Name));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task RemovePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;

        var confirmed = await _dialogService.ShowConfirmAsync(
            L("RemovePropertyTitle"),
            L("RemovePropertyMsg", prop.Name));
        if (!confirmed) return;

        // Capture state before removal for undo
        var propIndex = Properties.IndexOf(prop);
        var savedValues = Rows.ToDictionary(r => r, r => r[prop.Name]);

        Properties.Remove(prop);
        foreach (var row in Rows)
            row.RemoveProperty(prop.Name);

        UndoRedo.Push(new RemovePropertyAction(Properties, Rows, prop, propIndex, savedValues, FireColumnsChanged));

        NotifySuccess(L("PropertyRemoved", prop.Name));
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
        NotifySuccess(L("RowAdded", Rows.Count));
    }

    [RelayCommand]
    private void DuplicateRows(object? parameter)
    {
        List<DynamicDataRow> source;

        if (parameter is not IList items || items.Count == 0)
        {
            if (SelectedRow == null) return;
            source = new List<DynamicDataRow> { SelectedRow };
        }
        else
        {
            source = items.Cast<DynamicDataRow>().ToList();
        }

        var duplicated = new List<DynamicDataRow>();
        foreach (var row in source)
            duplicated.Add(DuplicateSingleRow(row));

        UndoRedo.Push(new DuplicateRowsAction(Rows, duplicated));
        NotifySuccess(L("RowsDuplicatedMsg", source.Count));
    }

    private DynamicDataRow DuplicateSingleRow(DynamicDataRow row)
    {
        var newRow = new DynamicDataRow();
        foreach (var prop in Properties)
            newRow.InitializeProperty(prop.Name, row[prop.Name]);
        Rows.Add(newRow);
        return newRow;
    }

    [RelayCommand]
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
            L("RemoveRowsTitle"),
            L("RemoveRowsMsg", toRemove.Count));
        if (!confirmed) return;

        // Capture indices BEFORE removal so undo can restore positions
        var withIndices = toRemove
            .Select(r => (Index: Rows.IndexOf(r), Row: r))
            .Where(x => x.Index >= 0)
            .ToList();

        foreach (var item in withIndices)
            Rows.Remove(item.Row);

        UndoRedo.Push(new RemoveRowsAction(Rows, withIndices.Select(x => (x.Index, x.Row))));
        NotifySuccess(L("RowsRemovedMsg", withIndices.Count));
    }

    private async Task RemoveSingleRowAsync(DynamicDataRow row)
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            L("RemoveRowTitle"),
            L("RemoveRowMsg"));
        if (!confirmed) return;

        var idx = Rows.IndexOf(row);
        Rows.Remove(row);
        UndoRedo.Push(new RemoveRowsAction(Rows, new[] { (idx, row) }));
        NotifySuccess(L("RowRemovedMsg"));
    }

    // -----------------------------------------------------------------------
    //  Cell edit (complex types — called from code-behind)
    // -----------------------------------------------------------------------

    public async Task EditJsonCellAsync(DynamicDataRow row, string propertyName, JsonFieldType type)
    {
        var currentValue = row[propertyName];
        var newValue = await _dialogService.ShowJsonEditorDialogAsync(
            $"Edit {propertyName}", currentValue, type, _jsonService);

        if (newValue == null || newValue == currentValue) return;

        var newRow = new DynamicDataRow();
        foreach (var kvp in row.GetAllValues())
            newRow.InitializeProperty(kvp.Key, kvp.Value);
        newRow[propertyName] = newValue;

        var idx = Rows.IndexOf(row);
        if (idx < 0) return;

        Rows[idx] = newRow;
        UndoRedo.Push(new EditJsonCellAction(Rows, row, newRow, propertyName));
        NotifySuccess(L("UpdatedCellMsg", propertyName));
    }

    // -----------------------------------------------------------------------
    //  Copy commands (read-only, no undo needed)
    // -----------------------------------------------------------------------

    [RelayCommand]
    private async Task CopyRowsToJsonAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : _jsonService.SerializeToJson(selectedRows, Properties);

        await _dialogService.CopyToClipboardAsync(json);
        NotifySuccess(L("RowsCopiedMsg", selectedRows.Count));
    }

    [RelayCommand]
    private async Task CopyRowsToJsonAsObjectsAsync(object? parameter)
    {
        var selectedRows = ResolveSelectedRows(parameter);
        if (selectedRows == null) return;

        var json = selectedRows.Count == 1
            ? _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties)
            : string.Join(", ", selectedRows.Select(r => _jsonService.SerializeSingleRowToJson(r, Properties)));

        await _dialogService.CopyToClipboardAsync(json);
        NotifySuccess(L("RowsCopiedMsg", selectedRows.Count));
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
        var path = await _fileDialogService.OpenFileAsync("Import JSON", filters);
        if (path == null) return;

        var json = await File.ReadAllTextAsync(path);

        if (Properties.Count == 0)
        {
            var detectedFields = _jsonService.DetectFields(json);
            if (detectedFields.Count == 0)
            {
                await _dialogService.ShowMessageAsync(L("ImportTitle"), L("NoFieldsDetectedMsg"));
                return;
            }

            var mappedFields = await _dialogService.ShowFieldMappingDialogAsync(detectedFields);
            if (mappedFields == null) return;

            foreach (var field in mappedFields)
                Properties.Add(new JsonPropertyDefinition { Name = field.FieldName, FieldType = field.SelectedType });
        }

        var rows = _jsonService.ParseJsonData(json, Properties);
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);

        Header = Path.GetFileNameWithoutExtension(path);
        UndoRedo.Clear();   // destructive — clear history
        NotifySuccess(L("ImportedMsg", Rows.Count, Properties.Count));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(L("ExportTitle"), L("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter> { new("JSON files", new[] { "*.json" }) };
        var path = await _fileDialogService.SaveFileAsync("Export JSON", filters);
        if (path == null) return;

        var json = _jsonService.SerializeToJson(Rows, Properties);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        NotifySuccess(L("ExportedMsg", Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task ImportClassAsync()
    {
        var filters = new List<FileFilter> { new("C# files", new[] { "*.cs" }) };
        var path = await _fileDialogService.OpenFileAsync("Import C# Class", filters);
        if (path == null) return;

        var code = await File.ReadAllTextAsync(path);
        var (className, parsedProps) = _classParserService.ParseClassFile(code);

        if (parsedProps.Count == 0)
        {
            await _dialogService.ShowMessageAsync(L("ImportTitle"), L("NoPropsFoundMsg"));
            return;
        }

        Properties.Clear();
        foreach (var prop in parsedProps)
            Properties.Add(prop);

        Rows.Clear();
        Header = className;
        UndoRedo.Clear();   // destructive — clear history
        NotifySuccess(L("ImportedClassMsg", className, Properties.Count));
        FireColumnsChanged();
    }

    [RelayCommand]
    private async Task ExportClassAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(L("ExportTitle"), L("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter> { new("C# files", new[] { "*.cs" }) };
        var path = await _fileDialogService.SaveFileAsync("Export C# Class", filters);
        if (path == null) return;

        var className = Path.GetFileNameWithoutExtension(path);
        var code = _classParserService.GenerateClassCode(className, Properties);
        await File.WriteAllTextAsync(path, code, Encoding.UTF8);
        Header = className;
        NotifySuccess(L("ExportedClassMsg", className));
    }

    // -----------------------------------------------------------------------
    //  Other commands
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task RenameAsync()
    {
        var newName = await _dialogService.ShowInputDialogAsync(
            L("RenameWorkspaceTitle"), L("RenameWorkspacePrompt"), Header, L("WorkspaceNameLabel"));

        if (newName == null) return;

        if (string.IsNullOrWhiteSpace(newName))
        {
            NotifyWarning(L("WorkspaceNameEmpty"));
            return;
        }

        var trimmed = newName.Trim();
        if (trimmed == Header) return;

        Header = trimmed;
        NotifySuccess(L("WorkspaceRenamedMsg", Header));
    }
}
