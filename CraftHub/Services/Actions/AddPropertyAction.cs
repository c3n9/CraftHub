using System;
using System.Collections.ObjectModel;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes adding a new property column.</summary>
public sealed class AddPropertyAction : IUndoableAction
{
    private readonly ObservableCollection<JsonPropertyDefinition> _props;
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly JsonPropertyDefinition _prop;
    private readonly Action _onColumnsChanged;

    public AddPropertyAction(ObservableCollection<JsonPropertyDefinition> props,
        ObservableCollection<DynamicDataRow> rows,
        JsonPropertyDefinition prop,
        Action onColumnsChanged)
    {
        _props = props;
        _rows = rows;
        _prop = prop;
        _onColumnsChanged = onColumnsChanged;
    }

    public string Description => Localizer.Get("UndoDescAddProperty", _prop.Name);

    public void Undo()
    {
        _props.Remove(_prop);
        foreach (var row in _rows)
            row.RemoveProperty(_prop.Name);
        _onColumnsChanged();
    }

    public void Redo()
    {
        _props.Add(_prop);
        foreach (var row in _rows)
            row.InitializeProperty(_prop.Name);
        _onColumnsChanged();
    }
}