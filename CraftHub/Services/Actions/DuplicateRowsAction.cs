using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes duplication of rows (rows were appended to the end).</summary>
public sealed class DuplicateRowsAction : IUndoableAction
{
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly List<DynamicDataRow> _duplicated;

    public DuplicateRowsAction(ObservableCollection<DynamicDataRow> rows,
        IEnumerable<DynamicDataRow> duplicated)
    {
        _rows = rows;
        _duplicated = duplicated.ToList();
    }

    public string Description => Localizer.Get("UndoDescDuplicateRows", _duplicated.Count);

    public void Undo()
    {
        foreach (var row in _duplicated)
            _rows.Remove(row);
    }

    public void Redo()
    {
        foreach (var row in _duplicated)
            _rows.Add(row);
    }
}