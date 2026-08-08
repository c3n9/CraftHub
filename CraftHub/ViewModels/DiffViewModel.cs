using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Helpers;
using DiffPlex.DiffBuilder.Model;

namespace CraftHub.ViewModels;

/// <summary>Anything that can occupy a row of the diff list — a real line, a side-by-side pair, or
/// a placeholder standing in for a collapsed run of unchanged lines.</summary>
public interface IDiffRow
{
    bool HasChange { get; }
}

/// <summary>A run of text inside one diff line. The flags mark the fragments that actually differ,
/// which is what drives the word-level highlight (red on the old side, green on the new one).</summary>
public sealed class DiffSegment
{
    public required string Text { get; init; }
    public bool IsAdded { get; init; }
    public bool IsRemoved { get; init; }
}

/// <summary>One rendered diff line: gutter marker, line numbers for both sides, and the text split
/// into segments. The Is* flags drive style classes in XAML (theme brushes must resolve there, not
/// in the view-model).</summary>
public sealed class DiffLineRowViewModel : IDiffRow
{
    public required IReadOnlyList<DiffSegment> Segments { get; init; }
    public string Marker { get; init; } = " ";
    public string OldLineNumber { get; init; } = string.Empty;
    public string NewLineNumber { get; init; } = string.Empty;

    /// <summary>The single number shown in side-by-side mode, where each column carries its own side.</summary>
    public string LineNumber => string.IsNullOrEmpty(OldLineNumber) ? NewLineNumber : OldLineNumber;

    public bool IsAdded { get; init; }
    public bool IsRemoved { get; init; }
    public bool IsChanged { get; init; }

    /// <summary>False for the alignment padding DiffPlex inserts to keep both columns row-aligned.</summary>
    public bool IsRealLine { get; init; } = true;

    public bool HasChange => IsAdded || IsRemoved || IsChanged;
}

/// <summary>One row of the two-column view. Either side may be padding (<see cref="DiffLineRowViewModel.IsRealLine"/>).</summary>
public sealed class SideBySideRowViewModel : IDiffRow
{
    public required DiffLineRowViewModel Old { get; init; }
    public required DiffLineRowViewModel New { get; init; }
    public bool HasChange => Old.HasChange || New.HasChange;
}

/// <summary>Stands in for a run of unchanged lines that were folded away, with a control to bring
/// them back.</summary>
public sealed class CollapsedRegionViewModel : IDiffRow
{
    public required int HiddenCount { get; init; }

    /// <summary>Range in the *uncollapsed* list this placeholder replaced.</summary>
    public required int StartIndex { get; init; }
    public required int EndIndex { get; init; }

    public bool HasChange => false;
    public string Label => Localizer.Get("DiffShowMoreLines", HiddenCount);
}

/// <summary>A change's position (0..1 down the document) and kind, for the minimap strip.</summary>
public sealed record MinimapMarker(double Position, bool IsAdded, bool IsRemoved, bool IsChanged);

/// <summary>
/// Reusable diff surface: takes two JSON texts, normalizes them and exposes both a unified and a
/// side-by-side rendering. Shared by the "show changes" window and the JSON comparer.
/// </summary>
public sealed partial class DiffViewModel : ObservableObject
{
    /// <summary>Unchanged lines kept visible either side of a change before folding the rest.</summary>
    private const int ContextLines = 3;

    private string _oldText = string.Empty;
    private string _newText = string.Empty;

    // Full, uncollapsed rows — kept so a folded region can be restored on demand.
    private IReadOnlyList<IDiffRow> _allUnified = Array.Empty<IDiffRow>();
    private IReadOnlyList<IDiffRow> _allSideBySide = Array.Empty<IDiffRow>();

    [ObservableProperty] private ObservableCollection<IDiffRow> _unifiedRows = new();
    [ObservableProperty] private ObservableCollection<IDiffRow> _sideBySideRows = new();

    [ObservableProperty] private int _addedCount;
    [ObservableProperty] private int _removedCount;
    [ObservableProperty] private int _changedCount;

    [ObservableProperty] private bool _isIdentical;
    [ObservableProperty] private bool _hasInvalidJson;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _isSideBySide = true;
    [ObservableProperty] private bool _ignoreKeyOrder;

