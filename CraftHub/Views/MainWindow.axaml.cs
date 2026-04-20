using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CraftHub.Views;

public partial class MainWindow : Window
{
    private ScrollViewer? _notificationHistoryScroll;
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        _notificationHistoryScroll = this.FindControl<ScrollViewer>("NotificationHistoryScroll");
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Wire up tab click selection since binding to SelectedWorkspace
        // through ItemsControl item template is non-trivial
        UpdateTabVisuals();
    }

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
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            if (_vm.Notifications is INotifyCollectionChanged notifications)
            {
                notifications.CollectionChanged += OnNotificationsCollectionChanged;
            }
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
