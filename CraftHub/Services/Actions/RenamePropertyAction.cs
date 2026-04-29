using System;
using System.Collections.ObjectModel;
using CraftHub.Core;
using CraftHub.Domain.Models;
using CraftHub.Helpers;

namespace CraftHub.Services.Actions;

/// <summary>Undoes renaming a property column, keeping all row data intact.</summary>
public sealed class RenamePropertyAction : IUndoableAction
{
    private readonly JsonPropertyDefinition _prop;
    private readonly string _oldName;
    private readonly string _newName;
    private readonly ObservableCollection<DynamicDataRow> _rows;
    private readonly Action _onColumnsChanged;

    public RenamePropertyAction(JsonPropertyDefinition prop,
        string oldName,
        string newName,
        ObservableCollection<DynamicDataRow> rows,
        Action onColumnsChanged)
    {
        _prop = prop;
        _oldName = oldName;
        _newName = newName;
        _rows = rows;
        _onColumnsChanged = onColumnsChanged;
    }

    public string Description => Localizer.Get("UndoDescRenameProperty", _oldName, _newName);

    public void Undo()
    {
        _prop.Name = _oldName;
        foreach (var row in _rows)
            row.RenameProperty(_newName, _oldName);
        _onColumnsChanged();
    }

    public void Redo()
    {
        _prop.Name = _newName;
        foreach (var row in _rows)
            row.RenameProperty(_oldName, _newName);
        _onColumnsChanged();
    }
}