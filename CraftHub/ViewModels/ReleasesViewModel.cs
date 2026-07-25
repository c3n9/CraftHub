using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;

namespace CraftHub.ViewModels;

/// <summary>Loads and exposes all GitHub releases for the changelog window.</summary>
public partial class ReleasesViewModel : ViewModelBase
{
    private const string ReleasesUrl = "https://api.github.com/repos/c3n9/CraftHub/releases";

    // Fail fast when the network hangs (connected but unresponsive) instead of waiting
    // for HttpClient's 100s default — only affects this request, not update downloads.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly string _currentVersion;

    public ObservableCollection<ReleaseItemViewModel> Releases { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasReleases => !IsLoading && !HasError && Releases.Count > 0;
    public bool IsEmpty => !IsLoading && !HasError && Releases.Count == 0;

    public ReleasesViewModel(string? currentVersion)
    {
        _currentVersion = (currentVersion ?? string.Empty).TrimStart('v');
    }

    private void RaiseStateFlags()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnIsLoadingChanged(bool value) => RaiseStateFlags();

    partial void OnErrorMessageChanged(string? value) => RaiseStateFlags();

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        Releases.Clear();
        RaiseStateFlags();

        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            var response = await NetManager.Get(ReleasesUrl, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = Localizer.Get("ReleasesLoadError");
                return;
            }

            var releases = await NetManager.ParseHttpResponseMessage<List<GitHubRelease>>(response);
            if (releases == null) return;

            foreach (var r in releases)
            {
                var version = r.TagName?.TrimStart('v') ?? string.Empty;
                var title = string.IsNullOrWhiteSpace(r.Name) ? (r.TagName ?? string.Empty) : r.Name;
                var date = r.PublishedAt?.LocalDateTime.ToString("dd.MM.yyyy") ?? string.Empty;
                var isCurrent = !string.IsNullOrEmpty(version) && version == _currentVersion;

                Releases.Add(new ReleaseItemViewModel(
                    r.TagName ?? string.Empty,
                    title,
                    date,
                    (r.Body ?? string.Empty).Trim(),
                    r.HtmlUrl ?? string.Empty,
                    isCurrent,
                    r.Prerelease));
            }
        }
        catch (Exception)
        {
            ErrorMessage = Localizer.Get("ReleasesLoadError");
        }
        finally
        {
            IsLoading = false;
            RaiseStateFlags();
        }
    }
}
