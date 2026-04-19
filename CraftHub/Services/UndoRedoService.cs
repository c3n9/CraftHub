using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftHub.Core;

namespace CraftHub.Services;

public sealed partial class UndoRedoService : ObservableObject
{
    private const int MaxHistory = 100;

    private readonly LinkedList<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private string? _undoDescription;

    [ObservableProperty]
    private string? _redoDescription;

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
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var action = _undo.Last!.Value;
        _undo.RemoveLast();
        action.Undo();
        _redo.Push(action);
        UpdateProperties();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var action = _redo.Pop();
        action.Redo();
        _undo.AddLast(action);
        UpdateProperties();
    }

    /// <summary>Clear both stacks (e.g. after a destructive import).</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        UpdateProperties();
    }

    private void UpdateProperties()
    {
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
        UndoDescription = _undo.Last?.Value.Description;
        RedoDescription = _redo.Count > 0 ? _redo.Peek().Description : null;
    }
}