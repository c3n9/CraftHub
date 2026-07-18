using System.Collections.ObjectModel;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes a single row inserted at a specific index (e.g. "insert after selected").</summary>
public sealed class InsertRowAction : IUndoableAction
{
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly DynamicDataRow _row;
    private readonly int _index;

    public InsertRowAction(ObservableCollection<DynamicDataRow> rows, DynamicDataRow row, int index)
    {
        _rows = rows;
        _row = row;
        _index = index;
    }

    public string Description => Localizer.Get("UndoDescInsertRow");

    public void Undo()
    {
        _rows.Remove(_row);
    }

    public void Redo()
    {
        var idx = _index >= 0 && _index <= _rows.Count ? _index : _rows.Count;
        _rows.Insert(idx, _row);
    }
}
