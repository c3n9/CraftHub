using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CraftHub.ViewModels;

/// <summary>
/// Single node in the file explorer tree (a file or a directory).
/// Directory children are loaded lazily the first time the node is expanded,
/// mirroring the VS Code explorer behaviour and keeping large trees responsive.
/// The tree only ever shows folders and *.json files.
/// </summary>
public partial class FileSystemItemViewModel : ObservableObject
{
    private readonly FileExplorerViewModel _owner;
    private bool _childrenLoaded;
    
    [ObservableProperty]
    private bool isDragOver;
    
    /// <summary>True for the dummy child used only to render the expander arrow before lazy loading.</summary>
    private readonly bool _isPlaceholder;

    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public bool IsFile => !IsDirectory;

    public ObservableCollection<FileSystemItemViewModel> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;

    public MaterialIconKind IconKind => IsDirectory
        ? (IsExpanded ? MaterialIconKind.FolderOpenOutline : MaterialIconKind.FolderOutline)
        : MaterialIconKind.CodeJson;

    public FileSystemItemViewModel(string fullPath, bool isDirectory, FileExplorerViewModel owner,
        bool isPlaceholder = false, bool addPlaceholder = true)
    {
        _owner = owner;
        _isPlaceholder = isPlaceholder;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Name = isPlaceholder ? "…" : (Path.GetFileName(fullPath) is { Length: > 0 } n ? n : fullPath);

        if (isDirectory && !isPlaceholder && addPlaceholder && HasVisibleChildren())
            Children.Add(new FileSystemItemViewModel(fullPath, false, owner, isPlaceholder: true));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IconKind));
        if (value && IsDirectory && !_childrenLoaded)
            LoadChildren();
    }

    //  Context-menu commands (delegate to the owner explorer)

    [RelayCommand]
    private void Open() => _owner.ActivateItem(this);

    [RelayCommand]
    private Task CopyPath() => _owner.CopyItemPathAsync(this);

    [RelayCommand]
    private void Reveal() => _owner.RevealItem(this);

    [RelayCommand]
    private void RefreshSelf() => _owner.RefreshItem(this);

    [RelayCommand]
    private Task NewFileHere() => _owner.CreateNewFileAsync(FullPath);

    [RelayCommand]
    private void Copy() => _owner.CopyItem(this);

    [RelayCommand]
    private void Cut() => _owner.CutItem(this);

    [RelayCommand]
    private Task Paste() => _owner.PasteIntoAsync(this);

    [RelayCommand]
    private Task Delete() => _owner.DeleteItemAsync(this);

    //  Children loading

    /// <summary>Reloads this directory's children from disk.</summary>
    public void Refresh()
    {
        _childrenLoaded = false;
        Children.Clear();
        if (IsExpanded)
            LoadChildren();
        else if (HasVisibleChildren())
            Children.Add(new FileSystemItemViewModel(FullPath, false, _owner, isPlaceholder: true));
    }

    /// <summary>Populates children directly (used by search results) without touching the disk.</summary>
    public void ApplySearchChildren(IEnumerable<FileSystemItemViewModel> children)
    {
        Children.Clear();
        foreach (var child in children)
            Children.Add(child);
        _childrenLoaded = true;
        IsExpanded = true;
    }

    private void LoadChildren()
    {
        Children.Clear();
        _childrenLoaded = true;

        try
        {
            foreach (var dir in EnumerateVisibleDirectories(FullPath))
                Children.Add(new FileSystemItemViewModel(dir, true, _owner));

            foreach (var file in EnumerateJsonFiles(FullPath))
                Children.Add(new FileSystemItemViewModel(file, false, _owner));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>Cheap check used to decide whether to show the expander arrow.</summary>
    private bool HasVisibleChildren()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(FullPath))
                if (!IsHidden(dir)) return true;
            foreach (var file in EnumerateDocumentFiles(FullPath))
                if (!IsHidden(file)) return true;
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return false;
    }

    //  Enumeration helpers (folders + *.json only)

    internal static IEnumerable<string> EnumerateVisibleDirectories(string path)
        => Directory.EnumerateDirectories(path)
            .Where(d => !IsHidden(d))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Everything the explorer will open: plain JSON plus CraftHub bundles, which are a
    /// document too and would otherwise be invisible in the very tree you'd look for them in.</summary>
    internal static IEnumerable<string> EnumerateJsonFiles(string path)
        => EnumerateDocumentFiles(path)
            .Where(f => !IsHidden(f))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateDocumentFiles(string path)
        => Directory.EnumerateFiles(path, "*.json")
            .Concat(Directory.EnumerateFiles(path, "*.crhb"));

    /// <summary>Fast, name-based hidden check (no per-file attribute syscalls).</summary>
    internal static bool IsHidden(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !string.IsNullOrEmpty(name) && name[0] == '.'; // .git, .vs, .idea, …
    }
}
