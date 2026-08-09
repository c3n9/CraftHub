using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using CraftHub.Models;

namespace CraftHub.ViewModels;

/// <summary>
/// Window chrome around the reusable <see cref="DiffViewModel"/>: title, the copy/export actions,
/// and — in confirm mode — the save/cancel gate shown before writing to disk. Both entry points
/// share this one window so the pre-save review has the same side-by-side/unified, navigation and
/// structural tabs as the standalone "show changes" view.
/// </summary>
public sealed partial class JsonChangesWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogService;
    private readonly string _oldLabel;
    private readonly string _newLabel;

    public DiffViewModel Diff { get; } = new();
    public StructuralDiffViewModel Structural { get; } = new();

    [ObservableProperty] private string _title;

    /// <summary>True when the window gates a save: adds the footer with Save/Cancel and the
    /// "don't show again" opt-out. Informational mode has no footer at all.</summary>
    public bool IsConfirmMode { get; }

    [ObservableProperty] private bool _dontShowAgain;

    /// <summary>Raised with the user's decision; the view closes itself with this as the result.</summary>
    public event Action<JsonDiffResult>? RequestClose;

    public JsonChangesWindowViewModel(
        string title,
        string oldLabel,
        string newLabel,
        IDialogService dialogService,
        IFileDialogService fileDialogService,
        bool isConfirmMode = false)
    {
        _title = title;
        _oldLabel = oldLabel;
        _newLabel = newLabel;
        _dialogService = dialogService;
        _fileDialogService = fileDialogService;
        IsConfirmMode = isConfirmMode;

        // The structural view compares the *normalized* text, so it reruns whenever the text diff
        // does — including when a comparison option changes.
        Diff.NormalizedTextsChanged += () =>
            _ = Structural.SetTextsAsync(Diff.NormalizedOld, Diff.NormalizedNew);
    }

    [RelayCommand]
    private void Proceed() => RequestClose?.Invoke(new JsonDiffResult(true, DontShowAgain));

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(new JsonDiffResult(false, DontShowAgain));

    public Task LoadAsync(string oldText, string newText) =>
        Diff.SetTextsAsync(oldText, newText);

    /// <summary>Built from the normalized text the view is actually showing, so a copied patch
    /// always matches what the user just looked at (including the ignore-key-order toggle).</summary>
    private string BuildPatch() =>
        DiffEngine.BuildUnifiedPatch(Diff.NormalizedOld, Diff.NormalizedNew, _oldLabel, _newLabel);

    [RelayCommand]
    private async Task CopyDiffAsync()
    {
        var patch = await Task.Run(BuildPatch);
        if (string.IsNullOrEmpty(patch)) return;

        await _dialogService.CopyToClipboardAsync(patch);
    }

    [RelayCommand]
    private async Task SaveAsPatchAsync() =>
        await SaveGeneratedAsync(BuildPatch, "DiffSavePatchTitle", "Patch files",
            new[] { "*.patch", "*.diff" }, ".patch");

    /// <summary>RFC 6902 operations. Unlike a unified patch these address values by JSON Pointer,
    /// so they survive reformatting and can be applied programmatically.</summary>
    [RelayCommand]
    private async Task SaveJsonPatchAsync() =>
        await SaveGeneratedAsync(
            () => JsonPatchGenerator.Generate(Diff.NormalizedOld, Diff.NormalizedNew),
            "DiffSaveJsonPatchTitle", "JSON files", new[] { "*.json" }, ".patch.json");

    [RelayCommand]
    private async Task SaveMarkdownReportAsync() =>
        await SaveReportAsync(DiffReportBuilder.BuildMarkdown, "DiffSaveMarkdownTitle",
            "Markdown files", new[] { "*.md" }, ".md");

    [RelayCommand]
    private async Task SaveHtmlReportAsync() =>
        await SaveReportAsync(DiffReportBuilder.BuildHtml, "DiffSaveHtmlTitle",
            "HTML files", new[] { "*.html" }, ".html");

    /// <summary>Reports are built from the structural comparison, so they list changes by path
    /// rather than by line — which is what makes them readable to someone without the file. Reuses
    /// the tree the structural tab already computed rather than re-parsing both documents.</summary>
    private async Task SaveReportAsync(
        Func<string, string, string, JsonDiffNode, string> build,
        string titleKey, string filterName, string[] patterns, string extension)
    {
        // Null while either side is invalid JSON — there's nothing structural to report on.
        if (Structural.Root is not { } root) return;

        var title = Title;
        await SaveGeneratedAsync(() => build(title, _oldLabel, _newLabel, root),
            titleKey, filterName, patterns, extension);
    }

    private async Task SaveGeneratedAsync(
        Func<string> build, string titleKey, string filterName, string[] patterns, string extension)
    {
        var content = await Task.Run(build);
        if (string.IsNullOrEmpty(content)) return;

        var filters = new List<FileFilter> { new(filterName, patterns) };
        var path = await _fileDialogService.SaveFileAsync(
            Localizer.Get(titleKey), filters, $"{_newLabel}{extension}");
        if (path == null) return;

        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }
}
