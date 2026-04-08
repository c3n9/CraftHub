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
using CraftHub.Helpers;
using CraftHub.Domain.Models;
using CraftHub.Domain.Enums;

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

            var headerText = new TextBlock
            {
                Text = header,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(headerText, header);

            var column = new DataGridTemplateColumn
            {
                Header = headerText,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 140,
                IsReadOnly = false,
            };

            var isComplexType = prop.FieldType == JsonFieldType.Object || prop.FieldType == JsonFieldType.Array;
            var isBoolType = prop.FieldType == JsonFieldType.Bool;

            column.CellTemplate = new FuncDataTemplate<DynamicDataRow>((row, ns) => 
            {
                var border = new Border();
                var mb = new MultiBinding
                {
                    Converter = new SearchHighlightConverter()
                };
                mb.Bindings.Add(new Binding
                {
                    Path = ".",
                    Converter = new DynamicRowValueConverter(),
                    ConverterParameter = prop.Name
                });
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
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MaxLines = 3
                    };
                    tb.Bind(TextBlockHelper.OriginalTextProperty, new Binding
                    {
                        Path = ".",
                        Converter = new DynamicRowJsonPreviewConverter(),
                        ConverterParameter = prop.Name
                    });
                    tb.Bind(TextBlockHelper.HighlightTextProperty, new Binding("DataContext.SearchQuery") { RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) } });
                    
                    var editBtn = new Button
                    {
                        Content = new Material.Icons.Avalonia.MaterialIcon
                        {
                            Kind = Material.Icons.MaterialIconKind.PencilOutline,
                            Width = 16,
                            Height = 16
                        },
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
                    if (isBoolType)
                    {
                        var cb = new CheckBox
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(12, 8, 12, 8)
                        };
                        cb.Bind(CheckBox.IsCheckedProperty, new Binding
                        {
                            Path = $"[{prop.Name}]",
                            Mode = BindingMode.TwoWay,
                            Converter = new DynamicRowBoolConverter()
                        });
                        border.Child = cb;
                    }
                    else
                    {
                        var tb = new TextBlock
                        {
                            VerticalAlignment = VerticalAlignment.Top,
                            Margin = new Avalonia.Thickness(12, 8, 12, 8),
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        };
                        tb.Bind(TextBlockHelper.OriginalTextProperty, new Binding
                        {
                            Path = ".",
                            Converter = new DynamicRowValueConverter(),
                            ConverterParameter = prop.Name
                        });
                        tb.Bind(TextBlockHelper.HighlightTextProperty, new Binding("DataContext.SearchQuery") { RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) } });
                        border.Child = tb;
                    }
                }
                
                return border;
            });

            if (!isComplexType)
            {
                column.CellEditingTemplate = new FuncDataTemplate<DynamicDataRow>((row, ns) => 
                {
                    if (isBoolType)
                    {
                        var cb = new CheckBox
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(12, 8, 12, 8)
                        };
                        cb.Bind(CheckBox.IsCheckedProperty, new Binding
                        {
                            Path = $"[{prop.Name}]",
                            Mode = BindingMode.TwoWay,
                            Converter = new DynamicRowBoolConverter()
                        });
                        return cb;
                    }
                    else
                    {
                        var tb = new TextBox
                        {
                            Classes = { "grid-editor" },
                            VerticalAlignment = VerticalAlignment.Stretch,
                            VerticalContentAlignment = VerticalAlignment.Top,
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            TextWrapping = TextWrapping.Wrap,
                            AcceptsReturn = true
                        };
                        tb.Text = row[prop.Name];
                        tb.TextChanged += (_, _) => row[prop.Name] = tb.Text ?? string.Empty;
                        return tb;
                    }
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
