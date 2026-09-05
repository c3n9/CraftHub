using System.Collections.Generic;

namespace CraftHub.Core;

/// <summary>
/// Lets the JSON cell editor (the pencil-button dialog for an Object/Array cell) read and write the
/// formulas stored for that cell, without this interface project taking a dependency on the formula
/// engine. Formulas travel as plain strings keyed by a path within the cell's own sub-table
/// (<c>$[0].total</c> for an object, <c>$[2].total</c> for an array element).
/// </summary>
public interface IJsonEditorFormulaBridge
{
    /// <summary>Formulas already stored for this cell, keyed by sub-table path. Empty if none.</summary>
    IReadOnlyDictionary<string, string> LoadFormulas();

    /// <summary>Replaces every formula stored for this cell with <paramref name="formulasByLocalPath"/>
    /// and recomputes. Called when the editor is submitted.</summary>
    void SaveFormulas(IReadOnlyDictionary<string, string> formulasByLocalPath);
}
