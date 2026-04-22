using Avalonia.Controls;
using Avalonia.Data;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Specialized;

namespace CraftHub.Views;

public partial class JsonEditorView : Window
{
    private JsonEditorViewModel? _currentVm;

    public JsonEditorView()
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

            vm.JsonSubmitted += (s, res) => { _isConfirmedClose = true; Close(res); };
            vm.Cancelled += (s, args) => { _isConfirmedClose = true; Close(null); };

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

            var headerText = new Avalonia.Controls.TextBlock
            {
                Text = header,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                MaxLines = 1,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Avalonia.Controls.ToolTip.SetTip(headerText, header);

            var column = new Avalonia.Controls.DataGridTemplateColumn
            {
                Header = headerText,
                Width = Avalonia.Controls.DataGridLength.SizeToCells,
                MinWidth = 100,
                MaxWidth = 600,
                IsReadOnly = false
            };

            var isComplexType = prop.FieldType == JsonFieldType.Object || prop.FieldType == JsonFieldType.Array;
            var isBoolType = prop.FieldType == JsonFieldType.Bool;

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
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MaxLines = 3
                    };
                    tb.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding
                    {
                        Path = ".",
                        Converter = new CraftHub.Converters.DynamicRowJsonPreviewConverter(),
                        ConverterParameter = prop.Name
                    });

                    var editBtn = new Avalonia.Controls.Button
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
                    if (isBoolType)
                    {
                        var cb = new Avalonia.Controls.CheckBox
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(12, 0)
                        };
                        cb.Bind(Avalonia.Controls.CheckBox.IsCheckedProperty, new Avalonia.Data.Binding
                        {
                            Path = $"[{prop.Name}]",
                            Mode = Avalonia.Data.BindingMode.TwoWay,
                            Converter = new CraftHub.Converters.DynamicRowBoolConverter()
                        });
                        border.Child = cb;
                    }
                    else
                    {
                        var tb = new Avalonia.Controls.TextBlock
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(12, 0),
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                            MaxLines = 1
                        };
                        tb.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding
                        {
                            Path = ".",
                            Converter = new CraftHub.Converters.DynamicRowValueConverter(),
                            ConverterParameter = prop.Name
                        });
                        border.Child = tb;
                    }
                }

                return border;
            });

            if (!isComplexType)
            {
                column.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DynamicDataRow>((row, ns) =>
                {
                    if (isBoolType)
                    {
                        var cb = new Avalonia.Controls.CheckBox
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(12, 0)
                        };
                        cb.Bind(Avalonia.Controls.CheckBox.IsCheckedProperty, new Avalonia.Data.Binding
                        {
                            Path = $"[{prop.Name}]",
                            Mode = Avalonia.Data.BindingMode.TwoWay,
                            Converter = new CraftHub.Converters.DynamicRowBoolConverter()
                        });
                        return cb;
                    }
                    else
                    {
                        var tb = new Avalonia.Controls.TextBox
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(4, 0),
                            Padding = new Avalonia.Thickness(0)
                        };
                        tb.Text = row[prop.Name];
                        tb.TextChanged += (_, _) => row[prop.Name] = tb.Text ?? string.Empty;
                        return tb;
                    }
                });
            }
            else
            {
                column.IsReadOnly = true;
            }

            NestedDataGrid.Columns.Add(column);
        }
    }

    private bool _isConfirmedClose = false;

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isConfirmedClose) return;

        e.Cancel = true;

        var dialogService = App.Current.Services.GetRequiredService<IDialogService>();
        var confirmed = await dialogService.ShowConfirmAsync(Localizer.Get("ClosingWarningTitle"), Localizer.Get("ClosingWarningMsg"));
        if (!confirmed)
        {
            return;
        }
        if (confirmed)
        {
            _isConfirmedClose = true;
            Close();
        }
    }
}
