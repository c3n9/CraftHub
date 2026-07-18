using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CraftHub.Converters;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.ViewModels;
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
    private ScrollViewer? _jsonTextBoxScrollViewer;
    private TextBlock? _lineNumbersBlock;
    private Button? _jsonErrorButton;
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

        // Recompute toolbar overflow when the toolbar is resized.
        ToolbarRoot.SizeChanged += (_, _) => ScheduleOverflowUpdate();

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

    //  JSON editor — line numbers + scroll sync

    private void InitJsonEditor()
    {
        _jsonTextBox        = this.FindControl<TextBox>("JsonTextBox");
        _lineNumberScroller = this.FindControl<ScrollViewer>("LineNumberScroller");
        _lineNumbersBlock   = this.FindControl<TextBlock>("LineNumbersBlock");
        _jsonErrorButton    = this.FindControl<Button>("JsonErrorButton");

        if (_jsonTextBox == null || _lineNumbersBlock == null) return;

        _jsonTextBox.TextChanged += (_, _) => RefreshLineNumbers();

        if (_jsonErrorButton != null)
            _jsonErrorButton.Click += OnErrorButtonClick;

        // After the TextBox template is applied its internal ScrollViewer exists.
        // Post to Background so the visual tree is fully ready before we search it.
        _jsonTextBox.TemplateApplied += (_, _) =>
            Dispatcher.UIThread.Post(HookScrollSync, DispatcherPriority.Background);
    }

    private void OnErrorButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (vm.JsonEditorErrorLine < 0 || _jsonTextBox == null) return;
        NavigateToLine((int)vm.JsonEditorErrorLine);
    }

    private void NavigateToLine(int lineIndex)
    {
        if (_jsonTextBox == null) return;
        var text = _jsonTextBox.Text ?? string.Empty;

        int offset = 0;
        int currentLine = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (currentLine == lineIndex) { offset = i; break; }
            if (text[i] == '\n') currentLine++;
        }

        _jsonTextBox.Focus();
        _jsonTextBox.CaretIndex = offset;

        if (_jsonTextBoxScrollViewer != null)
        {
            // FontSize=13, top padding=12; Avalonia line height ≈ FontSize * 1.5
            var lineHeight = _jsonTextBox.FontSize * 1.5;
            var targetY = 12.0 + lineIndex * lineHeight;
            var viewportHeight = _jsonTextBoxScrollViewer.Viewport.Height;
            _jsonTextBoxScrollViewer.Offset = new Vector(0, Math.Max(0, targetY - viewportHeight / 2));
        }
    }

    private void HookScrollSync()
    {
        if (_jsonTextBox == null || _lineNumberScroller == null) return;
        var sv = _jsonTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (sv == null) return;
        _jsonTextBoxScrollViewer = sv;
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

    //  Row-number header

    private void OnDataGridLoadingRow(object? sender, DataGridRowEventArgs e)
        => e.Row.Header = (e.Row.Index + 1).ToString();

    //  Selection → status bar

    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm)
            vm.SelectedRowsCount = DataGrid.SelectedItems?.Count ?? 0;
    }

    //  Cell edit tracking for undo / clipboard guard

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


    //  DataContext / column wiring

    private WorkspaceViewModel? _currentVm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.ColumnsChanged -= OnColumnsChanged;
            _currentVm.Properties.CollectionChanged -= OnPropertiesChanged;
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
        }

        if (DataContext is WorkspaceViewModel vm)
        {
            _currentVm = vm;
            vm.ColumnsChanged += OnColumnsChanged;
            vm.Properties.CollectionChanged += OnPropertiesChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
            RebuildColumns(vm);
            ScheduleOverflowUpdate();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Switching between table / JSON mode changes which buttons are shown,
        // so the overflow split must be recomputed.
        if (e.PropertyName is nameof(WorkspaceViewModel.IsJsonEditorMode)
            or nameof(WorkspaceViewModel.IsTableEditorMode))
            ScheduleOverflowUpdate();
    }

    //  Responsive toolbar: move the import/export buttons that do not fit into
    //  a "…" overflow menu, one at a time. The menu is shown only when needed.

    private bool _overflowUpdateQueued;
    private readonly System.Collections.Generic.Dictionary<Control, double> _naturalWidths = new();

    private void ScheduleOverflowUpdate()
    {
        if (_overflowUpdateQueued) return;
        _overflowUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _overflowUpdateQueued = false;
            UpdateToolbarOverflow();
        }, DispatcherPriority.Background);
    }

    private void UpdateToolbarOverflow()
    {
        if (ToolbarRoot == null || ToolbarButtons == null || OverflowButton == null || LeadSep == null ||
            BtnImportJson == null || BtnExportJson == null || BtnImportClass == null || BtnExportClass == null)
            return;

        // Display order; overflow priority runs from the end (Export Class overflows first).
        var candidates = new Button[] { BtnImportJson, BtnExportJson, BtnImportClass, BtnExportClass };
        var menus = new MenuItem[] { MiImportJson, MiExportJson, MiImportClass, MiExportClass };
        const double spacing = 4;

        // Cache the natural width of anything currently laid out so hidden items can still be sized.
        // DesiredSize includes margins (separators carry 6px on each side), so it is more accurate
        // than Bounds for deciding what fits.
        foreach (var child in ToolbarButtons.Children)
            if (child is Control ctl && ctl.IsVisible && ctl.DesiredSize.Width > 0)
                _naturalWidths[ctl] = ctl.DesiredSize.Width;
        if (OverflowButton.DesiredSize.Width > 0)
            _naturalWidths[OverflowButton] = OverflowButton.DesiredSize.Width;

        double Natural(Control c) =>
            _naturalWidths.TryGetValue(c, out var w) ? w : c.DesiredSize.Width;

        var rootWidth = ToolbarRoot.Bounds.Width;
        if (rootWidth <= 0) return;

        // Total width required if every candidate were visible.
        double total = 0;
        foreach (var child in ToolbarButtons.Children)
        {
            if (child is not Control ctl) continue;
            var isCandidate = Array.IndexOf(candidates, ctl) >= 0;
            if (!isCandidate && !ctl.IsVisible) continue; // skip mode-hidden controls
            total += Natural(ctl) + spacing;
        }

        // Reserve room for the … button plus a small safety pad so buttons never touch it.
        var overflowWidth = (_naturalWidths.TryGetValue(OverflowButton, out var ow) ? ow : 44) + 8;

        var toHide = new System.Collections.Generic.HashSet<Control>();
        if (total > rootWidth)
        {
            var available = rootWidth - overflowWidth;
            var running = total;
            for (var i = candidates.Length - 1; i >= 0 && running > available; i--)
            {
                toHide.Add(candidates[i]);
                running -= Natural(candidates[i]) + spacing;
            }
        }

        for (var i = 0; i < candidates.Length; i++)
        {
            var hide = toHide.Contains(candidates[i]);
            candidates[i].IsVisible = !hide;
            menus[i].IsVisible = hide;
        }

        // Hide the leading separator only when every candidate has overflowed.
        LeadSep.IsVisible = toHide.Count < candidates.Length;
        OverflowButton.IsVisible = toHide.Count > 0;
    }

    private void OnColumnsChanged(object? sender, EventArgs e)
    {
        // ColumnsChanged is a coarse "schema changed" signal, also fired right after an
        // add/remove that Properties.CollectionChanged already handled incrementally.
        // Only do the (expensive) full rebuild when the columns actually diverged from the
        // schema — e.g. a rename or a type change that has no CollectionChanged event.
        if (_currentVm != null && !ColumnsMatch(_currentVm))
            RebuildColumns(_currentVm);
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
                DataGrid.Columns.Insert(
                    e.NewStartingIndex >= 0 ? e.NewStartingIndex : DataGrid.Columns.Count,
                    BuildColumn(prop));
                break;

            case NotifyCollectionChangedAction.Remove
                when e.OldStartingIndex >= 0 && e.OldStartingIndex < DataGrid.Columns.Count:
                DataGrid.Columns.RemoveAt(e.OldStartingIndex);
                break;

            default:
                // Reset (bulk load), Replace, Move — structure changed too much to patch.
                RebuildColumns(_currentVm);
                break;
        }
    }

    // True when the existing DataGrid columns already match the schema (same order, name and
    // header — header carries the type), so no rebuild is needed.
    private bool ColumnsMatch(WorkspaceViewModel vm)
    {
        if (DataGrid.Columns.Count != vm.Properties.Count) return false;
        for (var i = 0; i < vm.Properties.Count; i++)
        {
            var prop = vm.Properties[i];
            var col = DataGrid.Columns[i];
            if (col.Tag as string != prop.Name) return false;
            if (col.Header as string !=
                $"{prop.Name} ({JsonPropertyDefinition.GetTypeDisplayName(prop.FieldType)})")
                return false;
        }
        return true;
    }

    //  Column builder

    private void RebuildColumns(WorkspaceViewModel vm)
    {
        var savedWidths = new System.Collections.Generic.Dictionary<string, DataGridLength>();
        foreach (var col in DataGrid.Columns)
            if (col.Tag is string tag)
                savedWidths[tag] = col.Width;

        DataGrid.Columns.Clear();

        foreach (var prop in vm.Properties)
        {
            var column = BuildColumn(prop);

            if (savedWidths.TryGetValue(prop.Name, out var savedWidth))
                column.Width = savedWidth;

            DataGrid.Columns.Add(column);
        }
    }

    private DataGridTemplateColumn BuildColumn(JsonPropertyDefinition prop)
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
                // Все колонки (включая последнюю) подгоняются под содержимое (SizeToCells):
                // ширина фиксируется по самой широкой ячейке и не меняется при скролле.
                // '*' (Star) НЕ используется — иначе последняя колонка «прилипает» к правому
                // краю и её нельзя растянуть дальше. Без MaxWidth любую колонку можно тянуть
                // вправо сколько угодно; когда суммарная ширина превысит окно, DataGrid
                // покажет собственный горизонтальный скроллбар.
                Width = DataGridLength.SizeToCells,
                MinWidth = 100,
                MaxWidth = double.PositiveInfinity,
                IsReadOnly = false,
                SortMemberPath = $"[{prop.Name}]",
                CustomSortComparer = new DynamicRowComparer(prop.Name, prop.FieldType),
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
                // Compiled binding (not "[{name}]" string path) — tolerates any key; see issue #11.
                mb.Bindings.Add(DynamicRowCellBinding.ForKey(prop.Name));
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
                    cb.Bind(CheckBox.IsCheckedProperty,
                        DynamicRowCellBinding.ForKey(prop.Name, BindingMode.TwoWay, new DynamicRowBoolConverter()));

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
                        if (newVal != boolOldValue && DataContext is WorkspaceViewModel vm2)
                            vm2.UndoRedo.Push(new EditCheckBoxCellAction(row, prop.Name, boolOldValue, newVal));
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
                    // Bind to the row indexer via a compiled binding so the TextBlock
                    // reacts to PropertyChanged that DynamicDataRow fires on every write,
                    // including writes from EditCellAction.Undo() / .Redo().
                    tb.Bind(TextBlockHelper.OriginalTextProperty, DynamicRowCellBinding.ForKey(prop.Name));
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
                        cb.Bind(CheckBox.IsCheckedProperty,
                            DynamicRowCellBinding.ForKey(prop.Name, BindingMode.TwoWay, new DynamicRowBoolConverter()));
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

            return column;
    }
}
