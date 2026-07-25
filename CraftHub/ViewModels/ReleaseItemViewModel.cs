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
        try
        {
            Process.Start(new ProcessStartInfo(HtmlUrl) { UseShellExecute = true });
        }
        catch
        {
            // opening the browser is best-effort
        }
    }
}
