using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.ViewModels;

/// <summary>One comparison result: a pane's content measured against the baseline pane.</summary>
public sealed partial class ComparePairViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogService;
    private readonly string _baselineLabel;
    private readonly string _otherLabel;

    public DiffViewModel Diff { get; }
    public StructuralDiffViewModel Structural { get; } = new();

    [ObservableProperty] private string _header;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _isIdentical;

    /// <summary>Short label for the tab strip. Only this pane's name — the baseline is the same for
    /// every pair, so naming it once above the strip keeps the tabs on one row.</summary>
    public string TabLabel => _otherLabel;

    public ComparePairViewModel(
        string baselineLabel,
        string otherLabel,
        IDialogService dialogService,
        IFileDialogService fileDialogService,
        CompareOptionsViewModel options)
    {
        _baselineLabel = baselineLabel;
        _otherLabel = otherLabel;
        _dialogService = dialogService;
        _fileDialogService = fileDialogService;
        _header = $"{otherLabel} ↔ {baselineLabel}";

        // One options instance for every pair, so switching tabs doesn't show different settings.
        Diff = new DiffViewModel(options);

        // The structural view compares the *normalized* text, so it reruns whenever the text diff
        // does — including when a comparison option changes.
        Diff.NormalizedTextsChanged += () =>
            _ = Structural.SetTextsAsync(Diff.NormalizedOld, Diff.NormalizedNew);
    }

    // -----------------------------------------------------------------------
    //  Export — same formats as the "show changes" window, scoped to this pair
    // -----------------------------------------------------------------------

    private string BuildUnifiedPatch() =>
        DiffEngine.BuildUnifiedPatch(Diff.NormalizedOld, Diff.NormalizedNew, _baselineLabel, _otherLabel);

    [RelayCommand]
    private async Task CopyDiffAsync()
    {
        var patch = await Task.Run(BuildUnifiedPatch);
        if (string.IsNullOrEmpty(patch)) return;

        await _dialogService.CopyToClipboardAsync(patch);
    }

    [RelayCommand]
    private Task SaveAsPatchAsync() => DiffExportSaver.SaveAsync(
        _fileDialogService, BuildUnifiedPatch, "DiffSavePatchTitle",
        "Patch files", new[] { "*.patch", "*.diff" }, _otherLabel, ".patch");

    [RelayCommand]
    private Task SaveJsonPatchAsync() => DiffExportSaver.SaveAsync(
        _fileDialogService, () => JsonPatchGenerator.Generate(Diff.NormalizedOld, Diff.NormalizedNew),
        "DiffSaveJsonPatchTitle", "JSON files", new[] { "*.json" }, _otherLabel, ".patch.json");

    [RelayCommand]
    private Task SaveMarkdownReportAsync() => SaveReportAsync(
        DiffReportBuilder.BuildMarkdown, "DiffSaveMarkdownTitle", "Markdown files",
        new[] { "*.md" }, ".md");

    [RelayCommand]
    private Task SaveHtmlReportAsync() => SaveReportAsync(
        DiffReportBuilder.BuildHtml, "DiffSaveHtmlTitle", "HTML files",
        new[] { "*.html" }, ".html");

    private Task SaveReportAsync(
        Func<string, string, string, JsonDiffNode, string> build,
        string titleKey, string filterName, string[] patterns, string extension)
    {
        // Null while either side is invalid JSON — nothing structural to report on.
        if (Structural.Root is not { } root) return Task.CompletedTask;

        var header = Header;
        return DiffExportSaver.SaveAsync(
            _fileDialogService, () => build(header, _baselineLabel, _otherLabel, root),
            titleKey, filterName, patterns, _otherLabel, extension);
    }

    public async Task LoadAsync(string baselineText, string otherText)
    {
        await Diff.SetTextsAsync(baselineText, otherText);

        var total = Diff.AddedCount + Diff.RemovedCount + Diff.ChangedCount;
        IsIdentical = total == 0;
        Summary = IsIdentical
            ? Localizer.Get("ComparerIdentical")
            : Localizer.Get("ComparerDifferences", total);
    }
}

/// <summary>
/// Standalone JSON comparer: two to six input panes, compared against the first one. The results
/// reuse the same diff surfaces as the editor's "show changes" window.
/// </summary>
public sealed partial class JsonCompareWindowViewModel : ObservableObject
{
    public const int MinPanels = 2;
    public const int MaxPanels = 6;

    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogService;

    public ObservableCollection<ComparePanelViewModel> Panels { get; } = new();
    public ObservableCollection<ComparePairViewModel> Pairs { get; } = new();

    [ObservableProperty] private ComparePairViewModel? _selectedPair;
    [ObservableProperty] private bool _canAddPanel = true;
    [ObservableProperty] private bool _canCompare;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>One options instance for every pair, so the flyout shows the same settings whichever
    /// result tab is open.</summary>
    public CompareOptionsViewModel Options { get; } = new();

