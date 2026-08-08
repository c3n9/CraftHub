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
    }

    [RelayCommand]
    private void Proceed() => RequestClose?.Invoke(new JsonDiffResult(true, DontShowAgain));

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(new JsonDiffResult(false, DontShowAgain));

    public Task LoadAsync(string oldText, string newText) =>
        Task.WhenAll(Diff.SetTextsAsync(oldText, newText), Structural.SetTextsAsync(oldText, newText));

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
    private async Task SaveAsPatchAsync()
    {
        var patch = await Task.Run(BuildPatch);
        if (string.IsNullOrEmpty(patch)) return;

        var filters = new List<FileFilter> { new("Patch files", new[] { "*.patch", "*.diff" }) };
        var path = await _fileDialogService.SaveFileAsync(
            Localizer.Get("DiffSavePatchTitle"), filters, $"{_newLabel}.patch");
        if (path == null) return;

        await File.WriteAllTextAsync(path, patch, Encoding.UTF8);
    }
}
