using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class DiffView : UserControl
{
    private DiffViewModel? _vm;

    public DiffView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Minimap.PointerPressed += OnMinimapPressed;
        Minimap.SizeChanged += (_, _) => RedrawMinimap();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.ScrollToRowRequested -= OnScrollToRowRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as DiffViewModel;

        if (_vm != null)
        {
            _vm.ScrollToRowRequested += OnScrollToRowRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        RedrawMinimap();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiffViewModel.MinimapMarkers) or nameof(DiffViewModel.IsSideBySide))
            RedrawMinimap();
    }

    private void OnScrollToRowRequested(int rowIndex)
    {
        var list = _vm?.IsSideBySide == true ? SideBySideList : UnifiedList;
        if (list.ItemCount == 0) return;

        list.ScrollIntoView(Math.Clamp(rowIndex, 0, list.ItemCount - 1));
    }

    private void OnMinimapPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || Minimap.Bounds.Height <= 0) return;

        var y = e.GetPosition(Minimap).Y;
        _vm.ScrollToPosition(y / Minimap.Bounds.Height);
    }

    /// <summary>
    /// Markers carry a 0..1 position rather than pixels, so they're laid out here where the
    /// rendered height is known — and redrawn whenever that height or the marker set changes.
    /// </summary>
    private void RedrawMinimap()
    {
        Minimap.Children.Clear();

        var markers = _vm?.MinimapMarkers;
        var height = Minimap.Bounds.Height;
        if (markers == null || markers.Count == 0 || height <= 0) return;

        const double markerHeight = 3;
        var width = Math.Max(Minimap.Bounds.Width - 4, 2);

        foreach (var marker in markers)
        {
            var brushKey = marker.IsAdded ? "DiffGutterAdded"
                : marker.IsRemoved ? "DiffGutterRemoved"
                : "DiffGutterChanged";

            var rect = new Rectangle
            {
                Width = width,
                Height = markerHeight,
                RadiusX = 1,
                RadiusY = 1,
                Fill = this.TryFindResource(brushKey, out var brush) ? brush as IBrush : Brushes.Gray
            };

            Canvas.SetLeft(rect, 2);
            Canvas.SetTop(rect, Math.Clamp(marker.Position * height, 0, height - markerHeight));
            Minimap.Children.Add(rect);
        }
    }
}
