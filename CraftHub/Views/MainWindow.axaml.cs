using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Services;
using CraftHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace CraftHub.Views;

public partial class MainWindow : Window
{
    private ScrollViewer? _notificationHistoryScroll;
    private MainWindowViewModel? _vm;

    // Onboarding is shown once per user; the "?" title-bar button replays it on demand.
    private bool _tourAutoStarted;

    // File types that can be opened by dropping them onto the window.
    private static readonly string[] OpenableExtensions = { ".json", ".txt", ".cs" };

    public MainWindow()
    {
        InitializeComponent();
        _notificationHistoryScroll = this.FindControl<ScrollViewer>("NotificationHistoryScroll");

        // Drag a .json/.txt/.cs file onto the window to open it in a tab.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFilesDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnFilesDragLeave);
        AddHandler(DragDrop.DropEvent, OnFilesDrop);
    }

    private void OnFilesDragOver(object? sender, DragEventArgs e)
    {
        // Ignore drops while a modal dialog (e.g. the import field-mapping window) is open.
        var canOpen = !IsModalDialogOpen() && ExtractOpenablePaths(e).Any();
        e.DragEffects = canOpen ? DragDropEffects.Copy : DragDropEffects.None;
        SetDragOverlay(canOpen);
        e.Handled = true;
    }

    private void OnFilesDragLeave(object? sender, DragEventArgs e) => SetDragOverlay(false);

    private async void OnFilesDrop(object? sender, DragEventArgs e)
    {
        // Mark handled synchronously so the OS drag operation is released immediately;
        // the import (which may open dialogs) runs afterwards, off the drag loop.
        e.Handled = true;
        SetDragOverlay(false);
        if (IsModalDialogOpen()) return;

        var paths = ExtractOpenablePaths(e);
        if (paths.Count == 0 || _vm == null) return;

        try
        {
            await _vm.OpenDroppedFilesAsync(paths);
        }
        catch (Exception ex)
        {
            // async void — never let a failure escape as an unhandled UI-thread exception.
            System.Diagnostics.Debug.WriteLine($"Drag-drop import failed: {ex}");
        }
    }

    private void SetDragOverlay(bool visible)
    {
        if (DragDropOverlay != null)
            DragDropOverlay.IsVisible = visible;
    }

