using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace CraftHub.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;
    private readonly NotificationService _notificationService;

    [ObservableProperty] private WorkspaceViewModel? _selectedWorkspace;
    [ObservableProperty] private int _selectedWorkspaceIndex;
    [ObservableProperty] private bool _isNotificationManagerOpen;
    [ObservableProperty] private bool _showNotificationPopups = true;

    [ObservableProperty] private bool _showUpdateButton = false;
    [ObservableProperty] private bool _isDownloading = false;
    [ObservableProperty] private double _downloadProgress = 0;
    [ObservableProperty] private string _currentLang = Services.LanguageService.Instance.CurrentLang;
    [ObservableProperty] private string _currentVersion = string.Empty;

    public bool ShowPopupOverlay => ShowNotificationPopups && !IsNotificationManagerOpen;

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = new();

    public ReadOnlyObservableCollection<Notification> Notifications => _notificationService.Notifications;
    public ReadOnlyObservableCollection<Notification> ActiveNotifications => _notificationService.ActiveNotifications;

    public MainWindowViewModel(IServiceProvider serviceProvider, ThemeService themeService, NotificationService notificationService, IDialogService dialogService)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _dialogService = dialogService;
        ShowNotificationPopups = _notificationService.ShowPopups;
        themeService.SwitchTheme(themeService.CurrentTheme);
        AddWorkspace();
        CheckUpdate();
    }
    private GitHubRelease? _latestRelease;
    private void CheckUpdate()
    {
        Task.Run(async () =>
        {
            try
            {
                CurrentVersion = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim();
                var response = await NetManager.Get("https://api.github.com/repos/c3n9/CraftHub/releases/latest");
                if (response.IsSuccessStatusCode)
                {
                    var release = await NetManager.ParseHttpResponseMessage<GitHubRelease>(response);
                    string latestVersion = release?.TagName?.TrimStart('v') ?? string.Empty;

                    if (!string.IsNullOrEmpty(latestVersion) && latestVersion != CurrentVersion)
                    {
                        _latestRelease = release;
                        Dispatcher.UIThread.Post(() =>
                        {
                            ShowUpdateButton = true;
                            _notificationService.Publish(NotificationType.Info, Services.LanguageService.Instance.Get("UpdateAvailableMsg"));
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckUpdate failed: {ex.Message}");
            }
        });
    }

    private GitHubAsset GetPlatformSpecificAsset(List<GitHubAsset> assets)
    {
        if(assets != null)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    return assets.FirstOrDefault(a => a.Name.Contains("arm64") && a.Name.EndsWith(".exe"));
                else if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
                    return assets.FirstOrDefault(a => a.Name.Contains("x86") && a.Name.EndsWith(".exe"));
                else
                    return assets.FirstOrDefault(a => a.Name.Contains("x64") && a.Name.EndsWith(".exe"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    return assets.FirstOrDefault(a => a.Name.Contains("arm64") && a.Name.EndsWith(".deb"));
                else
                    return assets.FirstOrDefault(a => (a.Name.Contains("amd64") || a.Name.Contains("x64")) && a.Name.EndsWith(".deb"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                    return assets.FirstOrDefault(a => a.Name.Contains("arm64") && a.Name.EndsWith(".dmg"));
                else
                    return assets.FirstOrDefault(a => (a.Name.Contains("x64") || a.Name.Contains("amd64")) && a.Name.EndsWith(".dmg"));
            }
        }

        return default;
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
    private async Task DownloadAndStartUpdate()
    {
        var ls = Services.LanguageService.Instance;
        var confirmed = await _dialogService.ShowConfirmAsync(
            ls.Get("NewVersionTitle"),
            ls.Get("NewVersionConfirmMsg"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            if (_latestRelease?.Assets == null)
            {
                await _dialogService.ShowMessageAsync(ls.Get("ErrorTitle"), ls.Get("NoReleaseInfoMsg"));
                return;
            }

            var asset = GetPlatformSpecificAsset(_latestRelease.Assets);

            if (asset == null)
            {
                await _dialogService.ShowMessageAsync("Error",
                    $"No installer found for your platform ({RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture})");
                return;
            }

            var result = await _dialogService.ShowProgressDialogAsync("Updating CraftHub", async (progress, cancellationToken) =>
            {
                var sanitizedName = Path.GetFileName(asset.Name);
                if (string.IsNullOrEmpty(sanitizedName))
                    throw new InvalidOperationException("Invalid asset filename.");

                string downloadPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), sanitizedName));
                if (!downloadPath.StartsWith(Path.GetFullPath(Path.GetTempPath())))
                    throw new InvalidOperationException("Asset filename contains invalid path characters.");

                progress.Report(new UpdateProgress
                {
                    Status = $"Downloading {asset.Name}...",
                    Message = "Downloading update...",
                    IsIndeterminate = true
                });

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "CraftHub-Updater");
                    client.Timeout = TimeSpan.FromMinutes(5);
                    
                    var uri = new Uri(asset.BrowserDownloadUrl);
                    if (!uri.Host.EndsWith(".github.com") && uri.Host != "github.com")
                        throw new Exception("The link has been replaced, and the github version cannot be installed.");
                    
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;
                        using (var fs = new FileStream(downloadPath, FileMode.Create,
                               FileAccess.Write, FileShare.None, 8192, true))
                        using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                        {
                            var buffer = new byte[8192];
                            long totalRead = 0;
                            int bytesRead;

                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await fs.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                totalRead += bytesRead;

                                if (totalBytes > 0)
                                {
                                    var percent = (int)((totalRead * 100) / totalBytes);
                                    progress.Report(new UpdateProgress
                                    {
                                        PercentComplete = percent,
                                        Status = $"Downloading... {percent}%",
                                        BytesReceived = totalRead,
                                        TotalBytes = totalBytes
                                    });
                                }
                                else
                                {
                                    var updateProgress = new UpdateProgress();
                                    updateProgress.BytesReceived = totalRead;
                                    updateProgress.Status = $"Downloading... {FileSizeHelper.FormatFileSize(updateProgress.BytesReceived)}";
                                    progress.Report(updateProgress);
                                }
                            }
                        }
                    }
                }

                progress.Report(new UpdateProgress
                {
                    Status = "Verifying checksum...",
                    Message = "Checking file integrity",
                    IsIndeterminate = true
                });

                if (!await VerifyChecksum(downloadPath, asset.Sha256))
                {
                    throw new Exception("Checksum verification failed. The file may be corrupted.");
                }

                progress.Report(new UpdateProgress
                {
                    Status = "Starting installer...",
                    Message = "Launching installer",
                    PercentComplete = 100
                });

                StartInstaller(downloadPath);

                CloseApplication();
            });

            if (result.IsCanceled)
            {
                await _dialogService.ShowMessageAsync(ls.Get("UpdateCancelledTitle"), ls.Get("UpdateCancelledMsg"));
            }
            else if (!result.IsSuccess && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                await _dialogService.ShowMessageAsync(ls.Get("UpdateFailedTitle"), result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync(ls.Get("ErrorTitle"), $"{ls.Get("UpdateFailedTitle")}: {ex.Message}");
        }
    }

    /// <summary>
    /// Вычисляет SHA256-хеш скачанного файла и сравнивает с ожидаемым хешем.
    /// </summary>
    private async Task<bool> VerifyChecksum(string filePath, string expectedSha256)
    {
        if (string.IsNullOrEmpty(expectedSha256))
        {
            Debug.WriteLine("Warning: No SHA256 provided for verification");
            return true;
        }

        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = await sha256.ComputeHashAsync(stream);
                var actualHash = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return actualHash == expectedSha256.ToLower();
            }
        }
    }

    private void CloseApplication()
    {
        Environment.Exit(0);
    }

    private void StartInstaller(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = $"/S /D={AppContext.BaseDirectory}",
                UseShellExecute = true
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "sudo",
                ArgumentList = { "dpkg", "-i", filePath },
                UseShellExecute = false
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", filePath);
        }
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
    private void AddWorkspace() => TryAddWorkspace();

    /// <summary>
    /// Creates a new workspace tab and returns it, or returns null if the 15-tab limit is reached.
    /// Used both by the "+" button and by WorkspaceViewModel.ImportJsonAsync for multi-file import.
    /// </summary>
    private WorkspaceViewModel? TryAddWorkspace()
    {
        if (Workspaces.Count >= 15)
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("TabLimitReachedMsg", 15));
            return null;
        }

        var vm = _serviceProvider.GetRequiredService<WorkspaceViewModel>();
        vm.Header = $"Tab {Workspaces.Count + 1}";
        vm.CloseRequested += OnWorkspaceCloseRequested;
        vm.RequestNewWorkspace = TryAddWorkspace;   // propagate so imported tabs can also multi-import
        Workspaces.Add(vm);
        SelectedWorkspace = vm;
        SelectedWorkspaceIndex = Workspaces.Count - 1;
        return vm;
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
    private void ToggleLanguage()
    {
        var ls = Services.LanguageService.Instance;
        ls.Toggle();
        CurrentLang = ls.CurrentLang;
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