    /// <summary>"3 / 6" next to the add button, so the limit isn't a surprise when it's hit.</summary>
    public string PanelCountText => $"{Panels.Count} / {MaxPanels}";

    /// <summary>With a single pair the strip would be one lone tab — the summary above already says
    /// which panes were compared, so it's hidden.</summary>
    public bool IsMultiPair => Pairs.Count > 1;

    /// <summary>Supplied by the shell so the quick-fill buttons can reach the active editor tab
    /// without this window holding a reference to it. Async because serializing a large table is
    /// real work that shouldn't block the click.</summary>
    public Func<Task<string?>>? GetCurrentDocument { get; set; }
    public Func<Task<string?>>? GetBaselineDocument { get; set; }

    public JsonCompareWindowViewModel(IDialogService dialogService, IFileDialogService fileDialogService)
    {
        _dialogService = dialogService;
        _fileDialogService = fileDialogService;

        Panels.CollectionChanged += OnPanelsChanged;

        for (var i = 0; i < MinPanels; i++)
            Panels.Add(CreatePanel());
    }

    private ComparePanelViewModel CreatePanel()
    {
        var panel = new ComparePanelViewModel(
            Localizer.Get("ComparerPanelLabel", Panels.Count + 1), _dialogService, _fileDialogService)
        {
            CloseRequested = RemovePanel,
            MoveRequested = MovePanel,
            ValidityChanged = RefreshCompareState
        };
        return panel;
    }

    private void OnPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CanAddPanel = Panels.Count < MaxPanels;
        OnPropertyChanged(nameof(PanelCountText));
        RefreshPanelAffordances();
        RefreshCompareState();
    }

    /// <summary>Keeps each panel's close/move buttons in step with its position and the pane count.</summary>
    private void RefreshPanelAffordances()
    {
        for (var i = 0; i < Panels.Count; i++)
        {
            Panels[i].CanClose = Panels.Count > MinPanels;
            Panels[i].CanMoveLeft = i > 0;
            Panels[i].CanMoveRight = i < Panels.Count - 1;

            // Moving a pane to the front makes it the baseline, so this has to follow reordering.
            Panels[i].IsBaseline = i == 0;
        }
    }

    private void RefreshCompareState() => CanCompare = Panels.Count(p => p.IsValid) >= 2;

    [RelayCommand]
    private void AddPanel()
    {
        if (Panels.Count >= MaxPanels) return;
        Panels.Add(CreatePanel());
    }

    private void RemovePanel(ComparePanelViewModel panel)
    {
        if (Panels.Count <= MinPanels) return;
        Panels.Remove(panel);
    }

    private void MovePanel(ComparePanelViewModel panel, int offset)
    {
        var from = Panels.IndexOf(panel);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= Panels.Count) return;

        Panels.Move(from, to);
        RefreshPanelAffordances();
    }

    [RelayCommand]
    private async Task UseCurrentDocumentAsync()
    {
        if (GetCurrentDocument == null) return;
        FillFirstPanel(await GetCurrentDocument(), "ComparerFromCurrent");
    }

    [RelayCommand]
    private async Task UseSavedDocumentAsync()
    {
        if (GetBaselineDocument == null) return;
        FillFirstPanel(await GetBaselineDocument(), "ComparerFromSaved");
    }

    private void FillFirstPanel(string? text, string labelKey)
    {
        if (string.IsNullOrEmpty(text) || Panels.Count == 0) return;

        Panels[0].Text = text;
        Panels[0].Label = Localizer.Get(labelKey);
    }

    /// <summary>Every valid pane is compared against the first one, which acts as the baseline.</summary>
    [RelayCommand]
    private async Task CompareAsync()
    {
        var valid = Panels.Where(p => p.IsValid).ToList();
        if (valid.Count < 2) return;

        IsBusy = true;
        try
        {
            Pairs.Clear();
            var baseline = valid[0];

            foreach (var other in valid.Skip(1))
            {
                var pair = new ComparePairViewModel(
                    baseline.Label, other.Label, _dialogService, _fileDialogService, Options);
                await pair.LoadAsync(baseline.Text, other.Text);
                Pairs.Add(pair);
            }

            SelectedPair = Pairs.FirstOrDefault();
            HasResult = true;
            OnPropertyChanged(nameof(IsMultiPair));

            var verdict = Pairs.All(p => p.IsIdentical)
                ? Localizer.Get("ComparerIdentical")
                : Localizer.Get("ComparerDifferences", Pairs.Sum(p =>
                    p.Diff.AddedCount + p.Diff.RemovedCount + p.Diff.ChangedCount));

            // Names the baseline once, so the tab strip doesn't have to repeat it per pair.
            Summary = Pairs.Count > 1
                ? $"{Localizer.Get("ComparerBaselineNote", baseline.Label)} · {verdict}"
                : verdict;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
