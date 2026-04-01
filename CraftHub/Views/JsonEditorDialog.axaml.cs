using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Data;
using CraftHub.Models;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class JsonEditorDialog : Window
{
    private JsonEditorViewModel? _currentVm;

    public JsonEditorDialog()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.Properties.CollectionChanged -= OnPropertiesChanged;
        }

        if (DataContext is JsonEditorViewModel vm)
        {
            _currentVm = vm;
            vm.Properties.CollectionChanged += OnPropertiesChanged;
            
            vm.JsonSubmitted += (s, res) => Close(res);
            vm.Cancelled += (s, args) => Close(null);

            RebuildColumns(vm);
        }
    }

    private void OnPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentVm != null) RebuildColumns(_currentVm);
    }

    private void RebuildColumns(JsonEditorViewModel vm)
    {
        NestedDataGrid.Columns.Clear();
        foreach (var prop in vm.Properties)
        {
            var header = $"{prop.Name} ({JsonPropertyDefinition.GetTypeDisplayName(prop.FieldType)})";

            var column = new Avalonia.Controls.DataGridTemplateColumn
            {
                Header = header,
                Width = Avalonia.Controls.DataGridLength.Auto,
                IsReadOnly = false
            };

            var isComplexType = prop.FieldType == JsonFieldType.Object || prop.FieldType == JsonFieldType.Array;

            column.CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DynamicDataRow>((row, ns) => 
            {
                var border = new Avalonia.Controls.Border();

                if (isComplexType)
                {
                    var grid = new Avalonia.Controls.Grid { ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("*,Auto") };
                    var tb = new Avalonia.Controls.TextBlock
                    {
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                        Margin = new Avalonia.Thickness(12, 12, 8, 12),
                        Foreground = Avalonia.Media.Brushes.LightGray,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                    tb.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding($"[{prop.Name}]") 
                    { 
                        Converter = new CraftHub.Converters.JsonPreviewConverter() 
                    });
                    
                    var editBtn = new Avalonia.Controls.Button
                    {
                        Content = "📝",
                        Padding = new Avalonia.Thickness(4, 2),
                        Margin = new Avalonia.Thickness(0, 0, 4, 0),
                        Cursor = Avalonia.Input.Cursor.Parse("Hand"),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Background = Avalonia.Media.Brushes.Transparent
                    };
                    Avalonia.Controls.Grid.SetColumn(editBtn, 1);
                    
                    editBtn.Click += async (s, e) => 
                    {
                        if (DataContext is JsonEditorViewModel vmCtx)
                        {
                            await vmCtx.EditJsonCellAsync(row, prop.Name, prop.FieldType);
                        }
                    };

                    grid.Children.Add(tb);
                    grid.Children.Add(editBtn);
                    border.Child = grid;
                }
                else
                {
                    var tb = new Avalonia.Controls.TextBlock
                    {
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(12, 0)
                    };
                    tb.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding($"[{prop.Name}]") { Mode = Avalonia.Data.BindingMode.TwoWay });
                    border.Child = tb;
                }
                
                return border;
            });

            if (!isComplexType)
            {
                column.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DynamicDataRow>((row, ns) => 
                {
                    var tb = new Avalonia.Controls.TextBox
                    {
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(4, 0),
                        Padding = new Avalonia.Thickness(0)
                    };
                    tb.Bind(Avalonia.Controls.TextBox.TextProperty, new Avalonia.Data.Binding($"[{prop.Name}]") { Mode = Avalonia.Data.BindingMode.TwoWay });
                    return tb;
                });
            }
            else
            {
                column.IsReadOnly = true;
            }

            NestedDataGrid.Columns.Add(column);
        }
    }
}
