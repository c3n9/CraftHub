using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CraftHub.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty] private WorkspaceViewModel? _selectedWorkspace;
    [ObservableProperty] private int _selectedWorkspaceIndex;

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = new();

    public MainWindowViewModel(IServiceProvider serviceProvider, ThemeService themeService)
    {
        _serviceProvider = serviceProvider;
        themeService.SwitchTheme(themeService.CurrentTheme); 
        AddWorkspace();
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
        ts.SwitchTheme(ts.CurrentTheme == Models.Enums.ThemeType.Dark ? Models.Enums.ThemeType.Light : Models.Enums.ThemeType.Dark);
    }
}
