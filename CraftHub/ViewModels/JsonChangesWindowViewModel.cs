using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Models;

namespace CraftHub.ViewModels;

/// <summary>
/// Window chrome around the reusable <see cref="DiffViewModel"/>: title, and the copy/export
/// actions that operate on the diff currently shown.
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

    public JsonChangesWindowViewModel(
        string title,
        string oldLabel,
        string newLabel,
        IDialogService dialogService,
        IFileDialogService fileDialogService)
    {
        _title = title;
        _oldLabel = oldLabel;
        _newLabel = newLabel;
        _dialogService = dialogService;
        _fileDialogService = fileDialogService;
    }

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
