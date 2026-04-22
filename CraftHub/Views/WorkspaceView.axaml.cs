using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CraftHub.Converters;
using CraftHub.Models;
using CraftHub.ViewModels;
using CraftHub.Helpers;
using CraftHub.Domain.Models;
using CraftHub.Domain.Enums;
using CraftHub.Services;
using CraftHub.Services.Actions;

namespace CraftHub.Views;

public partial class WorkspaceView : UserControl
{
    // Snapshot captured when the DataGrid enters edit mode (FuncDataTemplate factory runs).
    // Used by CellEditEnded to push an undo action only when the value actually changed.
    private (DynamicDataRow Row, string PropName, string OldValue)? _pendingEdit;

    private TextBox? _jsonTextBox;
    private ScrollViewer? _lineNumberScroller;
    private TextBlock? _lineNumbersBlock;
    private int _lastLineCount = -1;

    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataGrid.LoadingRow       += OnDataGridLoadingRow;
        DataGrid.SelectionChanged  += OnDataGridSelectionChanged;
        DataGrid.BeginningEdit     += (_, _) => SetCellEditing(true);
        DataGrid.CellEditEnded     += OnDataGridCellEditEnded;
        DataGrid.GotFocus          += async (_, _) =>
        {
            if (DataContext is WorkspaceViewModel vm)
                await vm.RefreshClipboardStateAsync();
        };
        InitJsonEditor();

