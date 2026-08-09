using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CraftHub.Helpers;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class JsonComparePanelView : UserControl
{
    private ComparePanelViewModel? _vm;

    // Guards the two-way sync between the editor and the view-model so an echo from one side does
    // not bounce back into the other — same pattern the workspace JSON editor uses.
    private bool _suppressSync;

    public JsonComparePanelView()
    {
        InitializeComponent();

        Editor.SyntaxHighlighting = JsonHighlightingHelper.ForCurrentTheme();
        Editor.TextChanged += OnEditorTextChanged;

        DataContextChanged += OnDataContextChanged;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        AttachedToVisualTree += (_, _) => ApplyTheme();
    }

    private void ApplyTheme() => Editor.SyntaxHighlighting = JsonHighlightingHelper.ForCurrentTheme();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ComparePanelViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            PushTextToEditor(_vm.Text);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Covers the view-model-driven fills: open file, paste, format.
        if (e.PropertyName == nameof(ComparePanelViewModel.Text))
            PushTextToEditor(_vm?.Text ?? string.Empty);
    }

    private void PushTextToEditor(string text)
    {
        if (_suppressSync || Editor.Text == text) return;

        _suppressSync = true;
        Editor.Text = text ?? string.Empty;
        _suppressSync = false;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_suppressSync || _vm == null) return;

        _suppressSync = true;
        _vm.Text = Editor.Text;
        _suppressSync = false;
    }

    // -----------------------------------------------------------------------
    //  Drag & drop
    // -----------------------------------------------------------------------

    private static bool HasFile(DragEventArgs e) => e.Data.Contains(DataFormats.Files);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accepted = HasFile(e);
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.IsVisible = accepted;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropOverlay.IsVisible = false;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropOverlay.IsVisible = false;
        if (_vm == null || !HasFile(e)) return;

        e.Handled = true;

        if (e.Data.GetFiles() is not { } items) return;

        string? path = null;
        foreach (var item in items)
        {
            // Storage handles are disposed once their path has been read (see MainWindow's drop
            // handler, which established this).
            try
            {
                path ??= item.TryGetLocalPath();
            }
            finally
            {
                item.Dispose();
            }
        }

        if (!string.IsNullOrEmpty(path)) await _vm.LoadFromFileAsync(path);
    }
}
