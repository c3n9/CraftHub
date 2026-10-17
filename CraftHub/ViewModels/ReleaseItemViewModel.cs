using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace CraftHub.ViewModels;

/// <summary>One GitHub release shown in the releases (changelog) window.</summary>
public sealed class ReleaseItemViewModel
{
    public string Version { get; }
    public string Title { get; }
    public string Date { get; }
    public string Notes { get; }
    public string HtmlUrl { get; }
    public bool IsCurrent { get; }
    public bool IsPrerelease { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    public IRelayCommand OpenOnGitHubCommand { get; }

    public ReleaseItemViewModel(string version, string title, string date, string notes,
        string htmlUrl, bool isCurrent, bool isPrerelease)
    {
        Version = version;
        Title = title;
        Date = date;
        Notes = notes;
        HtmlUrl = htmlUrl;
        IsCurrent = isCurrent;
        IsPrerelease = isPrerelease;
        OpenOnGitHubCommand = new RelayCommand(OpenOnGitHub, () => !string.IsNullOrEmpty(HtmlUrl));
    }

    private void OpenOnGitHub()
    {
        // HtmlUrl comes straight out of the GitHub API response, and UseShellExecute hands whatever
        // it is to the OS to open — so a value that isn't an http(s) URL (a local path, a
        // file:// target, some registered scheme) would be launched as such. Checking the scheme
        // keeps this button to what it claims to be: opening a web page.
        if (!Uri.TryCreate(HtmlUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // opening the browser is best-effort
        }
    }
}
