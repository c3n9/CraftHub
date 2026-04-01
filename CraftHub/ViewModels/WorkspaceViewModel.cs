using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Models;
using CraftHub.Services;

namespace CraftHub.ViewModels;

public partial class WorkspaceViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IJsonService _jsonService;
    private readonly IClassParserService _classParserService;
    private readonly IDialogService _dialogService;

    [ObservableProperty] private string _header = "Tab";
    [ObservableProperty] private string _propertyName = string.Empty;
    [ObservableProperty] private JsonFieldType _selectedType = JsonFieldType.String;
    [ObservableProperty] private JsonFieldType _selectedArrayElementType = JsonFieldType.String;
    [ObservableProperty] private bool _isArrayTypeSelected;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private DynamicDataRow? _selectedRow;

    public ObservableCollection<JsonPropertyDefinition> Properties { get; } = new();
    public ObservableCollection<DynamicDataRow> Rows { get; } = new();
    public Array AvailableTypes => Enum.GetValues(typeof(JsonFieldType));
    public event EventHandler? CloseRequested;
    public event EventHandler? ColumnsChanged;

    public WorkspaceViewModel(
        IFileDialogService fileDialogService,
        IJsonService jsonService,
        IClassParserService classParserService,
        IDialogService dialogService)
    {
        _fileDialogService = fileDialogService;
        _jsonService = jsonService;
        _classParserService = classParserService;
        _dialogService = dialogService;
    }

    partial void OnSelectedTypeChanged(JsonFieldType value)
    {
        IsArrayTypeSelected = value == JsonFieldType.Array;
    }

    [RelayCommand]
    private void AddProperty()
    {
        if (string.IsNullOrWhiteSpace(PropertyName))
        {
            StatusText = "⚠ Enter property name";
            return;
        }

        if (Properties.Any(p => p.Name == PropertyName))
        {
            StatusText = "⚠ Property with this name already exists";
            return;
        }

        var prop = new JsonPropertyDefinition
        {
            Name = PropertyName,
            FieldType = SelectedType,
            ArrayElementType = SelectedArrayElementType
        };

        Properties.Add(prop);

        // Add the property to all existing rows
        foreach (var row in Rows)
            row.InitializeProperty(prop.Name);

        PropertyName = string.Empty;
        StatusText = $"✓ Property '{prop.Name}' added";
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RemoveProperty(JsonPropertyDefinition? prop)
    {
        if (prop == null) return;
        Properties.Remove(prop);
        foreach (var row in Rows)
            row.RemoveProperty(prop.Name);
        StatusText = $"✓ Property '{prop.Name}' removed";
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new DynamicDataRow();
        foreach (var prop in Properties)
            row.InitializeProperty(prop.Name);
        Rows.Add(row);
        StatusText = $"✓ Row added ({Rows.Count} total)";
    }

    [RelayCommand]
    private void RemoveRow()
    {
        if (SelectedRow != null)
        {
            Rows.Remove(SelectedRow);
            StatusText = $"✓ Row removed ({Rows.Count} remaining)";
        }
    }

    [RelayCommand]
    private void DuplicateRow(DynamicDataRow? row)
    {
        if (row == null) return;
        var newRow = new DynamicDataRow();
        foreach (var prop in Properties)
        {
            newRow.InitializeProperty(prop.Name, row[prop.Name]);
        }
        
        var index = Rows.IndexOf(row);
        if (index >= 0) Rows.Insert(index + 1, newRow);
        else Rows.Add(newRow);
        
        StatusText = $"✓ Row duplicated";
    }

    [RelayCommand]
    private async Task CopyRowToJsonAsync(DynamicDataRow? row)
    {
        if (row == null) return;
        var json = _jsonService.SerializeToJson(new[] { row }, Properties);
        await _dialogService.CopyToClipboardAsync(json);
        StatusText = $"✓ Row copied to clipboard as JSON";
    }

    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        var filters = new List<FilePickerFileType>
        {
            new("JSON files") { Patterns = new[] { "*.json" } }
        };

        var path = await _fileDialogService.OpenFileAsync("Import JSON", filters);
        if (path == null) return;

        var json = await File.ReadAllTextAsync(path);

        if (Properties.Count == 0)
        {
            var detectedFields = _jsonService.DetectFields(json);

            if (detectedFields.Count == 0)
            {
                await _dialogService.ShowMessageAsync("Import", "No fields detected in JSON file.");
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
        StatusText = $"✓ Imported {Rows.Count} rows, {Properties.Count} fields";
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Export", "Add properties before exporting.");
            return;
        }

        var filters = new List<FilePickerFileType>
        {
            new("JSON files") { Patterns = new[] { "*.json" } }
        };

        var path = await _fileDialogService.SaveFileAsync("Export JSON", filters);
        if (path == null) return;

        var json = _jsonService.SerializeToJson(Rows, Properties);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        StatusText = $"✓ Exported to {Path.GetFileName(path)}";
    }

    [RelayCommand]
    private async Task ImportClassAsync()
    {
        var filters = new List<FilePickerFileType>
        {
            new("C# files") { Patterns = new[] { "*.cs" } }
        };

        var path = await _fileDialogService.OpenFileAsync("Import C# Class", filters);
        if (path == null) return;

        var code = await File.ReadAllTextAsync(path);
        var (className, parsedProps) = _classParserService.ParseClassFile(code);

        if (parsedProps.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Import", "No properties found in the class file.");
            return;
        }

        Properties.Clear();
        foreach (var prop in parsedProps)
            Properties.Add(prop);

        Rows.Clear();
        Header = className;
        StatusText = $"✓ Imported class '{className}' with {Properties.Count} properties";
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ExportClassAsync()
    {
        if (Properties.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Export", "Add properties before exporting.");
            return;
        }

        var filters = new List<FilePickerFileType>
        {
            new("C# files") { Patterns = new[] { "*.cs" } }
        };

        var path = await _fileDialogService.SaveFileAsync("Export C# Class", filters);
        if (path == null) return;

        var className = Path.GetFileNameWithoutExtension(path);
        var code = _classParserService.GenerateClassCode(className, Properties);
        await File.WriteAllTextAsync(path, code, Encoding.UTF8);
        Header = className;
        StatusText = $"✓ Exported class '{className}'";
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
