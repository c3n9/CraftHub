using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using CraftHub.Converters;
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

            var column = new DataGridTemplateColumn
            {
                Header = header,
                Width = DataGridLength.Auto,
                IsReadOnly = false
            };

            var isComplexType = prop.FieldType == JsonFieldType.Object || prop.FieldType == JsonFieldType.Array;

            column.CellTemplate = new FuncDataTemplate<DynamicDataRow>((row, ns) => 
            {
                var border = new Border();
                var mb = new MultiBinding
                {
                    Converter = new SearchHighlightConverter()
                };
                mb.Bindings.Add(new Binding($"[{prop.Name}]"));
                mb.Bindings.Add(new Binding("DataContext.SearchQuery") { RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) } });
                border.Bind(Border.BackgroundProperty, mb);

                if (isComplexType)
                {
                    var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    var tb = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Avalonia.Thickness(12, 12, 8, 12),
                        Foreground = Brushes.LightGray,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    tb.Bind(TextBlock.TextProperty, new Binding($"[{prop.Name}]") 
                    { 
                        Converter = new JsonPreviewConverter() 
                    });
                    
                    var editBtn = new Button
                    {
                        Content = "📝",
                        Padding = new Avalonia.Thickness(4, 2),
                        Margin = new Avalonia.Thickness(0, 0, 4, 0),
                        Cursor = Avalonia.Input.Cursor.Parse("Hand"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = Brushes.Transparent
                    };
                    Grid.SetColumn(editBtn, 1);
                    
                    editBtn.Click += async (s, e) => 
                    {
                        if (DataContext is WorkspaceViewModel vm)
                        {
                            await vm.EditJsonCellAsync(row, prop.Name, prop.FieldType);
                        }
                    };

                    grid.Children.Add(tb);
                    grid.Children.Add(editBtn);
                    border.Child = grid;
                }
                else
                {
                    var tb = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(12, 0)
                    };
                    tb.Bind(TextBlock.TextProperty, new Binding($"[{prop.Name}]") { Mode = BindingMode.TwoWay });
                    border.Child = tb;
                }
                
                return border;
            });

            if (!isComplexType)
            {
                column.CellEditingTemplate = new FuncDataTemplate<DynamicDataRow>((row, ns) => 
                {
                    var tb = new TextBox
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(4, 0),
                        Padding = new Avalonia.Thickness(0)
                    };
                    tb.Bind(TextBox.TextProperty, new Binding($"[{prop.Name}]") { Mode = BindingMode.TwoWay });
                    return tb;
                });
            }
            else
            {
                column.IsReadOnly = true; // Prevent normal editing for complex types
            }

            DataGrid.Columns.Add(column);
        }
    }
}
