using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Domain.Models;
using DiffPlex.DiffBuilder.Model;

namespace CraftHub.ViewModels;

/// <summary>One rendered line of the diff — a fixed "+"/"-"/" " gutter marker plus the line text,
/// styled (via IsAdded/IsRemoved bound to Classes in the view) like a GitHub Desktop diff.</summary>
public sealed class DiffLineViewModel
{
    public string Text { get; }
    public string Marker { get; }
    public bool IsAdded { get; }
    public bool IsRemoved { get; }

    public DiffLineViewModel(DiffPiece piece)
    {
        Text = piece.Text ?? string.Empty;
        IsAdded = piece.Type == ChangeType.Inserted;
        IsRemoved = piece.Type == ChangeType.Deleted;
        Marker = IsAdded ? "+" : IsRemoved ? "-" : " ";
    }
}

public sealed partial class JsonDiffViewModel : ObservableObject
{
    public string Title { get; }
    public bool IsConfirmMode { get; }
    public ObservableCollection<DiffLineViewModel> Lines { get; } = new();

    [ObservableProperty] private bool _dontShowAgain;

    public event Action<JsonDiffResult>? RequestClose;

    public JsonDiffViewModel(string title, DiffPaneModel diff, bool isConfirmMode)
    {
        Title = title;
        IsConfirmMode = isConfirmMode;

        foreach (var line in diff.Lines)
            Lines.Add(new DiffLineViewModel(line));
    }

    [RelayCommand]
    private void Proceed() => RequestClose?.Invoke(new JsonDiffResult(true, DontShowAgain));

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(new JsonDiffResult(false, DontShowAgain));
}
