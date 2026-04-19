using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes removal of one or more rows, preserving original positions.</summary>
public sealed class RemoveRowsAction : IUndoableAction
{
    private readonly ObservableCollection<DynamicDataRow> _rows;
    // Sorted ascending by index so we can re-insert in the right order
    private readonly List<(int Index, DynamicDataRow Row)> _removed;

    public RemoveRowsAction(ObservableCollection<DynamicDataRow> rows,
        IEnumerable<(int Index, DynamicDataRow Row)> removed)
    {
        _rows = rows;
        _removed = removed.OrderBy(x => x.Index).ToList();
    }

    public string Description => Localizer.Get("UndoDescRemoveRows", _removed.Count);

    public void Undo()
    {
        foreach (var (idx, row) in _removed)
            _rows.Insert(Math.Min(idx, _rows.Count), row);
    }

    public void Redo()
    {
        foreach (var (_, row) in _removed)
            _rows.Remove(row);
    }
}