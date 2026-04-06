using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Models;
using CraftHub.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CraftHub.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationService _notificationService;

    [ObservableProperty] private WorkspaceViewModel? _selectedWorkspace;
    [ObservableProperty] private int _selectedWorkspaceIndex;
    [ObservableProperty] private bool _isNotificationManagerOpen;
    [ObservableProperty] private bool _showNotificationPopups = true;

    public bool ShowPopupOverlay => ShowNotificationPopups && !IsNotificationManagerOpen;

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = new();

    public ReadOnlyObservableCollection<Notification> Notifications => _notificationService.Notifications;
    public ReadOnlyObservableCollection<Notification> ActiveNotifications => _notificationService.ActiveNotifications;

    public MainWindowViewModel(IServiceProvider serviceProvider, ThemeService themeService, NotificationService notificationService)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        ShowNotificationPopups = _notificationService.ShowPopups;
        themeService.SwitchTheme(themeService.CurrentTheme); 
        AddWorkspace();
    }

    partial void OnShowNotificationPopupsChanged(bool value)
    {
        _notificationService.ShowPopups = value;
        OnPropertyChanged(nameof(ShowPopupOverlay));
    }

    partial void OnIsNotificationManagerOpenChanged(bool value)
    {
        _notificationService.PopupsSuppressed = value;
        OnPropertyChanged(nameof(ShowPopupOverlay));
    }

    [RelayCommand]
    private void ToggleNotificationManager()
    {
        IsNotificationManagerOpen = !IsNotificationManagerOpen;
    }

    [RelayCommand]
    private void DismissNotification(Notification notification)
    {
        _notificationService.Dismiss(notification);
    }

    [RelayCommand]
    private void DismissHistoryNotification(Notification notification)
    {
        _notificationService.DismissHistory(notification);
    }

    [RelayCommand]
    private void ClearNotificationHistory()
    {
        _notificationService.ClearHistory();
    }

    [RelayCommand]
    private void ToggleNotificationPopups()
    {
        ShowNotificationPopups = !ShowNotificationPopups;
    }

    [RelayCommand]
    private void AddWorkspace()
    {
        if (Workspaces.Count >= 15)
            return;

        var vm = _serviceProvider.GetRequiredService<WorkspaceViewModel>();
        vm.Header = $"Tab {Workspaces.Count + 1}";
        vm.CloseRequested += OnWorkspaceCloseRequested;
        Workspaces.Add(vm);
        SelectedWorkspace = vm;
        SelectedWorkspaceIndex = Workspaces.Count - 1;
    }

    [RelayCommand]
    private void SelectWorkspace(WorkspaceViewModel vm)
    {
        if (Workspaces.Contains(vm))
        {
            SelectedWorkspace = vm;
            SelectedWorkspaceIndex = Workspaces.IndexOf(vm);
        }
    }

    private void OnWorkspaceCloseRequested(object? sender, EventArgs e)
    {
        if (sender is WorkspaceViewModel vm && Workspaces.Count > 1)
        {
            var idx = Workspaces.IndexOf(vm);
            vm.CloseRequested -= OnWorkspaceCloseRequested;
            Workspaces.Remove(vm);
            if (SelectedWorkspace == vm)
            {
                SelectedWorkspaceIndex = Math.Min(idx, Workspaces.Count - 1);
                SelectedWorkspace = Workspaces[SelectedWorkspaceIndex];
            }
        }
    }

    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel? oldValue, WorkspaceViewModel? newValue)
    {
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var ts = _serviceProvider.GetRequiredService<Services.ThemeService>();
        var currentTheme = ts.CurrentTheme;
        Models.Enums.ThemeType targetTheme;

        if (currentTheme == Models.Enums.ThemeType.Default)
        {
            var actualVariant = Application.Current?.ActualThemeVariant;
            targetTheme = actualVariant == ThemeVariant.Dark
                ? Models.Enums.ThemeType.Light
                : Models.Enums.ThemeType.Dark;
        }
        else
        {
            targetTheme = currentTheme == Models.Enums.ThemeType.Dark
                ? Models.Enums.ThemeType.Light
                : Models.Enums.ThemeType.Dark;
        }

        ts.SwitchTheme(targetTheme);
    }
}
