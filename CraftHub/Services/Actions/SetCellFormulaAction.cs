using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Services.Formulas;

namespace CraftHub.Services.Actions;

/// <summary>Undoes assigning (or removing) one cell's formula. The actual before/after cell values
/// were already computed once by <see cref="FormulaSessionService"/> when the formula was set —
/// this action only replays that captured <see cref="FormulaChangeSet"/>, it never re-parses or
/// re-evaluates anything.</summary>
public sealed class SetCellFormulaAction : IUndoableAction
{
    private readonly FormulaSessionService _session;
    private readonly FormulaChangeSet _changeSet;
    private readonly DataGrid? _dataGrid;

    public SetCellFormulaAction(FormulaSessionService session, FormulaChangeSet changeSet, DataGrid? dataGrid = null)
    {
        _session = session;
        _changeSet = changeSet;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescSetFormula", _changeSet.ColumnKey);

    public void Undo()
    {
        _session.ApplyChangeSet(_changeSet, redo: false);
        ForceDataGridUpdate();
    }

    public void Redo()
    {
        _session.ApplyChangeSet(_changeSet, redo: true);
        ForceDataGridUpdate();
    }

    private void ForceDataGridUpdate()
    {
        if (_dataGrid?.ItemsSource is System.Collections.IList)
        {
            var itemsSource = _dataGrid.ItemsSource;
            _dataGrid.ItemsSource = null;
            _dataGrid.ItemsSource = itemsSource;
        }
    }
}
