using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Models;
using CraftHub.ViewModels;
using CraftHub.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CraftHub.Services;

public class DialogService : IDialogService
{
    private readonly NotificationService _notificationService;
    private readonly IFileDialogService _fileDialogService;

    public DialogService(NotificationService notificationService, IFileDialogService fileDialogService)
    {
        _notificationService = notificationService;
        _fileDialogService = fileDialogService;
    }
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
    
    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows)
                if (w.IsActive)
                    return w;
            return desktop.MainWindow;
        }
        return null;
    }

    public async Task<List<JsonFieldMapping>?> ShowFieldMappingDialogAsync(List<JsonFieldMapping> fields, string? fileName = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var dialog = new JsonFieldMappingView();
        var vm = new JsonFieldMappingViewModel(fields, fileName);
        dialog.DataContext = vm;

        var result = await dialog.ShowDialog<List<JsonFieldMapping>?>(window);
        return result;
    }

    public async Task ShowReleasesDialogAsync(string? currentVersion)
    {
        var window = GetMainWindow();
        if (window == null) return;

        var dialog = new ReleasesView { DataContext = new ReleasesViewModel(currentVersion) };
        await dialog.ShowDialog(window);
    }

    public async Task<JsonDiffResult> ShowJsonDiffAsync(
        string title, string oldLabel, string newLabel, string oldText, string newText)
    {
        var owner = GetActiveWindow();
        if (owner == null) return new JsonDiffResult(true, false);

        var vm = new JsonChangesWindowViewModel(
            title, oldLabel, newLabel, this, _fileDialogService, isConfirmMode: true);
        var dialog = new JsonChangesWindow { DataContext = vm };

        await vm.LoadAsync(oldText, newText);

        // Closing via the window chrome (no explicit choice) must not silently save.
        var result = await dialog.ShowDialog<JsonDiffResult?>(owner);
        return result ?? new JsonDiffResult(false, false);
    }

    public async Task ShowJsonChangesWindowAsync(
        string title, string oldLabel, string newLabel, string oldText, string newText)
    {
        var owner = GetActiveWindow();
        if (owner == null) return;

        var vm = new JsonChangesWindowViewModel(title, oldLabel, newLabel, this, _fileDialogService);
        var window = new JsonChangesWindow { DataContext = vm };

        // Compute the diff before showing so the window never flashes empty, then Show (not
        // ShowDialog) so the editor stays usable alongside it.
        await vm.LoadAsync(oldText, newText);
        window.Show(owner);
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = GetActiveWindow();
        if (window == null) return;

        var msgDialog = new MessageBoxView
        {
            Title = title,
            TitleText = title,
            MessageText = message,
            IsConfirm = false
        };

        await msgDialog.ShowDialog<bool>(window);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var window = GetActiveWindow();
        if (window == null) return false;

        var msgDialog = new MessageBoxView
        {
            Title = title,
            TitleText = title,
            MessageText = message,
            IsConfirm = true
        };

        return await msgDialog.ShowDialog<bool>(window);
    }

    public async Task<string?> ShowInputDialogAsync(string title, string message, string initialValue, string? placeholder = null)
    {
        var window = GetActiveWindow();
        if (window == null) return null;

        var dialog = new InputDialogView
        {
            Title = title,
            TitleText = title,
            MessageText = message,
            InputText = initialValue,
            PlaceholderText = placeholder ?? string.Empty
        };

        return await dialog.ShowDialog<string?>(window);
    }

    public async Task<string?> ShowSelectDialogAsync(string title, string message, string fileName, List<string> options)
    {
        var window = GetActiveWindow();
        if (window == null) return null;

        var dialog = new SelectDialogView();
        dialog.SetOptions(title, message, fileName, options);
        return await dialog.ShowDialog<string?>(window);
    }

    public async Task CopyToClipboardAsync(string text)
    {
        var window = GetMainWindow();
        if (window != null && window.Clipboard != null)
        {
            await window.Clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> ShowJsonEditorDialogAsync(string title, string initialJson, JsonFieldType type, IJsonService jsonService, IReadOnlyList<JsonPropertyDefinition>? sharedProperties = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var dialog = new JsonEditorView { Title = title };
        var vm = new JsonEditorViewModel(initialJson, type, jsonService, this, _notificationService, sharedProperties);
        dialog.DataContext = vm;

        return await dialog.ShowDialog<string?>(GetActiveWindow() ?? window);
    }

    public async Task<ProgressResult> ShowProgressDialogAsync(string title, Func<IProgress<UpdateProgress>, CancellationToken, Task> task)
    {
        var window = GetMainWindow();
        if (window == null) return ProgressResult.Error("No main window found");

        var dialog = new ProgressDialogView
        {
            TitleText = title,
            MessageText = "Starting...",
            IsIndeterminate = true
        };

        ProgressResult result = ProgressResult.Canceled();

        var taskRun = Task.Run(async () =>
        {
            try
            {
                await dialog.RunWithProgress(task);
                result = ProgressResult.Success();
            }
            catch (OperationCanceledException)
            {
                result = ProgressResult.Canceled();
            }
            catch (Exception ex)
            {
                result = ProgressResult.Error(ex.Message);
            }
        });

        await dialog.ShowDialog(window);

        await taskRun;

        return result;
    }

    public async Task<string?> GetFromClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(App.Current.Services.GetRequiredService<MainWindow>())?.Clipboard;
        return await clipboard?.GetTextAsync();
    }
}
