using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Data;
using CraftHub.Models;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private WorkspaceViewModel? _currentVm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.ColumnsChanged -= OnColumnsChanged;
            _currentVm.Properties.CollectionChanged -= OnPropertiesChanged;
        }

        if (DataContext is WorkspaceViewModel vm)
        {
            _currentVm = vm;
            vm.ColumnsChanged += OnColumnsChanged;
            vm.Properties.CollectionChanged += OnPropertiesChanged;
            RebuildColumns(vm);
        }
    }

    private void OnColumnsChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
            RebuildColumns(_currentVm);
    }

    private void OnPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentVm != null)
            RebuildColumns(_currentVm);
    }

    private void RebuildColumns(WorkspaceViewModel vm)
    {
        DataGrid.Columns.Clear();

        foreach (var prop in vm.Properties)
        {
            var header = $"{prop.Name} ({JsonPropertyDefinition.GetTypeDisplayName(prop.FieldType)})";

            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"[{prop.Name}]", BindingMode.TwoWay),
                Width = DataGridLength.Auto,
                IsReadOnly = false
            };

            DataGrid.Columns.Add(column);
        }
    }
}
