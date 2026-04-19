using Avalonia.Controls;
using CraftHub.Core;
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
    private readonly string _newValue;
    private readonly DataGrid? _dataGrid;

    public EditCellAction(DynamicDataRow row, string propName, string oldValue, string newValue, DataGrid? dataGrid = null)
    {
        _row = row;
        _propName = propName;
        _oldValue = oldValue;
        _newValue = newValue;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescEditCell", _propName);

    public void Undo()
    {
        _row[_propName] = _oldValue;
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