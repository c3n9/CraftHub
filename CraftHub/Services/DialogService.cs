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

        var dialog = new JsonFieldMappingDialog();
        var vm = new JsonFieldMappingViewModel(fields);
        dialog.DataContext = vm;

        var result = await dialog.ShowDialog<List<JsonFieldMapping>?>(window);
        return result;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window == null) return;

        var msgDialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        MinWidth = 80
                    }
                }
            }
        };

        var okButton = ((StackPanel)msgDialog.Content).Children[1] as Button;
        okButton!.Click += (_, _) => msgDialog.Close();

        await msgDialog.ShowDialog(window);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var window = GetMainWindow();
        if (window == null) return false;

        var confirmed = false;
        var msgDialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Spacing = 12,
                        Children =
                        {
                            new Button { Content = "Yes", MinWidth = 80, Tag = "yes" },
                            new Button { Content = "No", MinWidth = 80, Tag = "no" }
                        }
                    }
                }
            }
        };

        var buttonPanel = ((StackPanel)msgDialog.Content).Children[1] as StackPanel;
        (buttonPanel!.Children[0] as Button)!.Click += (_, _) => { confirmed = true; msgDialog.Close(); };
        (buttonPanel.Children[1] as Button)!.Click += (_, _) => { confirmed = false; msgDialog.Close(); };

        await msgDialog.ShowDialog(window);
        return confirmed;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        var window = GetMainWindow();
        if (window != null && window.Clipboard != null)
        {
            await window.Clipboard.SetTextAsync(text);
        }
    }
}
