using System.Collections.ObjectModel;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes a single row added to the end of the collection.</summary>
public sealed class AddRowAction : IUndoableAction
{
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly DynamicDataRow _row;

    public AddRowAction(ObservableCollection<DynamicDataRow> rows, DynamicDataRow row)
    {
        _rows = rows;
        _row = row;
    }

    public string Description => Localizer.Get("UndoDescAddRow");

    public void Undo()
    {
        _rows.Remove(_row);
    }
    public void Redo()
    {
        _rows.Add(_row);
    }
}