        // Refresh clipboard state each time the context menu is about to open
        // so Paste is enabled/disabled correctly before the user sees the menu.
        var ctx = DataGrid.ContextMenu;
        if (ctx != null)
            ctx.Opening += async (_, _) =>
            {
                if (DataContext is WorkspaceViewModel vm)
                    await vm.RefreshClipboardStateAsync();
            };
    }

    // -----------------------------------------------------------------------
    //  JSON editor — line numbers + scroll sync
    // -----------------------------------------------------------------------

    private void InitJsonEditor()
    {
        _jsonTextBox       = this.FindControl<TextBox>("JsonTextBox");
        _lineNumberScroller = this.FindControl<ScrollViewer>("LineNumberScroller");
        _lineNumbersBlock  = this.FindControl<TextBlock>("LineNumbersBlock");

        if (_jsonTextBox == null || _lineNumbersBlock == null) return;

        _jsonTextBox.TextChanged += (_, _) => RefreshLineNumbers();

        // After the TextBox template is applied its internal ScrollViewer exists.
        // Post to Background so the visual tree is fully ready before we search it.
        _jsonTextBox.TemplateApplied += (_, _) =>
            Dispatcher.UIThread.Post(HookScrollSync, DispatcherPriority.Background);
    }

    private void HookScrollSync()
    {
        if (_jsonTextBox == null || _lineNumberScroller == null) return;
        var sv = _jsonTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (sv == null) return;
        sv.ScrollChanged += (_, _) =>
            _lineNumberScroller.Offset = new Vector(0, sv.Offset.Y);
    }

    private void RefreshLineNumbers()
    {
        if (_lineNumbersBlock == null || _jsonTextBox == null) return;
        var text = _jsonTextBox.Text ?? string.Empty;
        var count = 1;
        foreach (var c in text)
            if (c == '\n') count++;
        if (count == _lastLineCount) return;
        _lastLineCount = count;
        _lineNumbersBlock.Text = string.Join("\n", Enumerable.Range(1, count));
    }

    // -----------------------------------------------------------------------
    //  Row-number header
    // -----------------------------------------------------------------------

    private void OnDataGridLoadingRow(object? sender, DataGridRowEventArgs e)
        => e.Row.Header = (e.Row.Index + 1).ToString();

    // -----------------------------------------------------------------------
    //  Selection → status bar
    // -----------------------------------------------------------------------

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm)
            vm.SelectedRowsCount = DataGrid.SelectedItems?.Count ?? 0;
    }

    // -----------------------------------------------------------------------
    //  Cell edit tracking for undo / clipboard guard
    // -----------------------------------------------------------------------

    private void SetCellEditing(bool value)
    {
        if (DataContext is WorkspaceViewModel vm)
            vm.IsCellEditing = value;
    }

    private async void OnDataGridCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        SetCellEditing(false);

        if (_pendingEdit == null) return;

        var (row, propName, oldValue) = _pendingEdit.Value;
        _pendingEdit = null;

        var newValue = row[propName];
        if (newValue == oldValue) return;

        if (DataContext is WorkspaceViewModel vm)
            vm.UndoRedo.Push(new EditCellAction(row, propName, oldValue, newValue, DataGrid));
    }


    // -----------------------------------------------------------------------
    //  DataContext / column wiring
    // -----------------------------------------------------------------------

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
        if (_currentVm != null) RebuildColumns(_currentVm);
    }

    private void OnPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_currentVm != null) RebuildColumns(_currentVm);
    }

    // -----------------------------------------------------------------------
    //  Column builder
    // -----------------------------------------------------------------------

    private void RebuildColumns(WorkspaceViewModel vm)
    {
        DataGrid.Columns.Clear();

        foreach (var prop in vm.Properties)
        {
            var header = $"{prop.Name} ({JsonPropertyDefinition.GetTypeDisplayName(prop.FieldType)})";

            var column = new DataGridTemplateColumn
            {
                // Tag = property name so CellEditEnded can identify which field changed
                Tag = prop.Name,
                Header = header,
                HeaderTemplate = new FuncDataTemplate<object>((_, _) => new TextBlock
                {
                    Text = header,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    [!ToolTip.TipProperty] = new Binding { Source = header }
                }),
                Width = DataGridLength.Auto,
                MinWidth = 100,
                IsReadOnly = false,
                SortMemberPath = $"[{prop.Name}]",
                CanUserSort = true,
                CanUserResize = true
            };

            var isComplexType = prop.FieldType == JsonFieldType.Object || prop.FieldType == JsonFieldType.Array;
            var isBoolType    = prop.FieldType == JsonFieldType.Bool;

            // ---- read-only cell template ----
            column.CellTemplate = new FuncDataTemplate<DynamicDataRow>((row, _) =>
            {
                var border = new Border();
                var mb = new MultiBinding { Converter = new SearchHighlightConverter() };
                // Use the indexer path so the background reacts to PropertyChanged("Item[]")
                // (e.g. after undo/redo) without needing a converter.
                mb.Bindings.Add(new Binding($"[{prop.Name}]"));
                mb.Bindings.Add(new Binding("DataContext.SearchQuery")
                {
                    RelativeSource = new RelativeSource
                        { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                });
                border.Bind(Border.BackgroundProperty, mb);

                if (isComplexType)
                {
                    var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

                    var tb = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Avalonia.Thickness(12, 12, 8, 12),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.Wrap,
                        MaxLines = 3,
                    };
                    tb.Bind(TextBlockHelper.OriginalTextProperty, new Binding
                    {
                        Path = ".",
                        Converter = new DynamicRowJsonPreviewConverter(),
                        ConverterParameter = prop.Name
                    });
                    tb.Bind(TextBlockHelper.HighlightTextProperty,
                        new Binding("DataContext.SearchQuery")
                        {
                            RelativeSource = new RelativeSource
                                { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                        });

                    var editBtn = new Button
                    {
                        Content = new Material.Icons.Avalonia.MaterialIcon
                            { Kind = Material.Icons.MaterialIconKind.PencilOutline, Width = 16, Height = 16 },
                        Padding = new Avalonia.Thickness(4, 2),
                        Margin = new Avalonia.Thickness(0, 0, 4, 0),
                        Cursor = Avalonia.Input.Cursor.Parse("Hand"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = Brushes.Transparent
                    };
                    Grid.SetColumn(editBtn, 1);
                    editBtn.Click += async (_, _) =>
                    {
                        if (DataContext is WorkspaceViewModel vm2)
                            await vm2.EditJsonCellAsync(row, prop.Name, prop.FieldType);
                    };

                    grid.Children.Add(tb);
                    grid.Children.Add(editBtn);
                    border.Child = grid;
                }
                else if (isBoolType)
                {
                    var cb = new CheckBox
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Avalonia.Thickness(12, 8, 12, 8)
                    };
                    // TwoWay: user click → row, undo → row → binding → checkbox
                    cb.Bind(CheckBox.IsCheckedProperty, new Binding
                    {
                        Path = $"[{prop.Name}]",
                        Mode = BindingMode.TwoWay,
                        Converter = new DynamicRowBoolConverter()
                    });

                    // Undo tracking for bool cells (they never enter DataGrid edit mode).
                    // PointerPressed captures the old value just before the toggle; only
                    // changes triggered by that press will carry the flag into IsCheckedChanged,
                    // so binding-driven updates (from Undo/Redo) are silently ignored.
                    var boolOldValue = row[prop.Name];
                    var boolUserInteraction = false;

                    cb.PointerPressed += (_, _) =>
                    {
                        boolOldValue = row[prop.Name];
                        boolUserInteraction = true;
                    };
                    cb.IsCheckedChanged += (_, _) =>
                    {
                        if (!boolUserInteraction) return;
                        boolUserInteraction = false;

                        var newVal = (cb.IsChecked == true) ? "true" : "false";
                        if (newVal != boolOldValue && DataContext is WorkspaceViewModel vm2)
                            vm2.UndoRedo.Push(new EditCellAction(row, prop.Name, boolOldValue, newVal));
                    };

                    border.Child = cb;
                }
                else
                {
                    var tb = new TextBlock
                    {
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Avalonia.Thickness(12, 8, 12, 8),
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxLines = 3
                    };
                    // Bind directly to the indexer path so the TextBlock reacts to
                    // PropertyChanged("Item[]") that DynamicDataRow fires on every write,
                    // including writes from EditCellAction.Undo() / .Redo().
                    tb.Bind(TextBlockHelper.OriginalTextProperty, new Binding($"[{prop.Name}]"));
                    tb.Bind(TextBlockHelper.HighlightTextProperty,
                        new Binding("DataContext.SearchQuery")
                        {
                            RelativeSource = new RelativeSource
                                { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                        });
                    border.Child = tb;
                }

                return border;
            });

            // ---- editing template ----
            if (!isComplexType)
            {
                column.CellEditingTemplate = new FuncDataTemplate<DynamicDataRow>((row, _) =>
                {
                    // Snapshot the old value the moment the editing template is instantiated.
                    // CellEditEnded will compare against this to decide whether to push an action.
                    _pendingEdit = (row, prop.Name, row[prop.Name]);

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
                            AcceptsReturn = true,
                            Text = row[prop.Name]
                        };
                        tb.TextChanged += (_, _) => row[prop.Name] = tb.Text ?? string.Empty;
                        return tb;
                    }
                });
            }
            else
            {
                column.IsReadOnly = true;
            }

            DataGrid.Columns.Add(column);
        }
    }
}
