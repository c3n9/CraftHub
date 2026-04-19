using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes removing a property column, restoring saved cell values.</summary>
public sealed class RemovePropertyAction : IUndoableAction
{
    private readonly ObservableCollection<JsonPropertyDefinition> _props;
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly JsonPropertyDefinition _prop;
    private readonly int _propIndex;
    // Maps each row to the value it had before removal
    private readonly Dictionary<DynamicDataRow, string> _savedValues;
    private readonly Action _onColumnsChanged;

    public RemovePropertyAction(ObservableCollection<JsonPropertyDefinition> props,
        ObservableCollection<DynamicDataRow> rows,
        JsonPropertyDefinition prop,
        int propIndex,
        Dictionary<DynamicDataRow, string> savedValues,
        Action onColumnsChanged)
    {
        _props = props;
        _rows = rows;
        _prop = prop;
        _propIndex = propIndex;
        _savedValues = savedValues;
        _onColumnsChanged = onColumnsChanged;
    }

    public string Description => Localizer.Get("UndoDescRemoveProperty", _prop.Name);

    public void Undo()
    {
        _props.Insert(Math.Min(_propIndex, _props.Count), _prop);
        foreach (var row in _rows)
        {
            var val = _savedValues.TryGetValue(row, out var v) ? v : string.Empty;
            row.InitializeProperty(_prop.Name, val);
        }
        _onColumnsChanged();
    }

    public void Redo()
    {
        _props.Remove(_prop);
        foreach (var row in _rows)
            row.RemoveProperty(_prop.Name);
        _onColumnsChanged();
    }
}