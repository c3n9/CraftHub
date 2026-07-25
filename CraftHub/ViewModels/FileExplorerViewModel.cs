using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Models;
using CraftHub.Services;
using Material.Icons;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.ViewModels;

/// <summary>
/// VS Code-like file explorer: pick a root folder and browse its contents as a tree.
/// Double-clicking a file raises <see cref="FileOpenRequested"/> so the shell can load it
/// into a workspace tab. Panel state (root, visibility, width, filter) is persisted.
/// </summary>
public partial class FileExplorerViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly NotificationService _notificationService;

    /// <summary>Raised when the user activates (double-clicks) a file node.</summary>
    public event Action<string>? FileOpenRequested;

    /// <summary>Raised when a new empty file was created and should be opened in a fresh tab.</summary>
    public event Action<string>? NewFileRequested;

    /// <summary>Raised after a recent folder is picked, so the view can close the dropdown.</summary>
    public event Action? RecentFolderOpened;

    public ObservableCollection<FileSystemItemViewModel> RootItems { get; } = new();

    /// <summary>Most-recently-opened folders (newest first), VS Code-style quick pick.</summary>
    public ObservableCollection<RecentFolderViewModel> RecentFolders { get; } = new();
    public bool HasRecentFolders => RecentFolders.Count > 0;
    private const int MaxRecentFolders = 5;

    [ObservableProperty] private string? _rootPath;
    [ObservableProperty] private FileSystemItemViewModel? _selectedItem;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private double _panelWidth;
    [ObservableProperty] private string _searchQuery = string.Empty;

    public bool HasRoot => !string.IsNullOrEmpty(RootPath) && Directory.Exists(RootPath);
    public string RootName => HasRoot ? (Path.GetFileName(RootPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : RootPath!) : string.Empty;

    /// <summary>Toggle-button glyph that reflects whether the explorer panel is shown or hidden.</summary>
    public MaterialIconKind ToggleIconKind => IsVisible
        ? MaterialIconKind.MenuOpen // panel open — click to collapse
        : MaterialIconKind.Menu;    // panel hidden — click to open

    /// <summary>Minimum/maximum width (px) the explorer panel column may be resized to.</summary>
    public const double MinPanelWidth = 220;
    public const double MaxPanelWidth = 320;

    public FileExplorerViewModel(
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        NotificationService notificationService)
    {
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _notificationService = notificationService;

        _isVisible = Properties.Settings.Default.FileExplorerVisible;
        _panelWidth = Properties.Settings.Default.FileExplorerWidth is > 0 and var w ? w : 300;

        LoadRecentFolders();

        var savedRoot = Properties.Settings.Default.ProjectRootFolder;
        if (!string.IsNullOrWhiteSpace(savedRoot) && Directory.Exists(savedRoot))
        {
            SetRoot(savedRoot, persist: false);
            AddRecentFolder(savedRoot); // ensure the restored folder shows up in the recent list
        }
    }

    //  Recent folders

    private void LoadRecentFolders()
    {
        RecentFolders.Clear();
        var saved = Properties.Settings.Default.RecentFolders;
        if (saved != null)
            foreach (var path in saved)
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    RecentFolders.Add(new RecentFolderViewModel(path, OpenRecentFolder));
        OnPropertyChanged(nameof(HasRecentFolders));
    }

    /// <summary>Moves <paramref name="path"/> to the front of the recent list, capped at 5, and persists.</summary>
    private void AddRecentFolder(string path)
    {
        var stored = Properties.Settings.Default.RecentFolders ?? new StringCollection();
        for (var i = stored.Count - 1; i >= 0; i--)
            if (string.Equals(stored[i], path, StringComparison.OrdinalIgnoreCase))
                stored.RemoveAt(i);
        stored.Insert(0, path);
        while (stored.Count > MaxRecentFolders)
            stored.RemoveAt(stored.Count - 1);

        Properties.Settings.Default.RecentFolders = stored;
        Properties.Settings.Default.Save();
        LoadRecentFolders();
    }

    private void RemoveRecentFolder(string path)
    {
        var stored = Properties.Settings.Default.RecentFolders;
        if (stored == null) return;
        for (var i = stored.Count - 1; i >= 0; i--)
            if (string.Equals(stored[i], path, StringComparison.OrdinalIgnoreCase))
                stored.RemoveAt(i);

        Properties.Settings.Default.RecentFolders = stored;
        Properties.Settings.Default.Save();
        LoadRecentFolders();
    }

    private void OpenRecentFolder(string path)
    {
        if (Directory.Exists(path))
        {
            SetRoot(path, persist: true); // re-promotes it to the front of the recent list
            IsVisible = true;
        }
        else
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("RecentFolderMissingMsg", path));
            RemoveRecentFolder(path);
        }

        RecentFolderOpened?.Invoke(); // close the dropdown after the pick
    }

    //  Persistence

    partial void OnIsVisibleChanged(bool value)
    {
        Properties.Settings.Default.FileExplorerVisible = value;
        Properties.Settings.Default.Save();
        OnPropertyChanged(nameof(ToggleIconKind));
    }

    partial void OnPanelWidthChanged(double value)
    {
        Properties.Settings.Default.FileExplorerWidth = value;
        Properties.Settings.Default.Save();
    }

    partial void OnRootPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasRoot));
        OnPropertyChanged(nameof(RootName));
    }

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchQuery);

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearch));
        RebuildTree();
    }

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    //  Commands

    [RelayCommand]
    private void Toggle() => IsVisible = !IsVisible;

    [RelayCommand]
    private async Task OpenFolder()
    {
        var path = await _fileDialogService.OpenFolderAsync(Localizer.Get("OpenFolderTitle"), RootPath);
        if (string.IsNullOrWhiteSpace(path)) return;

        SetRoot(path, persist: true);
        IsVisible = true;
        _notificationService.Publish(NotificationType.Success, Localizer.Get("FolderOpenedMsg", RootName));
    }

    [RelayCommand]
    private void CloseFolder()
    {
        RootItems.Clear();
        RootPath = null;
        Properties.Settings.Default.ProjectRootFolder = string.Empty;
        Properties.Settings.Default.Save();
    }

    [RelayCommand]
    private void Refresh() => RebuildTree();

    [RelayCommand]
    private Task NewFile() => CreateNewFileAsync(RootPath);

    /// <summary>Creates a new empty JSON file inside <paramref name="directory"/>, then opens it.</summary>
    public async Task CreateNewFileAsync(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        var input = await _dialogService.ShowInputDialogAsync(
            Localizer.Get("NewFileTitle"),
            Localizer.Get("NewFilePrompt"),
            "data",
            Localizer.Get("NewFileWatermark"));
        if (string.IsNullOrWhiteSpace(input)) return;

        // Files are always .json — the user types only the name.
        var name = Path.GetFileNameWithoutExtension(input.Trim()) + ".json";

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("InvalidFileNameMsg"));
            return;
        }

        var fullPath = Path.Combine(directory, name);
        if (File.Exists(fullPath))
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("FileExistsMsg", name));
            return;
        }

        try
        {
            await File.WriteAllTextAsync(fullPath, "[]", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _notificationService.Publish(NotificationType.Error, ex.Message);
            return;
        }

        NotifyFileSaved(fullPath);          // reveal it in the tree
        NewFileRequested?.Invoke(fullPath); // open a fresh tab bound to it
    }

    /// <summary>Refreshes the directory node that contains <paramref name="path"/> so a new/saved file appears.</summary>
    public void NotifyFileSaved(string path)
    {
        if (!HasRoot || string.IsNullOrEmpty(path)) return;

        string full, rootFull;
        try
        {
            full = Path.GetFullPath(path);
            rootFull = Path.GetFullPath(RootPath!);
        }
        catch
        {
            return;
        }

        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return;

        var dir = Path.GetDirectoryName(full);
        if (dir == null) return;

        var node = FindDirectoryNode(dir);
        node?.Refresh();
    }

    private FileSystemItemViewModel? FindDirectoryNode(string directory)
    {
        var target = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        FileSystemItemViewModel? Search(FileSystemItemViewModel node)
        {
            if (!node.IsDirectory) return null;
            var nodePath = Path.GetFullPath(node.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(nodePath, target, StringComparison.OrdinalIgnoreCase))
                return node;

            foreach (var child in node.Children)
            {
                if (Search(child) is { } match) return match;
            }
            return null;
        }

        foreach (var root in RootItems)
        {
            if (Search(root) is { } match) return match;
        }
        return null;
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var item in RootItems)
            CollapseRecursive(item);
    }

    private static void CollapseRecursive(FileSystemItemViewModel item)
    {
        foreach (var child in item.Children)
            CollapseRecursive(child);
        item.IsExpanded = false;
    }

    //  Per-node actions (invoked from node commands / view)

    /// <summary>Open a file in a workspace, or toggle a directory's expansion.</summary>
    public void ActivateItem(FileSystemItemViewModel item)
    {
        if (item.IsDirectory)
            item.IsExpanded = !item.IsExpanded;
        else
            FileOpenRequested?.Invoke(item.FullPath);
    }

    public async Task CopyItemPathAsync(FileSystemItemViewModel item)
    {
        await _dialogService.CopyToClipboardAsync(item.FullPath);
        _notificationService.Publish(NotificationType.Success, Localizer.Get("PathCopiedMsg"));
    }

    public void RevealItem(FileSystemItemViewModel item)
        => RevealInSystemFileManager(item.FullPath, item.IsDirectory);

    public void RefreshItem(FileSystemItemViewModel item)
    {
        if (item.IsDirectory)
            item.Refresh();
        else
            RebuildTree();
    }

    //  Delete

    public async Task DeleteItemAsync(FileSystemItemViewModel item)
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            Localizer.Get("DeleteItemTitle"),
            Localizer.Get(item.IsDirectory ? "DeleteFolderMsg" : "DeleteFileMsg", item.Name));
        if (!confirmed) return;

        try
        {
            if (item.IsDirectory)
                Directory.Delete(item.FullPath, recursive: true);
            else
                File.Delete(item.FullPath);
        }
        catch (Exception ex)
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("DeleteFailedMsg", ex.Message));
            return;
        }

        // If the root folder itself was deleted, close it; otherwise refresh the parent node.
        if (SamePath(item.FullPath, RootPath))
        {
            RootItems.Clear();
            RootPath = null;
            Properties.Settings.Default.ProjectRootFolder = string.Empty;
            Properties.Settings.Default.Save();
        }
        else
        {
            var parent = Path.GetDirectoryName(item.FullPath);
            if (parent != null) RefreshDirectory(parent);
        }

        _notificationService.Publish(NotificationType.Success, Localizer.Get("DeletedMsg", item.Name));
    }
    
    //  Clipboard (copy / cut / paste within the tree)

    private string? _clipboardPath;
    private bool _clipboardIsCut;

    public void CopyItem(FileSystemItemViewModel item)
    {
        _clipboardPath = item.FullPath;
        _clipboardIsCut = false;
        _notificationService.Publish(NotificationType.Info, Localizer.Get("CopiedItemMsg", item.Name));
    }

    public void CutItem(FileSystemItemViewModel item)
    {
        _clipboardPath = item.FullPath;
        _clipboardIsCut = true;
        _notificationService.Publish(NotificationType.Info, Localizer.Get("CutItemMsg", item.Name));
    }

    /// <summary>Pastes the clipboard entry into the directory of <paramref name="target"/> (or the target itself).</summary>
    public Task PasteIntoAsync(FileSystemItemViewModel target)
    {
        var directory = target.IsDirectory ? target.FullPath : Path.GetDirectoryName(target.FullPath);
        return PasteToDirectoryAsync(directory);
    }

    private Task PasteToDirectoryAsync(string? directory)
        => string.IsNullOrEmpty(_clipboardPath)
            ? Task.CompletedTask
            : TransferAsync(_clipboardPath, directory, move: _clipboardIsCut, isPaste: true);

    /// <summary>Moves a dragged file/folder into <paramref name="target"/> (or its parent, for files).</summary>
    public Task MoveItemAsync(string sourcePath, FileSystemItemViewModel target)
    {
        var directory = target.IsDirectory ? target.FullPath : Path.GetDirectoryName(target.FullPath);
        return TransferAsync(sourcePath, directory, move: true, isPaste: false);
    }

    /// <summary>Shared move/copy engine for paste and drag-and-drop.</summary>
    private async Task TransferAsync(string source, string? directory, bool move, bool isPaste)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        var isDir = Directory.Exists(source);
        if (!isDir && !File.Exists(source))
        {
            if (isPaste) _clipboardPath = null; // stale clipboard entry
            return;
        }

        var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var destination = Path.Combine(directory, name);

        // Guard against dropping a folder into itself or one of its descendants.
        if (isDir && (SamePath(source, directory) || IsSubPath(source, directory)))
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("PasteIntoSelfMsg"));
            return;
        }

        if (SamePath(source, destination))
        {
            if (move) return;                                // already there — no-op
            destination = MakeUniquePath(directory, name);   // copy beside the original
        }
        else if (File.Exists(destination) || Directory.Exists(destination))
        {
            if (move)
            {
                _notificationService.Publish(NotificationType.Error, Localizer.Get("FileExistsMsg", name));
                return;
            }
            destination = MakeUniquePath(directory, name);
        }

        try
        {
            if (move)
            {
                if (isDir) Directory.Move(source, destination);
                else File.Move(source, destination);
            }
            else
            {
                if (isDir) CopyDirectory(source, destination);
                else File.Copy(source, destination);
            }
        }
        catch (Exception ex)
        {
            _notificationService.Publish(NotificationType.Error, Localizer.Get("PasteFailedMsg", ex.Message));
            return;
        }

        RefreshDirectory(directory);
        if (move)
        {
            var srcParent = Path.GetDirectoryName(source);
            if (srcParent != null && !SamePath(srcParent, directory)) RefreshDirectory(srcParent);
            if (isPaste) _clipboardPath = null;
        }

        _notificationService.Publish(NotificationType.Success,
            Localizer.Get(move ? "MovedItemMsg" : "PastedItemMsg", Path.GetFileName(destination)));
    }
    
    //  Selection-based commands (used by the tree's keyboard shortcuts)

    [RelayCommand]
    private Task DeleteSelected() => SelectedItem is { } item ? DeleteItemAsync(item) : Task.CompletedTask;

    [RelayCommand]
    private void CopySelected() { if (SelectedItem is { } item) CopyItem(item); }

    [RelayCommand]
    private void CutSelected() { if (SelectedItem is { } item) CutItem(item); }

    [RelayCommand]
    private Task PasteSelected() =>
        SelectedItem is { } item ? PasteIntoAsync(item) : PasteToDirectoryAsync(RootPath);

    //  File-system helpers

    private void RefreshDirectory(string directory)
    {
        var node = FindDirectoryNode(directory);
        node?.Refresh();
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSubPath(string parent, string child)
    {
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeUniquePath(string directory, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = Path.Combine(directory, name);
        var i = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            var suffix = i == 1 ? " copy" : $" copy {i}";
            candidate = Path.Combine(directory, $"{stem}{suffix}{ext}");
            i++;
        }
        return candidate;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    //  Tree building

    private void SetRoot(string path, bool persist)
    {
        RootPath = path;
        RebuildTree();

        if (persist)
        {
            Properties.Settings.Default.ProjectRootFolder = path;
            Properties.Settings.Default.Save();
            AddRecentFolder(path);
        }
    }

    private void RebuildTree()
    {
        RootItems.Clear();
        if (!HasRoot) return;

        var query = SearchQuery?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            var budget = 8000; // cap the scan so huge trees stay responsive
            var matches = BuildSearchNodes(RootPath!, query, ref budget);
            var searchRoot = new FileSystemItemViewModel(RootPath!, isDirectory: true, this, addPlaceholder: false);
            searchRoot.ApplySearchChildren(matches);
            RootItems.Add(searchRoot);
        }
        else
        {
            var root = new FileSystemItemViewModel(RootPath!, isDirectory: true, this) { IsExpanded = true };
            RootItems.Add(root);
        }
    }

    /// <summary>
    /// Recursively builds a filtered tree: JSON files whose name contains <paramref name="query"/>,
    /// plus the folders that lead to them (auto-expanded). Bounded by <paramref name="budget"/>.
    /// </summary>
    private List<FileSystemItemViewModel> BuildSearchNodes(string directory, string query, ref int budget)
    {
        var result = new List<FileSystemItemViewModel>();
        if (budget <= 0) return result;

        try
        {
            foreach (var dir in FileSystemItemViewModel.EnumerateVisibleDirectories(directory))
            {
                if (budget-- <= 0) break;
                var childMatches = BuildSearchNodes(dir, query, ref budget);
                var dirMatches = Path.GetFileName(dir).Contains(query, StringComparison.OrdinalIgnoreCase);
                if (childMatches.Count > 0 || dirMatches)
                {
                    var node = new FileSystemItemViewModel(dir, isDirectory: true, this, addPlaceholder: false);
                    if (childMatches.Count > 0) node.ApplySearchChildren(childMatches);
                    result.Add(node);
                }
            }

            foreach (var file in FileSystemItemViewModel.EnumerateJsonFiles(directory))
            {
                if (budget-- <= 0) break;
                if (Path.GetFileName(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                    result.Add(new FileSystemItemViewModel(file, isDirectory: false, this));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return result;
    }

    private void RevealInSystemFileManager(string path, bool isDirectory)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (isDirectory)
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo("open", isDirectory ? $"\"{path}\"" : $"-R \"{path}\"") { UseShellExecute = false });
            }
            else // Linux
            {
                var dir = isDirectory ? path : Path.GetDirectoryName(path) ?? path;
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            _notificationService.Publish(NotificationType.Error, ex.Message);
        }
    }
}
