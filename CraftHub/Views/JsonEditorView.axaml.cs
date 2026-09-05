using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using CraftHub.Services.Actions;
using CraftHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace CraftHub.Views;

public partial class JsonEditorView : Window
{
    private JsonEditorViewModel? _currentVm;

    private (DynamicDataRow Row, string PropName, string OldValue)? _pendingEdit;

    /// <summary>The same completion popup the main grid uses (function names, column names inside
    /// <c>[ ]</c>/<c>@[ ]</c>, signature hints, arrow-key selection) — see
    /// <see cref="FormulaAutocomplete"/>. No Ctrl+Enter here: this dialog doesn't offer
    /// "apply to the whole column".</summary>
    private FormulaAutocomplete? _autocomplete;

    private FormulaAutocomplete Autocomplete => _autocomplete ??=
        new FormulaAutocomplete(FormulaSuggestionsPopup, FormulaSuggestionsList, this,
            () => _currentVm?.Properties.Select(p => p.Name) ?? System.Linq.Enumerable.Empty<string>());

    public JsonEditorView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        NestedDataGrid.CellEditEnded += OnCellEditEnded;
    }

    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        _autocomplete?.Close();
        if (_pendingEdit == null || _currentVm == null) return;

        var (row, propName, oldValue) = _pendingEdit.Value;
        _pendingEdit = null;

        // Routes '=' text to the formula engine (when enabled) and pushes the right undo step;
        // for a plain value it does what the old direct EditCellAction push did.
        _currentVm.CommitCellText(row, propName, oldValue, row[propName], NestedDataGrid);
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
            vm.FormulaVisualsChanged += (_, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DataGridRefresh.Rows(NestedDataGrid),
                    Avalonia.Threading.DispatcherPriority.Background);

            RebuildColumns(vm);
        }
    }

    private void OnPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentVm == null) return;

        // Update only the affected columns so adding/removing a field does not tear down and
        // re-create every column (which forces the whole grid to re-render and drops widths).
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add
                when e.NewItems is { Count: 1 } added && added[0] is JsonPropertyDefinition prop:
                NestedDataGrid.Columns.Insert(
                    e.NewStartingIndex >= 0 ? e.NewStartingIndex : NestedDataGrid.Columns.Count,
                    BuildColumn(prop));
                break;

            case NotifyCollectionChangedAction.Remove
                when e.OldStartingIndex >= 0 && e.OldStartingIndex < NestedDataGrid.Columns.Count:
                NestedDataGrid.Columns.RemoveAt(e.OldStartingIndex);
                break;

            default:
                // Reset, Replace, Move — structure changed too much to patch.
                RebuildColumns(_currentVm);
                break;
        }
    }

    private void RebuildColumns(JsonEditorViewModel vm)
    {
        // Preserve per-column widths (keyed by property name in Tag) across the rebuild.
        var savedWidths = new System.Collections.Generic.Dictionary<string, Avalonia.Controls.DataGridLength>();
        foreach (var col in NestedDataGrid.Columns)
            if (col.Tag is string tag)
                savedWidths[tag] = col.Width;

        NestedDataGrid.Columns.Clear();
        foreach (var prop in vm.Properties)
        {
            var column = BuildColumn(prop);
            if (savedWidths.TryGetValue(prop.Name, out var savedWidth))
                column.Width = savedWidth;
            NestedDataGrid.Columns.Add(column);
        }
    }

    /// <summary>Adds a small "fx" corner marker (with the formula text / error as its tooltip) over
    /// a cell's value when that cell is a formula. Returns <paramref name="valueControl"/> unchanged
    /// when it isn't, or when this dialog has no formula session.</summary>
    private Avalonia.Controls.Control WrapWithFormulaMarker(Avalonia.Controls.Control valueControl, DynamicDataRow row, string columnKey)
    {
        if (_currentVm is not { FormulasEnabled: true } vm) return valueControl;
        var rowIndex = vm.Rows.IndexOf(row);
        if (rowIndex < 0 || !vm.IsFormulaCell(rowIndex, columnKey)) return valueControl;

        var error = vm.GetFormulaErrorState(rowIndex, columnKey);
        var formula = vm.GetDisplayFormula(rowIndex, columnKey) ?? "";
        var tip = error != null ? $"{formula}\n\n{error.ErrorCode}: {error.Message}" : formula;

        var marker = new Material.Icons.Avalonia.MaterialIcon
        {
            Kind = Material.Icons.MaterialIconKind.FunctionVariant,
            Width = 11,
            Height = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 3, 3, 0),
            Foreground = error != null ? Avalonia.Media.Brushes.OrangeRed : Avalonia.Media.Brushes.Gray,
            Opacity = error != null ? 1.0 : 0.6
        };

        var grid = new Avalonia.Controls.Grid();
        grid.Children.Add(valueControl);
        grid.Children.Add(marker);
        Avalonia.Controls.ToolTip.SetTip(grid, tip);
        return grid;
    }

    private Avalonia.Controls.DataGridTemplateColumn BuildColumn(JsonPropertyDefinition prop)
    {
            var header =
                $"{JsonPropertyDefinition.GetDisplayPath(prop.Name)} ({JsonPropertyDefinition.GetTypeDisplayName(prop.FieldType)})";

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
                Tag = prop.Name,
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
                        cb.Bind(Avalonia.Controls.CheckBox.IsCheckedProperty,
                            DynamicRowCellBinding.ForKey(prop.Name, Avalonia.Data.BindingMode.TwoWay, new CraftHub.Converters.DynamicRowBoolConverter()));

                        // Undo tracking for bool cells: PointerPressed flags a real user click and
                        // snapshots the old value, so binding-driven changes from Undo/Redo are ignored.
                        var boolOldValue = row[prop.Name] == "true";
                        var boolUserInteraction = false;

                        cb.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
                        {
                            boolUserInteraction = true;
                            boolOldValue = row[prop.Name] == "true";
                        }, RoutingStrategies.Tunnel);

                        cb.IsCheckedChanged += (_, _) =>
                        {
                            if (!boolUserInteraction) return;
                            boolUserInteraction = false;

                            var newVal = cb.IsChecked == true;
                            if (newVal != boolOldValue && _currentVm != null)
                                _currentVm.UndoRedo.Push(new EditCheckBoxCellAction(row, prop.Name, boolOldValue, newVal, NestedDataGrid));
                        };

                        border.Child = cb;
                    }
                    else
                    {
                        var tb = new Avalonia.Controls.TextBlock
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                            Margin = new Avalonia.Thickness(12, 8, 12, 8),
                            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            MaxLines = 3
                        };
                        tb.Bind(Avalonia.Controls.TextBlock.TextProperty, new Avalonia.Data.Binding
                        {
                            Path = ".",
                            Converter = new CraftHub.Converters.DynamicRowValueConverter(),
                            ConverterParameter = prop.Name
                        });
                        border.Child = WrapWithFormulaMarker(tb, row, prop.Name);
                    }
                }

                return border;
            });

            if (!isComplexType)
            {
                column.CellEditingTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DynamicDataRow>((row, ns) =>
                {
                    // Snapshot the value as the editor opens; OnCellEditEnded compares against it.
                    _pendingEdit = (row, prop.Name, row[prop.Name]);

                    if (isBoolType)
                    {
                        var cb = new Avalonia.Controls.CheckBox
                        {
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Margin = new Avalonia.Thickness(12, 0)
                        };
                        cb.Bind(Avalonia.Controls.CheckBox.IsCheckedProperty,
                            DynamicRowCellBinding.ForKey(prop.Name, Avalonia.Data.BindingMode.TwoWay, new CraftHub.Converters.DynamicRowBoolConverter()));
                        return cb;
                    }
                    else
                    {
                        var rowIndex = _currentVm?.Rows.IndexOf(row) ?? -1;
                        var initial = _currentVm != null && rowIndex >= 0
                            ? _currentVm.GetEditableCellText(rowIndex, prop.Name)
                            : row[prop.Name];

                        var tb = new Avalonia.Controls.TextBox
                        {
                            Classes = { "grid-editor" },
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
                            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            AcceptsReturn = true,
                            Text = initial
                        };
                        // Live-write keeps plain-value editing working; CommitCellText undoes it
                        // for '=' text (a formula's text is not the row's data).
                        tb.TextChanged += (_, _) => row[prop.Name] = tb.Text ?? string.Empty;
                        if (_currentVm is { FormulasEnabled: true }) Autocomplete.Attach(tb);
                        return tb;
                    }
                });
            }
            else
            {
                column.IsReadOnly = true;
            }

            return column;
    }

    private bool _isConfirmedClose = false;

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isConfirmedClose) return;

        if (_currentVm is { UndoRedo.CanUndo: false }) return;

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
