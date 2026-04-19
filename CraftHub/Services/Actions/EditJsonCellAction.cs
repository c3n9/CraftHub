using System.Collections.ObjectModel;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>
/// Undoes a complex-type (Object/Array) cell edit.
/// The JSON editor replaces the entire row, so we track old / new row objects.
/// </summary>
public sealed class EditJsonCellAction : IUndoableAction
{
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly DynamicDataRow _oldRow;
    private readonly DynamicDataRow _newRow;
    private readonly string _propName;

    public EditJsonCellAction(ObservableCollection<DynamicDataRow> rows,
        DynamicDataRow oldRow, DynamicDataRow newRow, string propName)
    {
        _rows = rows;
        _oldRow = oldRow;
        _newRow = newRow;
        _propName = propName;
    }

    public string Description => Localizer.Get("UndoDescEditCell", _propName);

    public void Undo()
    {
        var idx = _rows.IndexOf(_newRow);
        if (idx >= 0) _rows[idx] = _oldRow;
    }

    public void Redo()
    {
        var idx = _rows.IndexOf(_oldRow);
        if (idx >= 0) _rows[idx] = _newRow;
    }
}