using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Media;
using CraftHub.Helpers;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class JsonCompareWindow : Window
{
    private JsonCompareWindowViewModel? _vm;

    public JsonCompareWindow()
    {
        InitializeComponent();
        WindowGeometryHelper.Attach(this, "JsonCompareWindow");

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.Panels.CollectionChanged -= OnPanelsChanged;

        _vm = DataContext as JsonCompareWindowViewModel;

        if (_vm != null) _vm.Panels.CollectionChanged += OnPanelsChanged;

        RebuildPanels();
    }

    private void OnPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPanels();

    /// <summary>
    /// Floor for a pane's width. Set just above what the header actually needs (baseline badge,
    /// name, three buttons) so panes can be squeezed well down before the scroller kicks in.
    /// </summary>
    private const double MinPanelWidth = 170;

    private const double SplitterWidth = 6;

    /// <summary>
    /// Lays the panes out as star-sized grid columns with a splitter between each pair. Built here
    /// rather than with an ItemsControl because an items panel can't produce the interleaved
    /// splitter columns that make the panes resizable.
    /// </summary>
    private void RebuildPanels()
    {
        PanelsGrid.Children.Clear();
        PanelsGrid.ColumnDefinitions.Clear();

        if (_vm == null) return;

        // Star columns alone would keep shrinking the panes as more are added. Giving the grid a
        // minimum total width lets it stretch to fill a wide window, and overflow into the enclosing
        // ScrollViewer once the panes no longer fit.
        var count = _vm.Panels.Count;
        PanelsGrid.MinWidth = count * MinPanelWidth + Math.Max(0, count - 1) * SplitterWidth;

        foreach (var panel in _vm.Panels)
        {
            if (PanelsGrid.ColumnDefinitions.Count > 0)
            {
                PanelsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var splitter = new GridSplitter
                {
                    Width = SplitterWidth,
                    Background = Brushes.Transparent,
                    ResizeDirection = GridResizeDirection.Columns,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext
                };
                Grid.SetColumn(splitter, PanelsGrid.ColumnDefinitions.Count - 1);
                PanelsGrid.Children.Add(splitter);
            }

            // MinWidth stops a splitter drag from collapsing a pane to nothing — without it the
            // pane can be squeezed past the point where its toolbar is usable.
            PanelsGrid.ColumnDefinitions.Add(
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = MinPanelWidth });

            var view = new JsonComparePanelView { DataContext = panel };
            Grid.SetColumn(view, PanelsGrid.ColumnDefinitions.Count - 1);
            PanelsGrid.Children.Add(view);
        }
    }
}