    [ObservableProperty] private IReadOnlyList<MinimapMarker> _minimapMarkers = Array.Empty<MinimapMarker>();

    private List<int> _changeIndices = new();
    [ObservableProperty] private int _currentChangeNumber;
    [ObservableProperty] private int _totalChangeCount;

    /// <summary>Raised when navigation needs the view to bring a row into view; the view owns the
    /// scrolling because only it knows about the list controls.</summary>
    public event Action<int>? ScrollToRowRequested;

    /// <summary>Settable inverse of <see cref="IsSideBySide"/> so both view-mode toggles can bind
    /// two-way to a plain property instead of needing a converter.</summary>
    public bool IsUnified
    {
        get => !IsSideBySide;
        set => IsSideBySide = !value;
    }

    public bool HasChanges => TotalChangeCount > 0;

    /// <summary>Normalized text of both sides, used for patch export so it matches what's displayed.</summary>
    public string NormalizedOld { get; private set; } = string.Empty;
    public string NormalizedNew { get; private set; } = string.Empty;

    public DiffViewModel()
    {
        // The two modes have independent row lists, so switching resets the navigation cursor.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsSideBySide))
                RefreshNavigation();
        };
    }

    partial void OnIsSideBySideChanged(bool value) => OnPropertyChanged(nameof(IsUnified));

    partial void OnTotalChangeCountChanged(int value) => OnPropertyChanged(nameof(HasChanges));

    // Re-normalizing changes the text itself, so the whole diff has to be recomputed.
    partial void OnIgnoreKeyOrderChanged(bool value) => _ = RecomputeAsync();

    public async Task SetTextsAsync(string oldText, string newText)
    {
        _oldText = oldText ?? string.Empty;
        _newText = newText ?? string.Empty;
        await RecomputeAsync();
    }

    private async Task RecomputeAsync()
    {
        IsBusy = true;
        try
        {
            var ignoreKeyOrder = IgnoreKeyOrder;
            var oldRaw = _oldText;
            var newRaw = _newText;

            var result = await Task.Run(() =>
            {
                // Invalid JSON still gets a text diff (canonicalize returns it untouched) — only
                // the structural comparison is unavailable, which the window surfaces separately.
                var oldValid = JsonDiffHelper.TryParseJson(oldRaw, out var oldDoc, out _);
                var newValid = JsonDiffHelper.TryParseJson(newRaw, out var newDoc, out _);
                oldDoc?.Dispose();
                newDoc?.Dispose();

                var normOld = JsonDiffHelper.CanonicalizeForDiff(oldRaw, ignoreKeyOrder);
                var normNew = JsonDiffHelper.CanonicalizeForDiff(newRaw, ignoreKeyOrder);

                var sbs = DiffEngine.ComputeSideBySide(normOld, normNew);
                var unified = DiffEngine.ComputeUnified(normOld, normNew);
                var (added, removed, changed) = DiffEngine.CountChanges(sbs);

                var allSideBySide = BuildSideBySideRows(sbs);
                var allUnified = BuildUnifiedLines(unified);

                return (
                    Invalid: !oldValid || !newValid,
                    NormOld: normOld,
                    NormNew: normNew,
                    AllSideBySide: allSideBySide,
                    AllUnified: allUnified,
                    CollapsedSideBySide: Collapse(allSideBySide),
                    CollapsedUnified: Collapse(allUnified),
                    Added: added,
                    Removed: removed,
                    Changed: changed);
            });

            NormalizedOld = result.NormOld;
            NormalizedNew = result.NormNew;
            HasInvalidJson = result.Invalid;

            _allSideBySide = result.AllSideBySide;
            _allUnified = result.AllUnified;
            SideBySideRows = new ObservableCollection<IDiffRow>(result.CollapsedSideBySide);
            UnifiedRows = new ObservableCollection<IDiffRow>(result.CollapsedUnified);

            AddedCount = result.Added;
            RemovedCount = result.Removed;
            ChangedCount = result.Changed;
            IsIdentical = result.Added == 0 && result.Removed == 0 && result.Changed == 0;

            RefreshNavigation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // -----------------------------------------------------------------------
    //  Collapsing unchanged regions
    // -----------------------------------------------------------------------

    /// <summary>Folds runs of unchanged rows, keeping <see cref="ContextLines"/> either side of
    /// every change. A run is only worth folding if it hides more rows than the placeholder costs.</summary>
    private static List<IDiffRow> Collapse(IReadOnlyList<IDiffRow> rows)
    {
        if (rows.Count == 0) return new List<IDiffRow>();

        var keep = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].HasChange) continue;
            var from = Math.Max(0, i - ContextLines);
            var to = Math.Min(rows.Count - 1, i + ContextLines);
            for (var k = from; k <= to; k++) keep[k] = true;
        }

        var result = new List<IDiffRow>(rows.Count);
        var index = 0;
        while (index < rows.Count)
        {
            if (keep[index])
            {
                result.Add(rows[index++]);
                continue;
            }

            var start = index;
            while (index < rows.Count && !keep[index]) index++;
            var count = index - start;

            if (count > 1)
                result.Add(new CollapsedRegionViewModel { HiddenCount = count, StartIndex = start, EndIndex = index - 1 });
            else
                for (var k = start; k < index; k++) result.Add(rows[k]);
        }

        return result;
    }

    /// <summary>Swaps a placeholder for the rows it was hiding.</summary>
    [RelayCommand]
    private void ExpandRegion(CollapsedRegionViewModel? region)
    {
        if (region == null) return;

        var target = IsSideBySide ? SideBySideRows : UnifiedRows;
        var source = IsSideBySide ? _allSideBySide : _allUnified;

        var at = target.IndexOf(region);
        if (at < 0) return;

        target.RemoveAt(at);
        for (var i = region.StartIndex; i <= region.EndIndex && i < source.Count; i++)
            target.Insert(at++, source[i]);

        RefreshNavigation();
    }

    // -----------------------------------------------------------------------
    //  Navigation between changes
    // -----------------------------------------------------------------------

    private void RefreshNavigation()
    {
        var rows = IsSideBySide ? SideBySideRows : UnifiedRows;

        // Consecutive changed rows are one logical change, so a hunk counts once.
        _changeIndices = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].HasChange) continue;
            if (i > 0 && rows[i - 1].HasChange) continue;
            _changeIndices.Add(i);
        }

        TotalChangeCount = _changeIndices.Count;
        CurrentChangeNumber = 0;
        MinimapMarkers = BuildMinimapMarkers(rows);
    }

    private static IReadOnlyList<MinimapMarker> BuildMinimapMarkers(IReadOnlyList<IDiffRow> rows)
    {
        if (rows.Count == 0) return Array.Empty<MinimapMarker>();

        var markers = new List<MinimapMarker>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (added, removed, changed) = Classify(rows[i]);
            if (!added && !removed && !changed) continue;
            markers.Add(new MinimapMarker((double)i / rows.Count, added, removed, changed));
        }

        return markers;
    }

    private static (bool Added, bool Removed, bool Changed) Classify(IDiffRow row) => row switch
    {
        DiffLineRowViewModel line => (line.IsAdded, line.IsRemoved, line.IsChanged),
        SideBySideRowViewModel pair => (
            pair.New.IsAdded,
            pair.Old.IsRemoved,
            pair.Old.IsChanged || pair.New.IsChanged),
        _ => (false, false, false)
    };

    [RelayCommand]
    private void NextChange() => GoToChange(CurrentChangeNumber >= TotalChangeCount ? 1 : CurrentChangeNumber + 1);

    [RelayCommand]
    private void PreviousChange() => GoToChange(CurrentChangeNumber <= 1 ? TotalChangeCount : CurrentChangeNumber - 1);

    private void GoToChange(int number)
    {
        if (_changeIndices.Count == 0) return;

        CurrentChangeNumber = Math.Clamp(number, 1, _changeIndices.Count);
        ScrollToRowRequested?.Invoke(_changeIndices[CurrentChangeNumber - 1]);
    }

    /// <summary>Jumps to whichever change sits nearest a 0..1 position — used by the minimap.</summary>
    public void ScrollToPosition(double position)
    {
        var rows = IsSideBySide ? SideBySideRows : UnifiedRows;
        if (rows.Count == 0 || _changeIndices.Count == 0) return;

        var targetRow = (int)Math.Round(Math.Clamp(position, 0, 1) * (rows.Count - 1));

        var nearest = 0;
        for (var i = 1; i < _changeIndices.Count; i++)
            if (Math.Abs(_changeIndices[i] - targetRow) < Math.Abs(_changeIndices[nearest] - targetRow))
                nearest = i;

        GoToChange(nearest + 1);
    }

    // -----------------------------------------------------------------------
    //  Row construction
    // -----------------------------------------------------------------------

    private static IReadOnlyList<IDiffRow> BuildSideBySideRows(SideBySideDiffModel model)
    {
        // DiffPlex pads both panes to equal length, so index i is the same visual row on both
        // sides — that's what makes a single shared scroller stay in sync.
        var count = Math.Max(model.OldText.Lines.Count, model.NewText.Lines.Count);
        var rows = new List<IDiffRow>(count);

        for (var i = 0; i < count; i++)
        {
            var oldPiece = i < model.OldText.Lines.Count ? model.OldText.Lines[i] : null;
            var newPiece = i < model.NewText.Lines.Count ? model.NewText.Lines[i] : null;

            rows.Add(new SideBySideRowViewModel
            {
                Old = BuildRow(oldPiece, isOldSide: true),
                New = BuildRow(newPiece, isOldSide: false)
            });
        }

        return rows;
    }

    private static IReadOnlyList<IDiffRow> BuildUnifiedLines(DiffPaneModel model)
    {
        var lines = new List<IDiffRow>(model.Lines.Count);
        var oldNo = 0;
        var newNo = 0;

        foreach (var piece in model.Lines)
        {
            string oldLabel;
            string newLabel;

            switch (piece.Type)
            {
                case ChangeType.Inserted:
                    oldLabel = string.Empty;
                    newLabel = (++newNo).ToString();
                    break;
                case ChangeType.Deleted:
                    oldLabel = (++oldNo).ToString();
                    newLabel = string.Empty;
                    break;
                default:
                    oldLabel = (++oldNo).ToString();
                    newLabel = (++newNo).ToString();
                    break;
            }

            lines.Add(new DiffLineRowViewModel
            {
                Segments = BuildSegments(piece),
                Marker = MarkerFor(piece.Type),
                OldLineNumber = oldLabel,
                NewLineNumber = newLabel,
                IsAdded = piece.Type == ChangeType.Inserted,
                IsRemoved = piece.Type == ChangeType.Deleted,
                IsChanged = piece.Type == ChangeType.Modified
            });
        }

        return lines;
    }

    private static DiffLineRowViewModel BuildRow(DiffPiece? piece, bool isOldSide)
    {
        if (piece == null || piece.Type == ChangeType.Imaginary)
        {
            return new DiffLineRowViewModel
            {
                Segments = Array.Empty<DiffSegment>(),
                IsRealLine = false
            };
        }

        var number = piece.Position?.ToString() ?? string.Empty;

        return new DiffLineRowViewModel
        {
            Segments = BuildSegments(piece),
            Marker = MarkerFor(piece.Type),
            OldLineNumber = isOldSide ? number : string.Empty,
            NewLineNumber = isOldSide ? string.Empty : number,
            IsAdded = piece.Type == ChangeType.Inserted,
            IsRemoved = piece.Type == ChangeType.Deleted,
            IsChanged = piece.Type == ChangeType.Modified
        };
    }

    /// <summary>Splits a line into highlighted/plain runs. Only Modified lines carry sub-pieces;
    /// everything else is a single run whose line-level background already tells the story.</summary>
    private static IReadOnlyList<DiffSegment> BuildSegments(DiffPiece piece)
    {
        if (piece.SubPieces.Count == 0)
            return new[] { new DiffSegment { Text = piece.Text ?? string.Empty } };

        return piece.SubPieces
            .Select(sub => new DiffSegment
            {
                Text = sub.Text ?? string.Empty,
                IsAdded = sub.Type == ChangeType.Inserted,
                IsRemoved = sub.Type is ChangeType.Deleted or ChangeType.Modified
            })
            .ToList();
    }

    private static string MarkerFor(ChangeType type) => type switch
    {
        ChangeType.Inserted => "+",
        ChangeType.Deleted => "−",
        ChangeType.Modified => "~",
        _ => " "
    };
}
