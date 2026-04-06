using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CraftHub.Models;
using CraftHub.ViewModels;
using CraftHub.Views;

namespace CraftHub.Services;

public class DialogService : IDialogService
{
    private readonly NotificationService _notificationService;

    public DialogService(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public async Task<List<JsonFieldMapping>?> ShowFieldMappingDialogAsync(List<JsonFieldMapping> fields)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var dialog = new JsonFieldMappingView();
        var vm = new JsonFieldMappingViewModel(fields);
        dialog.DataContext = vm;

        var result = await dialog.ShowDialog<List<JsonFieldMapping>?>(window);
        return result;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = GetMainWindow();
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
        var window = GetMainWindow();
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
        var window = GetMainWindow();
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

    public async Task CopyToClipboardAsync(string text)
    {
        var window = GetMainWindow();
        if (window != null && window.Clipboard != null)
        {
            await window.Clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> ShowJsonEditorDialogAsync(string title, string initialJson, JsonFieldType type, IJsonService jsonService)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var dialog = new JsonEditorView { Title = title };
        var vm = new JsonEditorViewModel(initialJson, type, jsonService, this, _notificationService);
        dialog.DataContext = vm;
        
        return await dialog.ShowDialog<string?>(window);
    }
}
