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

    public FileExplorerViewModel FileExplorer { get; }

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

    /// <summary>False when every tab has been closed — the shell shows an empty-state placeholder.</summary>
    public bool HasWorkspaces => Workspaces.Count > 0;

    public ReadOnlyObservableCollection<Notification> Notifications => _notificationService.Notifications;
    public ReadOnlyObservableCollection<Notification> ActiveNotifications => _notificationService.ActiveNotifications;

    public MainWindowViewModel(IServiceProvider serviceProvider, ThemeService themeService, NotificationService notificationService, IDialogService dialogService, FileExplorerViewModel fileExplorer)
    {
        _serviceProvider = serviceProvider;
        _notificationService = notificationService;
        _dialogService = dialogService;
        FileExplorer = fileExplorer;
        FileExplorer.FileOpenRequested += OnFileOpenRequested;
        FileExplorer.NewFileRequested += OnNewFileRequested;
        ShowNotificationPopups = _notificationService.ShowPopups;
        Workspaces.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasWorkspaces));
        themeService.SwitchTheme(themeService.CurrentTheme);
        AddWorkspace();
        CheckUpdate();
    }

    /// <summary>
    /// Opens a file picked in the explorer inside a workspace tab: reuses the current tab
    /// when it is empty, otherwise creates a new one. C# files load the class schema,
    /// everything else is imported as JSON.
    /// </summary>
    private async void OnFileOpenRequested(string path) => await OpenFileIntoWorkspaceAsync(path);

    /// <summary>
    /// Opens a single file into a workspace tab: reuses the current tab if it is empty,
    /// otherwise creates a new one. Routes .cs files to the class importer, everything else
    /// to the JSON importer.
    /// </summary>
    private async Task OpenFileIntoWorkspaceAsync(string path)
    {
        try
        {
            var already = Workspaces.FirstOrDefault(w =>
                !string.IsNullOrEmpty(w.FilePath) &&
                string.Equals(Path.GetFullPath(w.FilePath!), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (already != null)
            {
                SelectWorkspace(already);
                return;
            }

            var target = SelectedWorkspace;
            var reuseEmpty = target is { Properties.Count: 0, Rows.Count: 0 };

            if (target == null || !reuseEmpty)
            {
                target = TryAddWorkspace();
                if (target == null) return; // tab limit reached (notification already shown)
            }
            else
            {
                SelectWorkspace(target);
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cs")
                await target.ImportClassFromPathAsync(path);
            else
                await target.ImportFromPathAsync(path);
        }
        catch (Exception ex)
        {
            _notificationService.Publish(NotificationType.Error, ex.Message);
        }
    }

    /// <summary>
    /// Opens files dropped onto the window — each in its own tab (the first reuses an empty tab).
    /// Awaited sequentially so the empty-tab reuse and tab-limit checks stay consistent.
    /// </summary>
    public async Task OpenDroppedFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                await OpenFileIntoWorkspaceAsync(path);
        }
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
        // "name" is optional in the API response as far as this code can know, and every branch
        // below calls Contains/EndsWith on it — one asset without a name would throw here rather
        // than simply not matching.
        assets = assets?.Where(a => a?.Name is not null && a.BrowserDownloadUrl is not null).ToList();

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
        // The bell panel's own toggle and the settings switch are the same preference, so
        // whichever one the user reaches for has to persist.
        Properties.Settings.Default.ShowNotificationPopups = value;
        Properties.Settings.Default.Save();
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
    private Task ShowReleases() => _dialogService.ShowReleasesDialogAsync(CurrentVersion);

    // Opens the standalone JSON Comparer window. Wired up in a later step; the toolbar button
    // stays bound to this command so it doesn't need re-pointing then.
    [RelayCommand]
    private Task ShowJsonComparer()
        => _dialogService.ShowJsonComparerAsync(
            // Resolved at click time, so the buttons always target whichever tab is active then.
            getCurrentDocument: async () => SelectedWorkspace == null
                ? null
                : await SelectedWorkspace.GetCurrentCanonicalJsonAsync(),
            getBaselineDocument: () => Task.FromResult(SelectedWorkspace?.BaselineJsonSnapshot));

    [RelayCommand]
    private Task ShowFormulaReference() => _dialogService.ShowFormulaReferenceAsync();

    [RelayCommand]
    private Task ShowSettings() => _dialogService.ShowSettingsAsync(
        CurrentVersion,
        showReleases: () => _dialogService.ShowReleasesDialogAsync(CurrentVersion),
        openGitHub: () => GoToGitHubRepoCommand.Execute(null));

    [RelayCommand]
    private async void GoToGitHubRepo()
    {
        var ls = Services.LanguageService.Instance;
        var confirmed = await _dialogService.ShowConfirmAsync(
            ls.Get("ClosingWarningTitle"),
            ls.Get("GoToGitWarningMsg"));
        if (!confirmed)
        {
            return;
        }
        Process.Start(new ProcessStartInfo("https://github.com/c3n9/CraftHub") { UseShellExecute = true });
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

                // A private, freshly created directory rather than the shared temp root. The name
                // of the file we are about to download is predictable from the release, and the
                // temp root is world-writable on Linux and macOS: another local account could
                // pre-create that path (or a symlink at it) and get the verified-then-executed
                // installer written somewhere of its choosing, or swapped between the two steps.
                var downloadDirectory = Directory.CreateTempSubdirectory("crafthub-update-").FullName;

                string downloadPath = Path.GetFullPath(Path.Combine(downloadDirectory, sanitizedName));
                if (!downloadPath.StartsWith(downloadDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
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
                    if (!IsTrustedDownloadUri(uri))
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
                    throw new Exception(
                        "The download could not be verified against the release's SHA256 digest, so it was not installed. " +
                        "The file may be corrupted or may have been tampered with.");
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
    /// Whether an asset URL may be downloaded and then executed as an installer. HTTPS is part of
    /// the check, not a detail: over plain HTTP anyone on the path could serve the installer
    /// instead, and a host test alone would have let "http://github.com/..." through. The host list
    /// covers github.com, its subdomains, and the *.githubusercontent.com buckets that release
    /// assets actually redirect to.
    /// </summary>
    private static bool IsTrustedDownloadUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Вычисляет SHA256-хеш скачанного файла и сравнивает с ожидаемым хешем.
    ///
    /// Fails closed when the release carries no digest: this file is about to be run as an
    /// installer, with elevation on Windows and sudo on Linux, so "no hash to check" is not a
    /// reason to trust it — it is the one case where a substituted asset would go unnoticed.
    /// </summary>
    private async Task<bool> VerifyChecksum(string filePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256) || !IsSha256Hex(expectedSha256))
        {
            Debug.WriteLine("Refusing to install: the release provides no usable SHA256 digest.");
            return false;
        }

        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = await sha256.ComputeHashAsync(stream);
                var expected = Convert.FromHexString(expectedSha256);
                // Fixed-time comparison: not because a timing attack is plausible here, but because
                // it costs nothing and keeps the comparison correct for any input length.
                return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(hash, expected);
            }
        }
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64) return false;
        foreach (var c in value)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
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
            // ArgumentList, not Process.Start("open", filePath): the two-string overload parses its
            // second argument into an argv, so a path with a quote or a space in it would split
            // into extra arguments to `open`.
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add(filePath);
            Process.Start(psi);
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
        vm.GetProjectRoot = () => FileExplorer.RootPath;   // default Save-As folder
        vm.FileSaved = OnWorkspaceFileSaved;               // refresh the tree after saving
        Workspaces.Add(vm);
        SelectedWorkspace = vm;
        SelectedWorkspaceIndex = Workspaces.Count - 1;
        return vm;
    }

    private void OnWorkspaceFileSaved(string path) => FileExplorer.NotifyFileSaved(path);

    /// <summary>Opens a freshly created (empty) file in a workspace bound to it, without importing.</summary>
    private void OnNewFileRequested(string path)
    {
        var target = SelectedWorkspace;
        var reuseEmpty = target is { Properties.Count: 0, Rows.Count: 0 };

        if (target == null || !reuseEmpty)
        {
            target = TryAddWorkspace();
            if (target == null) return;
        }
        else
        {
            SelectWorkspace(target);
        }

        target.BindToNewFile(path);
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
        if (sender is not WorkspaceViewModel vm || !Workspaces.Contains(vm)) return;

        var idx = Workspaces.IndexOf(vm);
        vm.CloseRequested -= OnWorkspaceCloseRequested;
        Workspaces.Remove(vm);

        if (Workspaces.Count == 0)
        {
            SelectedWorkspaceIndex = -1;
            SelectedWorkspace = null; // empty state
        }
        else if (SelectedWorkspace == vm)
        {
            SelectedWorkspaceIndex = Math.Min(idx, Workspaces.Count - 1);
            SelectedWorkspace = Workspaces[SelectedWorkspaceIndex];
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
