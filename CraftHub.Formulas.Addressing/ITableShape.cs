using System.Collections.Generic;

namespace CraftHub.Formulas.Addressing;

/// <summary>
/// What <see cref="A1Translator"/> needs to know about the table to turn A1-style syntax into
/// paths: how many rows there are, which columns exist and in what display order (for letter
/// lookup), and how a (row, column) pair maps to a path. Implemented by the app
/// (WorkspaceTableShape, wrapping Rows/Properties) — this project never touches those directly,
/// only this abstraction over them.
/// </summary>
public interface ITableShape
{
    int RowCount { get; }

    /// <summary>Column keys in the order they're currently displayed — index 0 is column "A".
    /// Reflects pinning/reordering, so A1 letters always match what's on screen right now.</summary>
    IReadOnlyList<string> ColumnKeysInDisplayOrder { get; }

    /// <summary>Path for one cell, or null if <paramref name="columnKey"/> isn't a real column.
    /// <paramref name="rowIndex"/> is not bounds-checked here — <see cref="A1Translator"/> already
    /// validates it against <see cref="RowCount"/> before calling this.</summary>
    JsonPath? PathForCell(int rowIndex, string columnKey);
}
