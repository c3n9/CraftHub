using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;
using CraftHub.Converters;
using CraftHub.Formulas.Functions;
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
    // The text editor currently open in a cell, if any. CellEditEnded reads its text; nothing
    // writes to the row until then.
    //
    // This used to be a value snapshot plus a live `TextChanged -> row[prop] = text` binding, and
    // that combination was wrong in a way worth recording. A formula cell is edited as its FORMULA
    // while the row holds the formula's RESULT, so the live binding immediately wrote the formula
    // TEXT into the row; restoring the result then depended on the commit path noticing. It
    // couldn't reliably: a single snapshot slot is overwritten by the next cell's editing template
    // as soon as the user clicks straight from one cell to another, which happens before
    // CellEditEnded fires for the one they left — so the cell they left kept the formula text on
    // screen where its value belonged. Not writing to the row until commit removes the whole class
    // of failure: there is nothing to restore, and Escape is free.
    private (DynamicDataRow Row, string PropName, TextBox Box)? _activeCellEditor;

    private TextEditor? _jsonEditor;
    private Button? _jsonErrorButton;
    private Button? _jsonFindButton;


    // Guards the two-way sync between the editor and WorkspaceViewModel.RawJsonText
    // so an echo from one side does not bounce back and re-trigger the other.
    private bool _suppressEditorSync;

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
        DataGrid.CurrentCellChanged += OnDataGridCurrentCellChanged;
        // Tunnel (before the Ctrl+D KeyBinding's own Bubble-stage handling) so a formula cell's
        // Ctrl+D can be claimed for fill-down here, while every other Ctrl+D still falls through
        // unchanged to the pre-existing "duplicate rows after" shortcut.
        DataGrid.AddHandler(KeyDownEvent, OnDataGridKeyDownForFillDown, RoutingStrategies.Tunnel);
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
                UpdateColumnFormulaMenuItems();
                if (DataContext is WorkspaceViewModel vm)
                    await vm.RefreshClipboardStateAsync();
            };

        // Pointer-pressed, not Click: the formula bar commits on LostFocus, and a Click fires only
        // after focus has already moved — which would apply the typed formula to the single current
        // cell first, and then apply it again to the column. Handling the press keeps focus put.
        ApplyToColumnButton.AddHandler(PointerPressedEvent, (_, e) =>
        {
            e.Handled = true;
            ApplyFormulaBarTextToColumn();
        }, RoutingStrategies.Tunnel);

        // Moving the caret changes what the hint should say just as much as typing does — clicking
        // into an existing formula's parentheses is exactly when someone wants its signature.
        FormulaBarTextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty) UpdateFormulaSuggestions();
        };
    }

    // Which of the two column-formula entries makes sense depends on the column under the pointer,
    // which is only known once the menu is about to open.
    private void UpdateColumnFormulaMenuItems()
    {
        var columnKey = DataGrid.CurrentColumn?.Tag as string;
        var hasColumnFormula = _currentVm is { } vm && columnKey is not null && vm.ColumnHasFormula(columnKey);

        ApplyToColumnMenuItem.IsVisible = columnKey is not null;
        RemoveColumnFormulaMenuItem.IsVisible = hasColumnFormula;
    }

    //  JSON editor (AvaloniaEdit)

    private void InitJsonEditor()
    {
        _jsonEditor      = this.FindControl<TextEditor>("JsonEditor");
        _jsonErrorButton = this.FindControl<Button>("JsonErrorButton");
        _jsonFindButton  = this.FindControl<Button>("JsonFindButton");

        if (_jsonEditor != null)
        {
            _jsonEditor.Options.IndentationSize = 2;
            JsonHighlightingHelper.ApplySelectionColors(_jsonEditor);
            _jsonEditor.TextChanged += OnEditorTextChanged;
            // Handle Ctrl+F / Ctrl+H ourselves (Tunnel, so before AvaloniaEdit's built-in) to make
            // them toggle the search panel — pressing the same combo again closes it.
            _jsonEditor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
            ApplyJsonSyntaxTheme();
            _jsonEditor.AttachedToVisualTree += (_, _) =>
            {
                ApplyJsonSyntaxTheme();
                if (Application.Current != null)
                    Application.Current.ActualThemeVariantChanged += OnAppThemeVariantChanged;
            };
            _jsonEditor.DetachedFromVisualTree += (_, _) =>
            {
                if (Application.Current != null)
                    Application.Current.ActualThemeVariantChanged -= OnAppThemeVariantChanged;
            };
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

    private void OnAppThemeVariantChanged(object? sender, EventArgs e) => ApplyJsonSyntaxTheme();

    // ActualThemeVariant on Application.Current is what ThemeService itself drives
    // (app.RequestedThemeVariant), and is reliable to read directly — unlike resolving a *keyed
    // resource* via Application.Current.TryFindResource, which can pick the wrong theme
    // dictionary (see SearchHighlightConverter). _jsonEditor.ActualThemeVariant was tried first
    // but returned a stale/wrong value even after the control was attached.
    private void ApplyJsonSyntaxTheme()
    {
        if (_jsonEditor == null) return;
        _jsonEditor.SyntaxHighlighting = JsonHighlightingHelper.ForCurrentTheme();
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

    // Plain code-behind Click handler (not a bound Command) because it needs DataGrid.CurrentColumn
    // — which cell/column the user actually right-clicked on — and that isn't something a
    // ContextMenu's CommandParameter can express alongside SelectedItems.
    private void OnFillDownClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is not { } vm) return;
        if (DataGrid.CurrentColumn?.Tag is not string columnKey) return;
        var indices = SelectedRowIndicesInOrder(vm);
        if (indices.Count < 2) return;

        vm.FillDown(indices[0], columnKey, indices.Skip(1).ToList(), DataGrid);
    }

    // Ctrl+D already means "duplicate rows after" in this app (see the DataGrid.KeyBindings in
    // XAML) — that shortcut stays untouched for everything except this one case: multiple rows
    // selected, current column a formula at the TOP of the selection. There, Ctrl+D means what it
    // means in Excel — fill down — and this claims the key before the existing binding sees it.
    private void OnDataGridKeyDownForFillDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control || e.Key != Key.D) return;
        if (_currentVm is not { } vm) return;
        if (DataGrid.CurrentColumn?.Tag is not string columnKey) return;

        var indices = SelectedRowIndicesInOrder(vm);
        if (indices.Count < 2 || !vm.IsFormulaCell(indices[0], columnKey)) return;

        e.Handled = true;
        vm.FillDown(indices[0], columnKey, indices.Skip(1).ToList(), DataGrid);
    }

    //  Column formulas — one template computing every row of a column, including rows added later.
    //  Reachable three ways on purpose (button, Ctrl+Enter, context menu): the formula bar is where
    //  the text already is, the context menu is where the neighbouring Fill down lives, and the
    //  shortcut is what someone coming from Excel tries first.

    private void OnApplyToColumnClick(object? sender, RoutedEventArgs e) => ApplyFormulaBarTextToColumn();

    private void OnRemoveColumnFormulaClick(object? sender, RoutedEventArgs e)
    {
        if (_currentVm is not { } vm) return;
        if (DataGrid.CurrentColumn?.Tag is not string columnKey) return;

        vm.RemoveColumnFormula(columnKey, DataGrid);
        RefreshFormulaBar();
    }

    /// <summary>Applies whatever the formula bar currently holds to the whole current column. Reads
    /// the bar rather than the cell so a formula that's been typed but not yet committed is applied
    /// as typed — otherwise the button would need the user to press Enter first, which would have
    /// already committed it to the single cell.</summary>
    private void ApplyFormulaBarTextToColumn()
    {
        if (_currentVm is not { HasCurrentCell: true } vm) return;

        FormulaSuggestionsPopup.IsOpen = false;
        vm.CommitColumnFormula(vm.CurrentCellRowIndex, vm.CurrentCellColumnKey!, FormulaBarTextBox.Text ?? "", DataGrid);
        RefreshFormulaBar();
    }

    private List<int> SelectedRowIndicesInOrder(WorkspaceViewModel vm) =>
        DataGrid.SelectedItems is { Count: > 1 } selected
            ? selected.Cast<DynamicDataRow>().Select(r => vm.Rows.IndexOf(r)).Where(i => i >= 0).OrderBy(i => i).ToList()
            : new List<int>();

    // Corner "fx" marker for a formula cell, with the formula text (or the error explanation, if
    // this evaluation produced one) as its tooltip. Computed once when the cell template is built
    // — like the rest of the formula UI, it's refreshed by the same ItemsSource-reset trick
    // SetCellFormulaAction/FillDownAction/DetachFormulasAction already use after any formula edit,
    // not by a live binding.
    private Control BuildFormulaMarkerOverlay(DynamicDataRow row, string columnKey, TextBlock valueText)
    {
        if (_currentVm is not { } vm) return valueText;

        var rowIndex = vm.Rows.IndexOf(row);
        if (rowIndex < 0 || !vm.IsFormulaCell(rowIndex, columnKey)) return valueText;

        var errorState = vm.GetFormulaErrorState(rowIndex, columnKey);
        var formulaText = vm.GetDisplayFormula(rowIndex, columnKey) ?? "";

        // A cell can be a formula two ways, and the difference is worth showing: its own formula
        // (editing it affects this cell) or the column's template (editing it here overrides the
        // column for this one row). Same distinction the marker's own tooltip spells out.
        var fromColumn = vm.ColumnHasFormula(columnKey) && !vm.CellHasOwnFormula(rowIndex, columnKey);

        var tooltip = fromColumn
            ? $"{formulaText}\n\n{Localizer.Get("FormulaFromColumnTip")}"
            : formulaText;
        if (errorState != null) tooltip = $"{tooltip}\n\n{errorState.ErrorCode}: {errorState.Message}";

        var marker = new Material.Icons.Avalonia.MaterialIcon
        {
            Kind = fromColumn ? Material.Icons.MaterialIconKind.TableColumn : Material.Icons.MaterialIconKind.FunctionVariant,
            Width = 11,
            Height = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 3, 3, 0),
            Foreground = errorState != null ? Brushes.OrangeRed : Brushes.Gray,
            Opacity = errorState != null ? 1.0 : 0.6,
            [!ToolTip.TipProperty] = new Binding { Source = tooltip }
        };

        // No fill handle on a column-computed cell: dragging it would write per-cell copies of a
        // formula the column already applies to every row, and those copies would then shadow the
        // template — silently un-doing the "and rows added later" part of a calculated column.
        if (fromColumn)
        {
            var columnGrid = new Grid();
            columnGrid.Children.Add(valueText);
            columnGrid.Children.Add(marker);
            return columnGrid;
        }

        var grid = new Grid();
        grid.Children.Add(valueText);
        grid.Children.Add(marker);
        grid.Children.Add(BuildFillHandle(row, columnKey));
        return grid;
    }

    /// <summary>The Excel fill handle: the little square at the active cell's bottom-right corner
    /// that you drag down to copy this cell's formula into the rows you drag over.
    ///
    /// Visibility is a live binding on "is this the current cell", the same signal the active-cell
    /// border already uses — so exactly one handle exists at a time and it follows the selection
    /// without the grid being rebuilt. (It used to be drawn on every formula cell at once, purely
    /// because template-build time doesn't know the selection; that was the wrong trade.)</summary>
    private Control BuildFillHandle(DynamicDataRow row, string columnKey)
    {
        var handle = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new Avalonia.CornerRadius(1),
            Background = Brushes.SteelBlue,
            BorderBrush = Brushes.White,
            BorderThickness = new Avalonia.Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            // Half outside the cell's corner, as in a spreadsheet — that's what makes it a grab
            // target rather than something you hit by accident while clicking the cell.
            Margin = new Avalonia.Thickness(0, 0, -3, -3),
            Cursor = new Cursor(StandardCursorType.Cross),
            IsVisible = false,
            [!ToolTip.TipProperty] = new Binding { Source = Localizer.Get("FillHandleTip") }
        };

        if (_currentVm is { } vm)
        {
            var mb = new MultiBinding { Converter = new IsCurrentCellConverter(vm.Rows, row, columnKey) };
            mb.Bindings.Add(new Binding("DataContext.CurrentCellRowIndex")
            {
                RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
            });
            mb.Bindings.Add(new Binding("DataContext.CurrentCellColumnKey")
            {
                RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
            });
            handle.Bind(Visual.IsVisibleProperty, mb);
        }

        handle.AddHandler(PointerPressedEvent, (sender, e) => OnFillHandlePressed(sender, e, row, columnKey));
        handle.AddHandler(PointerMovedEvent, OnFillHandleMoved);
        handle.AddHandler(PointerReleasedEvent, OnFillHandleReleased);
        return handle;
    }

    // Live state for a fill drag. The source is held as the row OBJECT, not its index: the drag
    // outlives any number of layout passes, and an index would go stale if anything reordered
    // underneath it.
    private DynamicDataRow? _fillDragSourceRow;
    private string? _fillDragColumnKey;
    private Border? _fillPreview;

    private void OnFillHandlePressed(object? sender, PointerPressedEventArgs e, DynamicDataRow sourceRow, string columnKey)
    {
        e.Handled = true; // don't let the DataGrid start its own selection-drag or cell edit
        if (sender is not IInputElement el) return;
        e.Pointer.Capture(el);
        _fillDragSourceRow = sourceRow;
        _fillDragColumnKey = columnKey;
    }

    // Draws the range the drop would fill, so the gesture shows its effect before committing to it.
    private void OnFillHandleMoved(object? sender, PointerEventArgs e)
    {
        if (_fillDragSourceRow is null || _currentVm is not { } vm) return;

        var sourceIndex = vm.Rows.IndexOf(_fillDragSourceRow);
        var targetIndex = FindRowIndexUnderPointer(e.GetPosition(DataGrid), vm);
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex == sourceIndex)
        {
            ClearFillPreview();
            return;
        }

        var sourceCell = FindRowVisual(vm.Rows[sourceIndex]);
        var targetCell = FindRowVisual(vm.Rows[targetIndex]);
        if (sourceCell is null || targetCell is null)
        {
            ClearFillPreview();
            return;
        }

        var sourceTop = sourceCell.TranslatePoint(default, DataGrid);
        var targetTop = targetCell.TranslatePoint(default, DataGrid);
        if (sourceTop is null || targetTop is null)
        {
            ClearFillPreview();
            return;
        }

        // Cover both drag directions: from whichever row is higher to the bottom of the lower one.
        var top = Math.Min(sourceTop.Value.Y, targetTop.Value.Y);
        var bottom = Math.Max(sourceTop.Value.Y + sourceCell.Bounds.Height,
                              targetTop.Value.Y + targetCell.Bounds.Height);

        _fillPreview ??= CreateFillPreview();
        Canvas.SetLeft(_fillPreview, 0);
        Canvas.SetTop(_fillPreview, top);
        _fillPreview.Width = Math.Max(0, DataGrid.Bounds.Width);
        _fillPreview.Height = Math.Max(0, bottom - top);
        _fillPreview.IsVisible = true;
    }

    private Border CreateFillPreview()
    {
        var preview = new Border
        {
            BorderBrush = Brushes.SteelBlue,
            BorderThickness = new Avalonia.Thickness(1.5),
            Background = new SolidColorBrush(Colors.SteelBlue, 0.12),
            IsHitTestVisible = false
        };
        FillPreviewLayer.Children.Add(preview);
        return preview;
    }

    private void ClearFillPreview()
    {
        if (_fillPreview != null) _fillPreview.IsVisible = false;
    }

    private void OnFillHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_fillDragSourceRow is null) return;
        e.Handled = true;
        e.Pointer.Capture(null);

        var sourceRowItem = _fillDragSourceRow;
        var columnKey = _fillDragColumnKey!;
        _fillDragSourceRow = null;
        _fillDragColumnKey = null;
        ClearFillPreview();

        if (_currentVm is not { } vm) return;

        var sourceRow = vm.Rows.IndexOf(sourceRowItem);
        var targetRow = FindRowIndexUnderPointer(e.GetPosition(DataGrid), vm);
        if (sourceRow < 0 || targetRow < 0 || targetRow == sourceRow) return;

        var from = Math.Min(sourceRow, targetRow);
        var to = Math.Max(sourceRow, targetRow);
        var targets = Enumerable.Range(from, to - from + 1).Where(r => r != sourceRow).ToList();
        vm.FillDown(sourceRow, columnKey, targets, DataGrid);
    }

    private DataGridRow? FindRowVisual(DynamicDataRow item) =>
        DataGrid.GetVisualDescendants()
            .OfType<DataGridRow>()
            .FirstOrDefault(r => ReferenceEquals(r.DataContext, item));

    private int FindRowIndexUnderPointer(Point position, WorkspaceViewModel vm)
    {
        var hit = DataGrid.InputHitTest(position);
        var visual = hit as Visual;
        while (visual != null && visual is not DataGridRow)
            visual = visual.GetVisualParent();
        return visual is DataGridRow { DataContext: DynamicDataRow row } ? vm.Rows.IndexOf(row) : -1;
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
        RefreshCurrentCell();
    }

    //  Cell edit tracking for undo / clipboard guard

    private void SetCellEditing(bool value)
    {
        if (DataContext is WorkspaceViewModel vm)
            vm.IsCellEditing = value;
    }

    private void OnDataGridCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        SetCellEditing(false);

        var editor = _activeCellEditor;
        _activeCellEditor = null;

        // Escape needs no undoing: the row was never touched while editing.
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (editor is not { } ed) return;

        // The editor field is a single slot, and the next cell's template can claim it before this
        // fires. Committing it against the wrong cell would be worse than dropping the edit, so
        // check it really is the cell that just closed.
        if (e.Column?.Tag as string != ed.PropName) return;
        if (!ReferenceEquals(e.Row?.DataContext, ed.Row)) return;

        if (DataContext is not WorkspaceViewModel vm) return;

        var rowIndex = vm.Rows.IndexOf(ed.Row);
        if (rowIndex < 0) return;

        // Read straight off the row: nothing has written to it, so this is genuinely "before".
        // Any resulting change in formula state comes back as FormulaVisualsChanged.
        vm.CommitCellEdit(rowIndex, ed.PropName, ed.Row[ed.PropName], ed.Row.GetKind(ed.PropName),
            ed.Box.Text ?? string.Empty, DataGrid);
    }

    private void OnFormulaVisualsChanged(object? sender, EventArgs e) => RefreshGridRows();

    private void RefreshGridRows()
    {
        DataGridRefresh.Rows(DataGrid);
        RefreshCurrentCell();
    }

    //  Formula bar — tracks the DataGrid's own "current cell" concept (column-level, unlike
    //  SelectedItem/SelectedRowsCount) so the bar always shows/edits whatever cell has focus.

    // Fired from both DataGrid.CurrentCellChanged and DataGrid.SelectionChanged — deliberately
    // redundant, since which one actually reflects "the cell the user just clicked" first isn't
    // documented/guaranteed, and missing the update entirely would leave the formula bar showing
    // the wrong cell's content with no visible sign anything was wrong.
    private void OnDataGridCurrentCellChanged(object? sender, EventArgs e) => RefreshCurrentCell();

    private void RefreshCurrentCell()
    {
        if (_currentVm is not { } vm) return;

        var columnKey = DataGrid.CurrentColumn?.Tag as string;
        var row = DataGrid.SelectedItem as DynamicDataRow;
        var rowIndex = row is not null ? vm.Rows.IndexOf(row) : -1;
        var hasCell = rowIndex >= 0 && columnKey is not null;

        vm.CurrentCellRowIndex = hasCell ? rowIndex : -1;
        vm.CurrentCellColumnKey = hasCell ? columnKey : null;

        RefreshFormulaBar();
    }

    private void RefreshFormulaBar()
    {
        FormulaBarTextBox.Text = _currentVm is { HasCurrentCell: true } vm ? vm.CurrentCellText : "";
        FormulaSuggestionsPopup.IsOpen = false;
    }

    // Enter and losing focus both commit (matches Excel's formula bar); Escape reverts the
    // displayed text without touching the cell. CommitCellEdit's own "no real change" guard makes
    // committing on every focus loss safe — clicking away without editing anything is a no-op.
    private void CommitFormulaBarEdit()
    {
        if (_currentVm is not { HasCurrentCell: true } vm) return;

        var rowIndex = vm.CurrentCellRowIndex;
        var columnKey = vm.CurrentCellColumnKey!;
        var row = vm.Rows[rowIndex];
        // Untouched by the formula bar itself (unlike the grid's own cell editor, this TextBox
        // isn't live-bound into the row), so the row still holds the true pre-edit state here.
        var oldValue = row[columnKey];
        var oldKind = row.GetKind(columnKey);

        vm.CommitCellEdit(rowIndex, columnKey, oldValue, oldKind, FormulaBarTextBox.Text ?? "", DataGrid);
    }

    private void OnFormulaBarKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Enter is "apply to the whole column", never "accept this suggestion".
        if (FormulaSuggestionsPopup.IsOpen && _formulaSuggestions.Count > 0
            && e.Key is Key.Tab or Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            AcceptFormulaSuggestion(_formulaSuggestions[0]);
            return;
        }

        if (FormulaSuggestionsPopup.IsOpen && e.Key == Key.Escape)
        {
            e.Handled = true;
            FormulaSuggestionsPopup.IsOpen = false;
            return;
        }

        // Ctrl+Enter applies to the whole column — Excel's own gesture for "commit this to more
        // than the one cell", so it's the first thing a spreadsheet user tries.
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            ApplyFormulaBarTextToColumn();
            DataGrid.Focus();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitFormulaBarEdit();
            RefreshFormulaBar();
            DataGrid.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RefreshFormulaBar();
            DataGrid.Focus();
        }
    }

    private void OnFormulaBarLostFocus(object? sender, RoutedEventArgs e) => CommitFormulaBarEdit();

    //  Formula autocomplete — formula bar only (not the grid's own inline cell editor).
    //
    //  Three things happen in the one popup, chosen by what's immediately before the caret:
    //  completing a function name, completing a COLUMN name inside [ ] or @[ ], and — when there's
    //  nothing to complete but the caret sits inside a call's parentheses — showing that function's
    //  signature with the argument being typed picked out. Column completion matters at least as
    //  much as the function list here: unlike Excel's A1 grid, this table's columns are named
    //  fields, so `@[unitPrice]` is the reference people actually write, and getting it wrong by a
    //  character is the most likely way to write a formula that doesn't work.
    //
    //  None of this parses the formula for real, so a function-shaped word or a '[' inside a string
    //  literal can still trigger it. That trade-off is unchanged from the original function-only
    //  version and is worth the simplicity given how rarely it happens in practice.

    private static readonly FunctionRegistry FormulaHintsRegistry = FunctionRegistry.CreateStandard();

    /// <summary>One offered completion. <see cref="InsertText"/> is what replaces the token being
    /// typed; <see cref="Display"/>/<see cref="Detail"/> are what the row shows.</summary>
    private sealed record FormulaSuggestion(string InsertText, string Display, string Detail);

    private enum CompletionKind { None, Function, Column }

    private List<FormulaSuggestion> _formulaSuggestions = new();
    private CompletionKind _completionKind = CompletionKind.None;
    private int _completionStart;

    private void OnFormulaBarTextChanged(object? sender, TextChangedEventArgs e) => UpdateFormulaSuggestions();

    private void UpdateFormulaSuggestions()
    {
        // Selecting a different cell rewrites the bar's text and moves its caret; without this the
        // popup would pop open over a bar nobody is typing in.
        if (!FormulaBarTextBox.IsFocused)
        {
            FormulaSuggestionsPopup.IsOpen = false;
            return;
        }

        var text = FormulaBarTextBox.Text ?? "";
        var caret = Math.Clamp(FormulaBarTextBox.CaretIndex, 0, text.Length);

        (_completionKind, _completionStart) = FindCompletionTarget(text, caret);
        var prefix = _completionKind == CompletionKind.None ? "" : text[_completionStart..caret];

        _formulaSuggestions = _completionKind switch
        {
            CompletionKind.Function => FormulaHintsRegistry.All()
                .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Take(8)
                .Select(ToSuggestion)
                .ToList(),

            CompletionKind.Column => ColumnKeys()
                .Where(k => JsonPropertyDefinition.GetDisplayPath(k).Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Take(8)
                .Select(k => new FormulaSuggestion(k, JsonPropertyDefinition.GetDisplayPath(k),
                    Localizer.Get("FormulaColumnSuggestionDetail")))
                .ToList(),

            _ => new List<FormulaSuggestion>()
        };

        FormulaSuggestionsList.Children.Clear();

        if (_formulaSuggestions.Count > 0)
        {
            foreach (var suggestion in _formulaSuggestions)
                FormulaSuggestionsList.Children.Add(BuildSuggestionRow(suggestion));
        }
        else if (FindEnclosingCall(text, caret) is ({ } meta, var activeArg))
        {
            FormulaSuggestionsList.Children.Add(BuildSignatureHint(meta, activeArg));
        }

        FormulaSuggestionsPopup.IsOpen = FormulaSuggestionsList.Children.Count > 0;
    }

    private IEnumerable<string> ColumnKeys() =>
        _currentVm?.Properties.Select(p => p.Name) ?? Enumerable.Empty<string>();

    private static FormulaSuggestion ToSuggestion(FunctionMetadata meta)
    {
        var argsText = string.Join(", ", meta.Arguments.Select(a => a.Repeating ? a.Name + "…" : a.Name));
        var detail = meta.Volatile
            ? $"{meta.Description} {Localizer.Get("FormulaVolatileNote")}"
            : meta.Description;
        return new FormulaSuggestion(meta.Name, $"{meta.Name}({argsText})", detail);
    }

    /// <summary>What the caret is in the middle of typing, and where that token starts. A column
    /// reference wins over a function name whenever there's an unclosed '[' before the caret —
    /// inside brackets, a run of letters is a field name, never a function.</summary>
    private static (CompletionKind Kind, int Start) FindCompletionTarget(string text, int caret)
    {
        if (!text.StartsWith('=')) return (CompletionKind.None, 0);

        var open = caret > 0 ? text.LastIndexOf('[', caret - 1) : -1;
        if (open >= 0 && text.IndexOf(']', open, caret - open) < 0)
            return (CompletionKind.Column, open + 1);

        var start = caret;
        while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
        return start == caret ? (CompletionKind.None, 0) : (CompletionKind.Function, start);
    }

    /// <summary>The call the caret sits inside, and which argument it's on — found by scanning back
    /// for an unmatched '(' and counting the commas at that same depth along the way.</summary>
    private static (FunctionMetadata? Meta, int ActiveArg) FindEnclosingCall(string text, int caret)
    {
        var depth = 0;
        var commas = 0;

        for (var i = Math.Min(caret, text.Length) - 1; i >= 0; i--)
        {
            switch (text[i])
            {
                case ')':
                    depth++;
                    break;

                case '(' when depth == 0:
                    var end = i;
                    var start = end;
                    while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
                    if (start == end) return (null, 0); // a grouping paren, not a call
                    return FormulaHintsRegistry.TryGetMetadata(text[start..end].ToUpperInvariant(), out var meta)
                        ? (meta, commas)
                        : (null, 0);

                case '(':
                    depth--;
                    break;

                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return (null, 0);
    }

    private Control BuildSuggestionRow(FormulaSuggestion suggestion)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = suggestion.Display,
                        FontFamily = new FontFamily("Cascadia Code,Cascadia Mono,Consolas,Menlo,Courier New,monospace"),
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 12
                    },
                    new TextBlock
                    {
                        Text = suggestion.Detail,
                        FontSize = 11,
                        Opacity = 0.65,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8, 4),
            Cursor = Cursor.Parse("Hand")
        };
        // Mouse-down (not Click) so the formula bar doesn't lose focus before the text swap runs.
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            e.Handled = true;
            AcceptFormulaSuggestion(suggestion);
        }, RoutingStrategies.Tunnel);
        return button;
    }

    // Not a Button: there's nothing to accept here, only something to read while typing arguments.
    private static Control BuildSignatureHint(FunctionMetadata meta, int activeArg)
    {
        var signature = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code,Cascadia Mono,Consolas,Menlo,Courier New,monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        signature.Inlines?.Add(new Run(meta.Name + "(") { FontWeight = FontWeight.SemiBold });

        for (var i = 0; i < meta.Arguments.Count; i++)
        {
            if (i > 0) signature.Inlines?.Add(new Run(", "));

            var arg = meta.Arguments[i];
            var label = arg.Optional ? $"[{arg.Name}]" : arg.Name;
            if (arg.Repeating) label += "…";

            // A repeating final argument stays highlighted however many values get typed into it.
            var isActive = i == activeArg || (arg.Repeating && i == meta.Arguments.Count - 1 && activeArg >= i);
            signature.Inlines?.Add(new Run(label)
            {
                FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal,
                TextDecorations = isActive ? TextDecorations.Underline : null
            });
        }
        signature.Inlines?.Add(new Run(")") { FontWeight = FontWeight.SemiBold });

        var activeSpec = activeArg < meta.Arguments.Count
            ? meta.Arguments[activeArg]
            : meta.Arguments.LastOrDefault(a => a.Repeating);

        return new StackPanel
        {
            Spacing = 1,
            Margin = new Avalonia.Thickness(8, 4),
            Children =
            {
                signature,
                new TextBlock
                {
                    Text = activeSpec?.Description ?? meta.Description,
                    FontSize = 11,
                    Opacity = 0.65,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    /// <summary>Where the token being completed ends, which is not always the caret: someone
    /// fixing a typo in the middle of `@[pri|ce]` or `SU|M(` means to replace the whole name, not
    /// to splice the completion in front of the rest of it.</summary>
    private int CompletionEnd(string text, int caret)
    {
        switch (_completionKind)
        {
            case CompletionKind.Column:
                var close = text.IndexOf(']', caret);
                return close >= 0 ? close : caret;

            case CompletionKind.Function:
                var end = caret;
                while (end < text.Length && char.IsAsciiLetter(text[end])) end++;
                return end;

            default:
                return caret;
        }
    }

    private void AcceptFormulaSuggestion(FormulaSuggestion suggestion)
    {
        var text = FormulaBarTextBox.Text ?? "";
        var caret = Math.Clamp(FormulaBarTextBox.CaretIndex, 0, text.Length);
        var start = Math.Clamp(_completionStart, 0, caret);
        var end = CompletionEnd(text, caret);

        // A function name brings its own '('; a column name closes its bracket unless one is
        // already sitting there (which it is whenever the user is editing an existing reference) —
        // in which case the caret still steps over it, so typing continues after the reference
        // rather than inside it.
        var bracketAlreadyThere = end < text.Length && text[end] == ']';
        var replacement = _completionKind switch
        {
            CompletionKind.Function => suggestion.InsertText + "(",
            CompletionKind.Column => bracketAlreadyThere ? suggestion.InsertText : suggestion.InsertText + "]",
            _ => suggestion.InsertText
        };
        var caretShift = _completionKind == CompletionKind.Column && bracketAlreadyThere ? 1 : 0;

        FormulaBarTextBox.Text = text[..start] + replacement + text[end..];
        FormulaBarTextBox.CaretIndex = start + replacement.Length + caretShift;

        FormulaSuggestionsPopup.IsOpen = false;
        FormulaBarTextBox.Focus();
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
            _currentVm.FormulaVisualsChanged -= OnFormulaVisualsChanged;
        }

        if (DataContext is WorkspaceViewModel vm)
        {
            _currentVm = vm;
            vm.ColumnsChanged += OnColumnsChanged;
            vm.Properties.CollectionChanged += OnPropertiesChanged;
            vm.PropertyChanged += OnVmPropertyChanged;
            vm.FocusSearchRequested += OnFocusSearchRequested;
            vm.ToggleReplaceRequested += OnToggleReplaceRequested;
            vm.FormulaVisualsChanged += OnFormulaVisualsChanged;
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

                // Excel-style "active cell" indicator: a live accent border on whichever cell
                // CurrentCellRowIndex/CurrentCellColumnKey currently point at (see
                // OnDataGridCurrentCellChanged). A genuine live binding, unlike the formula
                // marker/fill handle below — it tracks clicks in real time, no grid refresh needed.
                if (_currentVm is { } cellVm)
                {
                    border.BorderThickness = new Avalonia.Thickness(1.5);
                    var currentCellMb = new MultiBinding { Converter = new CurrentCellHighlightConverter(cellVm.Rows, row, prop.Name) };
                    currentCellMb.Bindings.Add(new Binding("DataContext.CurrentCellRowIndex")
                    {
                        RelativeSource = new RelativeSource
                            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                    });
                    currentCellMb.Bindings.Add(new Binding("DataContext.CurrentCellColumnKey")
                    {
                        RelativeSource = new RelativeSource
                            { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(DataGrid) }
                    });
                    currentCellMb.Bindings.Add(new DynamicResourceExtension("AccentPrimary"));
                    border.Bind(Border.BorderBrushProperty, currentCellMb);
                }

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
                    var boolOldKind = row.GetKind(prop.Name);
                    var boolUserInteraction = false;

                    cb.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
                    {
                        boolUserInteraction = true;
                        boolOldValue = row[prop.Name] == "true";
                        boolOldKind = row.GetKind(prop.Name);
                    }, RoutingStrategies.Tunnel);

                    cb.IsCheckedChanged += (_, _) =>
                    {
                        if (!boolUserInteraction) return;
                        boolUserInteraction = false;

                        var newVal = cb.IsChecked == true;
                        if (newVal != boolOldValue && DataContext is WorkspaceViewModel vm2)
                            vm2.UndoRedo.Push(new EditCheckBoxCellAction(row, prop.Name, boolOldValue, boolOldKind, newVal));
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

                    border.Child = BuildFormulaMarkerOverlay(row, prop.Name, tb);
                }

                return border;
            });

            // ---- editing template ----
            if (!isComplexType)
            {
                column.CellEditingTemplate = new FuncDataTemplate<DynamicDataRow>((row, _) =>
                {
                    // A formula cell edits its FORMULA text, not its computed result — matches
                    // what OnDataGridCellEditEnded expects back (a leading '=' means "still a
                    // formula"), and what gets typed here on a no-op edit-and-leave.
                    var displayText = row[prop.Name];
                    if (_currentVm is { } editVm)
                    {
                        var rowIndex = editVm.Rows.IndexOf(row);
                        if (rowIndex >= 0 && editVm.GetDisplayFormula(rowIndex, prop.Name) is { } formulaText)
                            displayText = formulaText;
                    }

                    if (isBoolType)
                    {
                        // A checkbox commits through its own two-way binding, and its undo entry is
                        // pushed by the display template's IsCheckedChanged — so there is no text to
                        // read back at CellEditEnded. Clearing the slot keeps a text editor left
                        // over from a previously edited cell from being mistaken for this one.
                        _activeCellEditor = null;

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
                            Text = displayText
                        };
                        // No live write into the row — CellEditEnded reads this box on commit.
                        // See _activeCellEditor for why that matters.
                        _activeCellEditor = (row, prop.Name, tb);
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
