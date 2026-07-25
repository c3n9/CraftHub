using System;
using System.IO;
using CommunityToolkit.Mvvm.Input;

namespace CraftHub.ViewModels;

/// <summary>One entry in the file explorer's "recent folders" dropdown.</summary>
public sealed class RecentFolderViewModel
{
    /// <summary>Full path of the folder (shown as the menu item tooltip).</summary>
    public string Path { get; }

    /// <summary>Folder name shown as the menu item header.</summary>
    public string DisplayName { get; }

    /// <summary>Home-relative parent path, trimmed to its tail — the dimmed hint like in VS Code.</summary>
    public string ShortPath { get; }

    /// <summary>Opens this folder as the explorer root.</summary>
    public IRelayCommand OpenCommand { get; }

    public RecentFolderViewModel(string path, Action<string> open)
    {
        Path = path;
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        DisplayName = string.IsNullOrEmpty(name) ? path : name;
        ShortPath = BuildShortPath(System.IO.Path.GetDirectoryName(trimmed));
        OpenCommand = new RelayCommand(() => open(Path));
    }

    /// <summary>
    /// Turns a parent directory into a compact hint: home is shown as "~", and if the path is
    /// deep only its last few segments are kept (prefixed with "…/") so the end stays visible.
    /// </summary>
    private static string BuildShortPath(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return string.Empty;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && dir.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            dir = "~" + dir[home.Length..];

        dir = dir.Replace('\\', '/');

        const int maxSegments = 3;
        var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > maxSegments)
            return "…/" + string.Join('/', parts[^maxSegments..]);

        return dir;
    }
}
