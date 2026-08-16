using System.Collections.Generic;
using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Services.Formulas;

namespace CraftHub.Services.Actions;

/// <summary>Undoes a fill-down — one <see cref="FormulaChangeSet"/> per row the formula was copied
/// to, replayed together as a single undo step.</summary>
public sealed class FillDownAction : IUndoableAction
{
    private readonly FormulaSessionService _session;
    private readonly IReadOnlyList<FormulaChangeSet> _changeSets;
    private readonly DataGrid? _dataGrid;

    public FillDownAction(FormulaSessionService session, IReadOnlyList<FormulaChangeSet> changeSets, DataGrid? dataGrid = null)
    {
        _session = session;
        _changeSets = changeSets;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescFillDown", _changeSets.Count);

    public void Undo()
    {
        foreach (var changeSet in _changeSets)
            _session.ApplyChangeSet(changeSet, redo: false);
        DataGridRefresh.Rows(_dataGrid);
    }

    public void Redo()
    {
        foreach (var changeSet in _changeSets)
            _session.ApplyChangeSet(changeSet, redo: true);
        DataGridRefresh.Rows(_dataGrid);
    }
}
