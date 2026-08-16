using Avalonia.Controls;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Services.Formulas;

namespace CraftHub.Services.Actions;

/// <summary>Undoes "Отсоединить формулы". No cell values ever change here (see
/// <see cref="FormulaSessionService.DetachAll"/>'s own doc comment) — only which paths count as
/// formulas, so undo only needs to restore the sidecar dictionaries, not any row data.</summary>
public sealed class DetachFormulasAction : IUndoableAction
{
    private readonly FormulaSessionService _session;
    private readonly FormulaSessionService.DetachSnapshot _snapshot;
    private readonly DataGrid? _dataGrid;

    public DetachFormulasAction(FormulaSessionService session, FormulaSessionService.DetachSnapshot snapshot, DataGrid? dataGrid = null)
    {
        _session = session;
        _snapshot = snapshot;
        _dataGrid = dataGrid;
    }

    public string Description => Localizer.Get("UndoDescDetachFormulas");

    public void Undo()
    {
        _session.RestoreFromDetach(_snapshot);
        DataGridRefresh.Rows(_dataGrid);
    }

    public void Redo()
    {
        _session.DetachAll();
        DataGridRefresh.Rows(_dataGrid);
    }
}
