using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Helpers;
using System.ComponentModel;

namespace CraftHub.Services.Actions;

/// <summary>Undoes a plain-text or bool cell edit (in-place value change).</summary>
public sealed class EditCellAction : IUndoableAction
{
    private readonly DynamicDataRow _row;
    private readonly string _propName;
    private readonly string _oldValue;
    private readonly CellKind _oldKind;
    private readonly string _newValue;
    private readonly DataGrid? _dataGrid;

    // Kept for callers that don't track kind: matches the indexer's own inference (Empty for blank
    // text), same as this action always did before CellKind existed.
    public EditCellAction(DynamicDataRow row, string propName, string oldValue, string newValue, DataGrid? dataGrid = null)
        : this(row, propName, oldValue, CellKind.Empty, newValue, dataGrid)
    {
    }

    // Restoring the OLD kind needs to be explicit: a Null or Missing cell that got typed over
    // cannot be recovered from its text alone, since both round-trip through an empty string.
    public EditCellAction(DynamicDataRow row, string propName, string oldValue, CellKind oldKind, string newValue, DataGrid? dataGrid = null)
    {
        _row = row;
        _propName = propName;
        _oldValue = oldValue;
        _oldKind = oldKind;
        _newValue = newValue;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescEditCell", _propName);

    public void Undo()
    {
        _row.SetCell(_propName, _oldValue, _oldKind);
        ForceDataGridUpdate();
    }

    public void Redo()
    {
        _row[_propName] = _newValue;
        ForceDataGridUpdate();
    }

    private void ForceDataGridUpdate()
    {
        if (_dataGrid?.ItemsSource is System.Collections.IList list)
        {
            var itemsSource = _dataGrid.ItemsSource;
            _dataGrid.ItemsSource = null;
            _dataGrid.ItemsSource = itemsSource;
        }
    }
}