    // True when any other window (import mapping, message box, nested editor, …) is open on
    // top of the main window. Dialogs are shown as separate modal Windows, so they appear in
    // the desktop lifetime's window list alongside this one.
    private bool IsModalDialogOpen()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
                if (!ReferenceEquals(window, this) && window.IsVisible)
                    return true;
        }
        return false;
    }

    // Pulls local file paths with a supported extension out of a drag payload.
    // The storage items are IDisposable, so each is released once its path is read.
    private static List<string> ExtractOpenablePaths(DragEventArgs e)
    {
        var paths = new List<string>();
        if (!e.Data.Contains(DataFormats.Files) || e.Data.GetFiles() is not { } items)
            return paths;

        foreach (var item in items)
        {
            try
            {
                var path = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)
                    && File.Exists(path)
                    && OpenableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                    paths.Add(path);
            }
            finally
            {
                item.Dispose();
            }
        }

        return paths;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Wire up tab click selection since binding to SelectedWorkspace
        // through ItemsControl item template is non-trivial
        UpdateTabVisuals();

        // Auto-start the app tour the first time (posted to Background so the first layout pass
        // completes and coachmark targets have on-screen bounds). Once-only is enforced by the
        // onboarding service's progress store.
        if (!_tourAutoStarted)
        {
            _tourAutoStarted = true;
            Dispatcher.UIThread.Post(
                () => App.Current.Services.GetRequiredService<IOnboardingService>().ShowAppTour(this),
                DispatcherPriority.Background);
        }
    }

    //  Interface onboarding tour (Coachlight)

    private void OnStartTourClick(object? sender, RoutedEventArgs e)
        // force: true — always replay, even if already completed once.
        => App.Current.Services.GetRequiredService<IOnboardingService>().ShowAppTour(this, force: true);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            if (_vm.Notifications is INotifyCollectionChanged oldNotifications)
            {
                oldNotifications.CollectionChanged -= OnNotificationsCollectionChanged;
            }
            _vm.FileExplorer.PropertyChanged -= OnFileExplorerPropertyChanged;
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            if (_vm.Notifications is INotifyCollectionChanged notifications)
            {
                notifications.CollectionChanged += OnNotificationsCollectionChanged;
            }

            _vm.FileExplorer.PropertyChanged += OnFileExplorerPropertyChanged;
            ApplyExplorerColumn();
        }
    }

    //  File explorer panel width management
    //  ColumnDefinition does not inherit DataContext, so the column width is
    //  driven imperatively from the FileExplorer view-model state.

    private ColumnDefinition? ExplorerColumn =>
        MainContentGrid?.ColumnDefinitions.Count > 0 ? MainContentGrid.ColumnDefinitions[0] : null;

    private void OnFileExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileExplorerViewModel.IsVisible))
        {
            // Preserve the width the user dragged to before collapsing the panel.
            var col = ExplorerColumn;
            if (_vm?.FileExplorer is { IsVisible: false } fe && col != null &&
                col.Width.IsAbsolute && col.Width.Value > 0)
            {
                fe.PanelWidth = col.Width.Value;
            }
            ApplyExplorerColumn();
        }
    }

    private void ApplyExplorerColumn()
    {
        var fe = _vm?.FileExplorer;
        var col = ExplorerColumn;
        if (fe == null || col == null) return;

        if (fe.IsVisible)
        {
            col.MinWidth = FileExplorerViewModel.MinPanelWidth;
            col.MaxWidth = FileExplorerViewModel.MaxPanelWidth;
            var width = Math.Clamp(fe.PanelWidth, FileExplorerViewModel.MinPanelWidth, FileExplorerViewModel.MaxPanelWidth);
            col.Width = new GridLength(width, GridUnitType.Pixel);
        }
        else
        {
            col.MinWidth = 0;
            col.MaxWidth = double.PositiveInfinity;
            col.Width = new GridLength(0);
        }
    }

    private void CaptureExplorerWidth()
    {
        var col = ExplorerColumn;
        if (_vm?.FileExplorer is { IsVisible: true } fe && col != null &&
            col.Width.IsAbsolute && col.Width.Value > 0)
        {
            fe.PanelWidth = col.Width.Value;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsNotificationManagerOpen) &&
            _vm?.IsNotificationManagerOpen == true)
        {
            ScrollNotificationHistoryToBottom(force: true);
        }
    }

    private void OnNotificationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm?.IsNotificationManagerOpen != true)
        {
            return;
        }

        // Keep the view pinned to the bottom only if the user already was at (or near) bottom.
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace)
        {
            ScrollNotificationHistoryToBottom(force: false);
        }
    }

    private void ScrollNotificationHistoryToBottom(bool force)
    {
        var sv = _notificationHistoryScroll;
        if (sv == null)
        {
            return;
        }

        if (!force && !IsAtBottom(sv))
        {
            return;
        }

        // Post to UI thread so layout has a chance to update Extent/Viewport after item changes.
        Dispatcher.UIThread.Post(() =>
        {
            if (_notificationHistoryScroll == null)
            {
                return;
            }
            _notificationHistoryScroll.Offset = new Avalonia.Vector(_notificationHistoryScroll.Offset.X, double.MaxValue);
        }, DispatcherPriority.Background);
    }

    private static bool IsAtBottom(ScrollViewer sv)
    {
        const double epsilon = 6;
        var maxY = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
        return sv.Offset.Y >= maxY - epsilon;
    }

    private void UpdateTabVisuals()
    {
        // Tab visuals are handled via styles
    }
    
    private bool _isConfirmedClose = false;

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_isConfirmedClose) return;

        CaptureExplorerWidth();
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
