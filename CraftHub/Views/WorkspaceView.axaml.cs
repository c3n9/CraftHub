using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;
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

    private TextEditor? _jsonEditor;
    private Button? _jsonErrorButton;
    private Button? _jsonFindButton;

    // Guards the two-way sync between the editor and WorkspaceViewModel.RawJsonText
    // so an echo from one side does not bounce back and re-trigger the other.
    private bool _suppressEditorSync;

    // JSON highlighting is loaded once from the bundled .xshd and shared by every tab.
    private static IHighlightingDefinition? _jsonHighlighting;

    // Column pin icon + header background, keyed by column, so their visual (pinned/unpinned)
    // state can be refreshed after a toggle or a reorder without rebuilding the whole header.
    private readonly System.Collections.Generic.Dictionary<DataGridColumn, (Material.Icons.Avalonia.MaterialIcon Icon, Border Header)> _pinIcons = new();

    public WorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DataGrid.LoadingRow       += OnDataGridLoadingRow;
        DataGrid.SelectionChanged  += OnDataGridSelectionChanged;
        DataGrid.BeginningEdit     += (_, _) => SetCellEditing(true);
        DataGrid.CellEditEnded     += OnDataGridCellEditEnded;
        DataGrid.ColumnReordered   += (_, _) => UpdatePinIconStates();
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

    //  JSON editor (AvaloniaEdit)

    private void InitJsonEditor()
    {
        _jsonEditor      = this.FindControl<TextEditor>("JsonEditor");
        _jsonErrorButton = this.FindControl<Button>("JsonErrorButton");
        _jsonFindButton  = this.FindControl<Button>("JsonFindButton");

        if (_jsonEditor != null)
        {
            _jsonEditor.SyntaxHighlighting = GetJsonHighlighting();
            _jsonEditor.Options.IndentationSize = 2;
            _jsonEditor.TextChanged += OnEditorTextChanged;
            // Handle Ctrl+F / Ctrl+H ourselves (Tunnel, so before AvaloniaEdit's built-in) to make
            // them toggle the search panel — pressing the same combo again closes it.
            _jsonEditor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        }

        if (_jsonErrorButton != null)
            _jsonErrorButton.Click += OnErrorButtonClick;

        if (_jsonFindButton != null)
            _jsonFindButton.Click += (_, _) => ToggleSearchPanel(replace: false);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;

        if (e.Key == Key.F)
        {
            ToggleSearchPanel(replace: false);
            e.Handled = true;
        }
        else if (e.Key == Key.H)
        {
            ToggleSearchPanel(replace: true);
            e.Handled = true;
        }
    }

    // Opens the search panel in the requested mode, switches mode if already open in the other one,
    // or closes it when the same combo is pressed again.
    private void ToggleSearchPanel(bool replace)
    {
        if (_jsonEditor?.SearchPanel is not { } panel) return;

        if (panel.IsOpened && panel.IsReplaceMode == replace)
        {
            panel.Close();
        }
        else
        {
            panel.Open();
            panel.IsReplaceMode = replace;
        }
    }

    private static IHighlightingDefinition? GetJsonHighlighting()
    {
        if (_jsonHighlighting != null) return _jsonHighlighting;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CraftHub/Resources/JsonHighlighting.xshd"));
            using var reader = XmlReader.Create(stream);
            _jsonHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            // Fall back to AvaloniaEdit's built-in JSON definition if the bundled one fails to load.
            _jsonHighlighting = HighlightingManager.Instance.GetDefinition("Json");
        }
        return _jsonHighlighting;
    }

    // Editor -> view-model
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorSync || _currentVm == null || _jsonEditor == null) return;
        _suppressEditorSync = true;
        _currentVm.RawJsonText = _jsonEditor.Text;
        _suppressEditorSync = false;
    }

    // View-model -> editor
    private void PushTextToEditor(string text)
    {
        if (_jsonEditor == null || _suppressEditorSync || _jsonEditor.Text == text) return;
        _suppressEditorSync = true;
        _jsonEditor.Text = text ?? string.Empty;
        _suppressEditorSync = false;
    }

    private void OnErrorButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || vm.JsonEditorErrorLine < 0) return;
        NavigateToLine((int)vm.JsonEditorErrorLine);
    }

    // JsonException.LineNumber is 0-based; AvaloniaEdit lines are 1-based.
    private void NavigateToLine(int lineIndex)
    {
        if (_jsonEditor?.Document is not { } doc) return;
        var line = Math.Clamp(lineIndex + 1, 1, doc.LineCount);
        _jsonEditor.ScrollToLine(line);
        _jsonEditor.CaretOffset = doc.GetLineByNumber(line).Offset;
        _jsonEditor.TextArea.Focus();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        FindNextMatch();
    }

    private void OnFocusSearchRequested(object? sender, EventArgs e)
    {
        if (_currentVm is not { IsTableEditorMode: true }) return;

        // While the replace flyout is open it visually covers SearchBox, so focus its own
        // find box instead of the one hidden underneath.
        var isReplaceOpen = FlyoutBase.GetAttachedFlyout(SearchBox) is { IsOpen: true };
        var target = isReplaceOpen ? FlyoutFindBox : SearchBox;
        target.Focus();
        target.SelectAll();
    }

    private void OnToggleReplaceRequested(object? sender, EventArgs e)
    {
        if (_currentVm is not { IsTableEditorMode: true }) return;
        ToggleReplaceFlyout();
    }

    private void OnReplaceButtonClick(object? sender, RoutedEventArgs e) => ToggleReplaceFlyout();

    private void OnReplaceFlyoutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.H))
        {
            ToggleReplaceFlyout();
            e.Handled = true;
        }
    }

    // Anchored to SearchBox (see FlyoutBase.AttachedFlyout in the XAML) so it visually unfolds
    // from the search field itself rather than the small trigger button next to it.
    private void ToggleReplaceFlyout()
    {
        // Flyout isn't a Control, so x:Name on it doesn't generate a field — fetch the instance
        // via the same attached property it was declared with instead.
        if (FlyoutBase.GetAttachedFlyout(SearchBox) is { IsOpen: true } flyout)
        {
            flyout.Hide();
            // Move focus off the (now hidden) replace box instead of leaving it stranded there.
            SearchBox.Focus();
            return;
        }

        FlyoutBase.ShowAttachedFlyout(SearchBox);
        // The flyout's content isn't in the visual tree yet on this same tick, so focusing the
        // box has to wait one dispatcher pass for the popup to actually open.
        Dispatcher.UIThread.Post(() =>
        {
            ReplaceBox.Focus();
            ReplaceBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnReplaceBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnReplaceCurrentClick(sender, e);
    }

    private void OnHistoryEntryClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is not { } vm) return;
        if (sender is not Button { DataContext: HistoryEntry entry }) return;
        vm.UndoRedo.JumpTo(entry.Index);
    }

    private static bool RowMatchesQuery(DynamicDataRow row, IEnumerable<JsonPropertyDefinition> props, string query) =>
        props.Any(p => (row[p.Name] ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));

    private void FindNextMatch()
    {
        if (_currentVm is not { } vm || string.IsNullOrWhiteSpace(vm.SearchQuery)) return;

        var rows = vm.Rows;
        var props = vm.Properties;
        if (rows.Count == 0 || props.Count == 0) return;

        var query = vm.SearchQuery;
        var startIndex = vm.SelectedRow != null ? rows.IndexOf(vm.SelectedRow) : -1;

        for (var offset = 1; offset <= rows.Count; offset++)
        {
            var idx = (startIndex + offset) % rows.Count;
            if (!RowMatchesQuery(rows[idx], props, query)) continue;

            vm.SelectedRow = rows[idx];
            DataGrid.ScrollIntoView(rows[idx], null);
            return;
        }
    }

    // Replaces in the currently matched row, then advances to the next match — same "row is a
    // match" granularity as search/highlight. If nothing is currently on a match, jump to one first.
    private void OnReplaceCurrentClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is not { } vm || string.IsNullOrWhiteSpace(vm.SearchQuery)) return;

        if (vm.SelectedRow == null || !RowMatchesQuery(vm.SelectedRow, vm.Properties, vm.SearchQuery))
            FindNextMatch();

        vm.ReplaceInSelectedRow();
        FindNextMatch();
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
            _currentVm.FocusSearchRequested -= OnFocusSearchRequested;
            _currentVm.ToggleReplaceRequested -= OnToggleReplaceRequested;
        }

        if (DataContext is WorkspaceViewModel vm)
        {
            _currentVm = vm;
            vm.ColumnsChanged += OnColumnsChanged;
            vm.Properties.CollectionChanged += OnPropertiesChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.FocusSearchRequested += OnFocusSearchRequested;
            vm.ToggleReplaceRequested += OnToggleReplaceRequested;
            RebuildColumns(vm);
            PushTextToEditor(vm.RawJsonText); // seed the editor for this tab
            ScheduleOverflowUpdate();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Keep the editor in sync when the raw JSON is changed by the view-model
        // (entering JSON mode, prettify/minify, import).
        if (e.PropertyName == nameof(WorkspaceViewModel.RawJsonText))
            PushTextToEditor(_currentVm?.RawJsonText ?? string.Empty);

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
                _pinIcons.Remove(DataGrid.Columns[e.OldStartingIndex]);
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
        _pinIcons.Clear();

        foreach (var prop in vm.Properties)
        {
            var column = BuildColumn(prop);

            if (savedWidths.TryGetValue(prop.Name, out var savedWidth))
                column.Width = savedWidth;

            DataGrid.Columns.Add(column);
        }

        UpdatePinIconStates();
    }

    // The DataGrid only supports freezing a contiguous block from the left (FrozenColumnCount),
    // not arbitrary individual columns. So pinning a column that isn't already adjacent to the
    // pinned block moves it there (DisplayIndex = right after the last pinned column) instead of
    // just widening the frozen block to include everything in between — otherwise pinning e.g.
    // column 5 with nothing else pinned would freeze columns 0-5 as one block, and if that block
    // is wider than the viewport the grid renders empty (frozen-columns rendering bug).
    private void ToggleColumnPin(DataGridColumn column)
    {
        var pinnedCount = DataGrid.FrozenColumnCount;
        var idx = column.DisplayIndex;
        if (idx < 0) return;

        if (idx < pinnedCount)
        {
            // Unpin just this column: move it to the end of the pinned block first so the
            // remaining pinned columns shift left and stay pinned, instead of the whole block
            // collapsing (previously unpinning the first pinned column unpinned everything).
            if (idx != pinnedCount - 1)
                column.DisplayIndex = pinnedCount - 1;
            DataGrid.FrozenColumnCount = pinnedCount - 1;
        }
        else
        {
            if (idx != pinnedCount)
                column.DisplayIndex = pinnedCount;
            DataGrid.FrozenColumnCount = pinnedCount + 1;
        }

        UpdatePinIconStates();
    }

    private void UpdatePinIconStates()
    {
        foreach (var (col, entry) in _pinIcons)
        {
            var pinned = col.DisplayIndex >= 0 && col.DisplayIndex < DataGrid.FrozenColumnCount;
            entry.Icon.Kind = pinned ? Material.Icons.MaterialIconKind.PinOutline : Material.Icons.MaterialIconKind.PinOffOutline;
            entry.Icon.Opacity = 1.0;

            // Resolved against the header Border itself (already attached to the visual tree),
            // not Application.Current — that's what makes it track the actually-rendered theme.
            var accentColor = entry.Header.TryFindResource("AccentPrimary", out var res) && res is ISolidColorBrush accent
                ? accent.Color
                : Colors.Gray;

            entry.Icon.Foreground = pinned ? new SolidColorBrush(accentColor) : Brushes.Gray;
            entry.Header.Background = pinned ? new SolidColorBrush(accentColor, 0x4A / 255.0) : Brushes.Transparent;
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

            column.HeaderTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                var headerBorder = new Border();
                // Pin button goes first (left) — frozen columns anchor to the viewport's left
                // edge, so the left side stays reachable regardless of column width/scroll,
                // unlike the right edge which can end up off-screen on a wide pinned column.
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                headerBorder.Child = grid;

                var text = new TextBlock
                {
                    Text = header,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    [!ToolTip.TipProperty] = new Binding { Source = header }
                };

                var pinIcon = new Material.Icons.Avalonia.MaterialIcon
                {
                    Kind = Material.Icons.MaterialIconKind.PinOffOutline,
                    Width = 13,
                    Height = 13,
                    Opacity = 0.45,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var pinBtn = new Button
                {
                    Content = pinIcon,
                    Padding = new Avalonia.Thickness(4, 2),
                    Margin = new Avalonia.Thickness(0, 0, 2, 0),
                    Background = Brushes.Transparent,
                    Cursor = Avalonia.Input.Cursor.Parse("Hand"),
                    [!ToolTip.TipProperty] = new Binding { Source = Localizer.Get("PinColumnTip") }
                };
                pinBtn.Click += (_, e) =>
                {
                    e.Handled = true; // don't let the click bubble into the header's own sort gesture
                    ToggleColumnPin(column);
                };

                _pinIcons[column] = (pinIcon, headerBorder);

                Grid.SetColumn(pinBtn, 0);
                Grid.SetColumn(text, 1);
                grid.Children.Add(pinBtn);
                grid.Children.Add(text);
                return headerBorder;
            });

            var isComplexType = prop.FieldType == JsonFieldType.Object || prop.FieldType == JsonFieldType.Array;
            var isBoolType    = prop.FieldType == JsonFieldType.Bool;

            // ---- read-only cell template ----
            column.CellTemplate = new FuncDataTemplate<DynamicDataRow>((row, _) =>
            {
                var border = new Border();
                var mb = new MultiBinding { Converter = new SearchHighlightConverter(column) };
                // Compiled binding (not "[{name}]" string path) — tolerates any key; see issue #11.
                mb.Bindings.Add(DynamicRowCellBinding.ForKey(prop.Name));
                mb.Bindings.Add(new Binding("DataContext.SearchQuery")
                {
                    RelativeSource = new RelativeSource
                        { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                });
                // Re-evaluates the pin tint whenever pinning changes, since FrozenColumnCount
                // is a bindable AvaloniaProperty on DataGrid.
                mb.Bindings.Add(new Binding(nameof(DataGrid.FrozenColumnCount))
                {
                    RelativeSource = new RelativeSource
                        { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                });
                // Resolved against this Border itself (not Application.Current), so it always
                // matches the theme actually rendered — see SearchHighlightConverter for why.
                mb.Bindings.Add(new DynamicResourceExtension("AccentPrimary"));
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
