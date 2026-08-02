using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Core;
using CraftHub.Helpers;

namespace CraftHub.Services;

/// <summary>One row of the visual undo/redo timeline. <see cref="Index"/> is the number of
/// actions applied to reach this point — pass it straight to <see cref="UndoRedoService.JumpTo"/>.</summary>
public sealed record HistoryEntry(int Index, string Description, bool IsCurrent);

public sealed partial class UndoRedoService : ObservableObject
{
    private const int MaxHistory = 100;

    private readonly LinkedList<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    /// <summary>
    /// Raised whenever the data state changes through an action (Push / Undo / Redo).
    /// Used by the workspace to flag unsaved changes. Not raised by <see cref="Clear"/>.
    /// </summary>
    public event Action? StateChanged;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private string? _undoDescription;

    [ObservableProperty]
    private string? _redoDescription;

    [ObservableProperty]
    private IReadOnlyList<HistoryEntry> _historyEntries = Array.Empty<HistoryEntry>();

    public UndoRedoService()
    {
        UpdateProperties();
    }

    /// <summary>Record a new action (clears redo stack).</summary>
    public void Push(IUndoableAction action)
    {
        _undo.AddLast(action);
        if (_undo.Count > MaxHistory)
            _undo.RemoveFirst();

        _redo.Clear();
        UpdateProperties();
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var action = _undo.Last!.Value;
        _undo.RemoveLast();
        action.Undo();
        _redo.Push(action);
        UpdateProperties();
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var action = _redo.Pop();
        action.Redo();
        _undo.AddLast(action);
        UpdateProperties();
        StateChanged?.Invoke();
    }

    /// <summary>Clear both stacks (e.g. after a destructive import).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        UpdateProperties();
    }

    /// <summary>Jumps straight to an arbitrary point in the timeline (0 = before any action)
    /// by replaying Undo/Redo the necessary number of times.</summary>
    public void JumpTo(int index)
    {
        var target = Math.Clamp(index, 0, _undo.Count + _redo.Count);
        while (_undo.Count > target) Undo();
        while (_undo.Count < target) Redo();
    }

    private void UpdateProperties()
    {
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
        UndoDescription = _undo.Last?.Value.Description;
        RedoDescription = _redo.Count > 0 ? _redo.Peek().Description : null;

        // _redo enumerates top-first, which for a Stack filled purely by Undo() calls is already
        // in forward chronological order (top = next action to redo) — no reversal needed.
        var applied = _undo.Count;
        var entries = new List<HistoryEntry> { new(0, Localizer.Get("HistoryInitialState"), applied == 0) };
        var i = 1;
        foreach (var action in _undo)
            entries.Add(new HistoryEntry(i, action.Description, i++ == applied));
        foreach (var action in _redo)
            entries.Add(new HistoryEntry(i, action.Description, i++ == applied));
        HistoryEntries = entries;
    }
}