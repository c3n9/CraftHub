using System.Collections.Generic;
using System.Linq;
using CraftHub.Core;
using CraftHub.Formulas.Sidecar;

namespace CraftHub.Services.Formulas;

/// <summary>
/// Connects the JSON cell editor to a <see cref="FormulaSessionService"/>: formulas typed inside the
/// dialog for an Object/Array cell are read from and written back to the document's sidecar, keyed
/// by their path within that cell (<see cref="FormulaSessionService.ReadNestedScope"/> /
/// <see cref="FormulaSessionService.WriteNestedScope"/>).
/// </summary>
public sealed class NestedFormulaBridge : IJsonEditorFormulaBridge
{
    private readonly FormulaSessionService _session;
    private readonly string _basePath;
    private readonly int _rowIndex;
    private readonly string _columnName;
    private readonly bool _isArray;

    public NestedFormulaBridge(FormulaSessionService session, int rowIndex, string columnName, bool isArray)
    {
        _session = session;
        _rowIndex = rowIndex;
        _columnName = columnName;
        _isArray = isArray;
        _basePath = session.NestedBasePathFor(rowIndex, columnName);
    }

    public IReadOnlyDictionary<string, string> LoadFormulas() =>
        _session.ReadNestedScope(_basePath).CellFormulas
            .ToDictionary(kv => kv.Key, kv => kv.Value.Formula);

    public void SaveFormulas(IReadOnlyDictionary<string, string> formulasByLocalPath) =>
        _session.WriteNestedScope(_basePath, _rowIndex, _columnName, _isArray,
            formulasByLocalPath.ToDictionary(kv => kv.Key, kv => new FormulaEntry(kv.Value)),
            new Dictionary<string, CellState>());
}
