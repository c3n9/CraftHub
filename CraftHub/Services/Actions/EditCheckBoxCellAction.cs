using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

public sealed class EditCheckBoxCellAction : IUndoableAction
{
    private readonly DynamicDataRow _row;
    private readonly string _propName;
    private readonly bool? _oldValue;
    private readonly CellKind _oldKind;
    private readonly bool? _newValue;
    private readonly DataGrid? _dataGrid;

    public EditCheckBoxCellAction(DynamicDataRow row, string propName, bool? oldValue, bool? newValue, DataGrid? dataGrid = null)
        : this(row, propName, oldValue, CellKind.Empty, newValue, dataGrid)
    {
    }

    // A bool column with a Null/Missing cell displays unchecked either way (no tri-state checkbox),
    // but the underlying kind is still worth restoring on undo for round-trip fidelity with the file.
    public EditCheckBoxCellAction(DynamicDataRow row, string propName, bool? oldValue, CellKind oldKind, bool? newValue, DataGrid? dataGrid = null)
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
        _row.SetCell(_propName, _oldValue?.ToString().ToLower() ?? "", _oldKind);
        ForceDataGridUpdate();
    }

    public void Redo()
    {
        _row[_propName] = _newValue?.ToString().ToLower();
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
