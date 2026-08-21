using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Services.Formulas;

namespace CraftHub.Services.Actions;

/// <summary>Undoes typing a plain value into a cell its column's formula was computing. One step,
/// not two: the value and the opt-out are a single user action, and undoing only half of it would
/// leave either a value the formula immediately overwrites or a cell nothing computes.</summary>
public sealed class ExcludeCellFromColumnAction : IUndoableAction
{
    private readonly FormulaSessionService _session;
    private readonly CellExclusionChangeSet _changeSet;
    private readonly DataGrid? _dataGrid;

    public ExcludeCellFromColumnAction(FormulaSessionService session, CellExclusionChangeSet changeSet, DataGrid? dataGrid = null)
    {
        _session = session;
        _changeSet = changeSet;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescEditCell", _changeSet.ColumnKey);

    public void Undo()
    {
        _session.ApplyExclusionChangeSet(_changeSet, redo: false);
        DataGridRefresh.Rows(_dataGrid);
    }

    public void Redo()
    {
        _session.ApplyExclusionChangeSet(_changeSet, redo: true);
        DataGridRefresh.Rows(_dataGrid);
    }
}
