using System.Collections.Generic;
using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes a Find & Replace pass — one or more cell edits applied as a single step.</summary>
public sealed class ReplaceAllAction : IUndoableAction
{
    public readonly record struct Change(DynamicDataRow Row, string PropName, string OldValue, string NewValue);

    private readonly IReadOnlyList<Change> _changes;
    private readonly DataGrid? _dataGrid;

    public ReplaceAllAction(IReadOnlyList<Change> changes, DataGrid? dataGrid = null)
    {
        _changes = changes;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescReplaceAll", _changes.Count);

    public void Undo()
    {
        foreach (var c in _changes)
            c.Row[c.PropName] = c.OldValue;
        ForceDataGridUpdate();
    }

    public void Redo()
    {
        foreach (var c in _changes)
            c.Row[c.PropName] = c.NewValue;
        ForceDataGridUpdate();
    }

    private void ForceDataGridUpdate()
    {
        if (_dataGrid?.ItemsSource is System.Collections.IList)
        {
            var itemsSource = _dataGrid.ItemsSource;
            _dataGrid.ItemsSource = null;
            _dataGrid.ItemsSource = itemsSource;
        }
    }
}
