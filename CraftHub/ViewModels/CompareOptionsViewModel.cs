using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Domain.Models;

namespace CraftHub.ViewModels;

/// <summary>
/// Bindable comparison settings, persisted to the app's own settings file. Produces immutable
/// <see cref="JsonCompareOptions"/> snapshots for the background normalization work.
/// </summary>
public sealed partial class CompareOptionsViewModel : ObservableObject
{
    [ObservableProperty] private bool _ignoreKeyOrder;
    [ObservableProperty] private bool _ignoreArrayOrder;
    [ObservableProperty] private bool _caseInsensitiveStrings;
    [ObservableProperty] private bool _ignoreNullAndEmpty;

    /// <summary>One path per line — a text box is a better fit than a list editor for something
    /// people typically paste a handful of entries into.</summary>
    [ObservableProperty] private string _ignoredPathsText = string.Empty;

    /// <summary>Raised after any option changes, so owners can recompute their diff.</summary>
    public event Action? Changed;

    public CompareOptionsViewModel() => Load();

    public JsonCompareOptions ToSnapshot() => new(
        IgnoreKeyOrder,
        IgnoreArrayOrder,
        CaseInsensitiveStrings,
        IgnoreNullAndEmpty,
        ParsePaths(IgnoredPathsText));

    private static IReadOnlyList<string> ParsePaths(string text) =>
        text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    partial void OnIgnoreKeyOrderChanged(bool value) => Persist();
    partial void OnIgnoreArrayOrderChanged(bool value) => Persist();
    partial void OnCaseInsensitiveStringsChanged(bool value) => Persist();
    partial void OnIgnoreNullAndEmptyChanged(bool value) => Persist();
    partial void OnIgnoredPathsTextChanged(string value) => Persist();

    private bool _loading;

    private void Load()
    {
        _loading = true;
        try
        {
            var settings = global::CraftHub.Properties.Settings.Default;
            IgnoreKeyOrder = settings.CompareIgnoreKeyOrder;
            IgnoreArrayOrder = settings.CompareIgnoreArrayOrder;
            CaseInsensitiveStrings = settings.CompareCaseInsensitive;
            IgnoreNullAndEmpty = settings.CompareIgnoreNullAndEmpty;
            IgnoredPathsText = settings.CompareIgnoredPaths ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Persist()
    {
        if (_loading) return;

        var settings = global::CraftHub.Properties.Settings.Default;
        settings.CompareIgnoreKeyOrder = IgnoreKeyOrder;
        settings.CompareIgnoreArrayOrder = IgnoreArrayOrder;
        settings.CompareCaseInsensitive = CaseInsensitiveStrings;
        settings.CompareIgnoreNullAndEmpty = IgnoreNullAndEmpty;
        settings.CompareIgnoredPaths = IgnoredPathsText;
        settings.Save();

        Changed?.Invoke();
    }
}
