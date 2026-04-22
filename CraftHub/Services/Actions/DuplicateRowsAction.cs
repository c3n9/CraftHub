using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public sealed class DuplicateRowsAction : IUndoableAction
{
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly List<DynamicDataRow> _duplicated;
    private readonly DynamicDataRow? _insertAfter; 

    public DuplicateRowsAction(ObservableCollection<DynamicDataRow> rows,
        IEnumerable<DynamicDataRow> duplicated,
        DynamicDataRow? insertAfter = null)
    {
        _rows = rows;
        _duplicated = duplicated.ToList();
        _insertAfter = insertAfter;
    }

    public string Description => Localizer.Get("UndoDescDuplicateRows", _duplicated.Count);

    public void Undo()
    {
        foreach (var row in _duplicated)
            _rows.Remove(row);
    }

    public void Redo()
    {
        if (_insertAfter == null)
        {
            foreach (var row in _duplicated)
                _rows.Add(row);
        }
        else
        {
            int idx = _rows.IndexOf(_insertAfter);
            int insertAt = idx >= 0 ? idx + 1 : _rows.Count;
            for (int i = 0; i < _duplicated.Count; i++)
                _rows.Insert(insertAt + i, _duplicated[i]);
        }
    }
}