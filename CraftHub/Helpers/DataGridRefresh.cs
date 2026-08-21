using System;
using System.Collections;
using Avalonia.Controls;

namespace CraftHub.Helpers;

/// <summary>
/// Rebuilds a <see cref="DataGrid"/>'s row visuals by cycling its ItemsSource.
///
/// Needed because parts of a cell are decided when its template runs rather than by a binding —
/// the formula "fx" marker and the fill handle — so a cell that has just started or stopped being
/// a formula keeps showing the old decoration until its row is rebuilt. Values themselves are
/// bound and need none of this.
///
/// Selection and current column are put back afterwards: clearing ItemsSource drops both, and
/// losing them moves the formula bar off whichever cell the user is working in. Every undoable
/// action used to carry its own copy of this without that part.
/// </summary>
public static class DataGridRefresh
{
    public static void Rows(DataGrid? grid)
    {
        if (grid?.ItemsSource is not IList) return;

        var selected = grid.SelectedItem;
        var currentColumn = grid.CurrentColumn;

        var itemsSource = grid.ItemsSource;
        grid.ItemsSource = null;
        grid.ItemsSource = itemsSource;

        if (selected != null) grid.SelectedItem = selected;

        // CurrentColumn can only be assigned while the grid has a current ROW, and re-selecting
        // above does not always establish one by the time we get here — with nothing selected it
        // never does. Restoring the cursor is a convenience; throwing out of a refresh because it
        // could not be restored is not acceptable, so this is best-effort.
        if (currentColumn == null || !grid.Columns.Contains(currentColumn)) return;
        try
        {
            grid.CurrentColumn = currentColumn;
        }
        catch (InvalidOperationException)
        {
            // No current row to hang it on — the cursor just starts fresh.
        }
    }
}
