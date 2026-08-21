using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Services.Formulas;

namespace CraftHub.Services.Actions;

/// <summary>Undoes applying (or removing) a whole column's formula template. Like
/// <see cref="SetCellFormulaAction"/> this only replays the before/after that
/// <see cref="FormulaSessionService"/> already computed once — but through
/// <see cref="FormulaSessionService.ApplyColumnChangeSet"/>, which also puts back the per-cell
/// formulas that applying a column formula deliberately cleared.</summary>
public sealed class SetColumnFormulaAction : IUndoableAction
{
    private readonly FormulaSessionService _session;
    private readonly ColumnFormulaChangeSet _changeSet;
    private readonly DataGrid? _dataGrid;

    public SetColumnFormulaAction(FormulaSessionService session, ColumnFormulaChangeSet changeSet, DataGrid? dataGrid = null)
    {
        _session = session;
        _changeSet = changeSet;
        _dataGrid = dataGrid;
    }

    public string Description => _changeSet.NewColumnFormula is null
        ? Localizer.Get("UndoDescRemoveColumnFormula", _changeSet.ColumnKey)
        : Localizer.Get("UndoDescSetColumnFormula", _changeSet.ColumnKey);

    public void Undo()
    {
        _session.ApplyColumnChangeSet(_changeSet, redo: false);
        DataGridRefresh.Rows(_dataGrid);
    }

    public void Redo()
    {
        _session.ApplyColumnChangeSet(_changeSet, redo: true);
        DataGridRefresh.Rows(_dataGrid);
    }
}
