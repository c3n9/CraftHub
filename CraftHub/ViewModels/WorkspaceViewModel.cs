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

    private void NotifySuccess(string message) => _notificationService.Publish(NotificationType.Success, message);
    private void NotifyWarning(string message) => _notificationService.Publish(NotificationType.Warning, message);
    private static string L(string key) => LanguageService.Instance.Get(key);
    private static string L(string key, params object[] args) => LanguageService.Instance.Get(key, args);

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
    }

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

        // Add the property to all existing rows
        foreach (var row in Rows)
            row.InitializeProperty(prop.Name);

        PropertyName = string.Empty;
        NotifySuccess(L("PropertyAdded", prop.Name));
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RemovePropertyAsync(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;
        var confirmed = await _dialogService.ShowConfirmAsync(
            L("RemovePropertyTitle"),
            L("RemovePropertyMsg", prop.Name));
        if (!confirmed)
        {
            return;
        }
        Properties.Remove(prop);
        foreach (var row in Rows)
            row.RemoveProperty(prop.Name);
        NotifySuccess(L("PropertyRemoved", prop.Name));
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new DynamicDataRow();
        foreach (var prop in Properties)
            row.InitializeProperty(prop.Name);
        Rows.Add(row);
        NotifySuccess(L("RowAdded", Rows.Count));
    }

    [RelayCommand]
    private void DuplicateRows(object? parameter)
    {
        if (parameter is not IList items || items.Count == 0)
        {
            if (SelectedRow != null)
                DuplicateSingleRow(SelectedRow);
            return;
        }

        var toDuplicate = items.Cast<DynamicDataRow>().ToList();
        foreach (var row in toDuplicate)
        {
            DuplicateSingleRow(row);
        }

        NotifySuccess(L("RowsDuplicatedMsg", toDuplicate.Count));
    }

    private void DuplicateSingleRow(DynamicDataRow row)
    {
        var newRow = new DynamicDataRow();
        foreach (var prop in Properties)
        {
            newRow.InitializeProperty(prop.Name, row[prop.Name]);
        }
        Rows.Add(newRow);
    }

    [RelayCommand]
    private async Task CopyRowsToJsonAsync(object? parameter)
    {
        IList? items = parameter as IList;
        List<DynamicDataRow> selectedRows;

        if (items == null || items.Count == 0)
        {
            if (SelectedRow == null) return;
            selectedRows = new List<DynamicDataRow> { SelectedRow };
        }
        else
        {
            selectedRows = items.Cast<DynamicDataRow>().ToList();
        }

        string json;
        if (selectedRows.Count == 1)
        {
            json = _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties);
        }
        else
        {
            json = _jsonService.SerializeToJson(selectedRows, Properties);
        }

        await _dialogService.CopyToClipboardAsync(json);
        NotifySuccess(L("RowsCopiedMsg", selectedRows.Count));
    }

    [RelayCommand]
    private async Task CopyRowsToJsonAsObjectsAsync(object? parameter)
    {
        IList? items = parameter as IList;
        List<DynamicDataRow> selectedRows;

        if (items == null || items.Count == 0)
        {
            if (SelectedRow == null) return;
            selectedRows = new List<DynamicDataRow> { SelectedRow };
        }
        else
        {
            selectedRows = items.Cast<DynamicDataRow>().ToList();
        }

        string json;

        if (selectedRows.Count == 1)
        {
            json = _jsonService.SerializeSingleRowToJson(selectedRows[0], Properties);
        }
        else
        {
            var objects = selectedRows.Select(row => _jsonService.SerializeSingleRowToJson(row, Properties));

            json = string.Join(", ", objects);
        }

        await _dialogService.CopyToClipboardAsync(json);
        NotifySuccess(L("RowsCopiedMsg", selectedRows.Count));
    }

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        var filters = new List<FileFilter>
        {
            new("JSON files", new[] { "*.json" })
        };

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

            // Show the type mapping dialog
            var mappedFields = await _dialogService.ShowFieldMappingDialogAsync(detectedFields);
            if (mappedFields == null) return; // User cancelled

            // Build property definitions from mapped fields
            foreach (var field in mappedFields)
            {
                Properties.Add(new JsonPropertyDefinition
                {
                    Name = field.FieldName,
                    FieldType = field.SelectedType
                });
            }
        }

        // Parse data with the schema
        var rows = _jsonService.ParseJsonData(json, Properties);
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);

        Header = Path.GetFileNameWithoutExtension(path);
        NotifySuccess(L("ImportedMsg", Rows.Count, Properties.Count));
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(L("ExportTitle"), L("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter>
        {
            new("JSON files", new[] { "*.json" })
        };

        var path = await _fileDialogService.SaveFileAsync("Export JSON", filters);
        if (path == null) return;

        var json = _jsonService.SerializeToJson(Rows, Properties);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        NotifySuccess(L("ExportedMsg", Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task ImportClassAsync()
    {
        var filters = new List<FileFilter>
        {
            new("C# files", new[] { "*.cs" })
        };

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
        NotifySuccess(L("ImportedClassMsg", className, Properties.Count));
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ExportClassAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync(L("ExportTitle"), L("AddPropsBeforeExport"));
            return;
        }

        var filters = new List<FileFilter>
        {
            new("C# files", new[] { "*.cs" })
        };

        var path = await _fileDialogService.SaveFileAsync("Export C# Class", filters);
        if (path == null) return;

        var className = Path.GetFileNameWithoutExtension(path);
        var code = _classParserService.GenerateClassCode(className, Properties);
        await File.WriteAllTextAsync(path, code, Encoding.UTF8);
        Header = className;
        NotifySuccess(L("ExportedClassMsg", className));
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        var newName = await _dialogService.ShowInputDialogAsync(
            L("RenameWorkspaceTitle"),
            L("RenameWorkspacePrompt"),
            Header,
            L("WorkspaceNameLabel"));

        if (newName == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            NotifyWarning(L("WorkspaceNameEmpty"));
            return;
        }

        var trimmed = newName.Trim();
        if (trimmed == Header)
        {
            return;
        }

        Header = trimmed;
        NotifySuccess(L("WorkspaceRenamedMsg", Header));
    }

    public async Task EditJsonCellAsync(DynamicDataRow row, string propertyName, JsonFieldType type)
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
            
            // Force Avalonia DataGrid to refresh the row entirely by using a new instance
            var idx = Rows.IndexOf(row);
            if (idx >= 0) 
            {
                Rows[idx] = newRow;
            }

            NotifySuccess(L("UpdatedCellMsg", propertyName));
        }
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

        foreach (var row in toRemove)
        {
            Rows.Remove(row);
        }

        NotifySuccess(L("RowsRemovedMsg", toRemove.Count));
    }

    private async Task RemoveSingleRowAsync(DynamicDataRow row)
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            L("RemoveRowTitle"),
            L("RemoveRowMsg"));

        if (confirmed)
        {
            Rows.Remove(row);
            NotifySuccess(L("RowRemovedMsg"));
        }
    }
}
