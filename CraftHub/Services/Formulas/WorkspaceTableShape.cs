using System.Collections.Generic;
using System.Linq;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Addressing;

namespace CraftHub.Services.Formulas;

/// <summary>
/// <see cref="ITableShape"/> over a workspace's live <see cref="JsonPropertyDefinition"/> columns
/// and <see cref="DynamicDataRow"/> rows. Column keys can themselves be multi-segment paths (fields
/// expanded during import join their segments with <see cref="JsonFieldMapping.PathSeparator"/> —
/// see CLAUDE.md's "Nested JSON paths" section) — <see cref="PathForCell"/> splits those into real
/// path segments so a formula reading a nested column resolves to where the value actually lives in
/// the exported JSON, not to a single segment containing a stray control character.
/// </summary>
public sealed class WorkspaceTableShape : ITableShape
{
    private readonly IReadOnlyList<DynamicDataRow> _rows;
    private readonly IReadOnlyList<string> _columnKeys;
    private readonly HashSet<string> _knownColumns;

    public WorkspaceTableShape(IReadOnlyList<DynamicDataRow> rows, IReadOnlyList<JsonPropertyDefinition> properties)
    {
        _rows = rows;
        _columnKeys = properties.Select(p => p.Name).ToList();
        _knownColumns = new HashSet<string>(_columnKeys, System.StringComparer.Ordinal);
    }

    public int RowCount => _rows.Count;

    public IReadOnlyList<string> ColumnKeysInDisplayOrder => _columnKeys;

    public JsonPath? PathForCell(int rowIndex, string columnKey)
    {
        if (!_knownColumns.Contains(columnKey)) return null;

        var path = JsonPath.RootRow(rowIndex);
        foreach (var segment in columnKey.Split(JsonFieldMapping.PathSeparator, System.StringSplitOptions.RemoveEmptyEntries))
        {
            path = segment.StartsWith('<') && segment.EndsWith('>') && int.TryParse(segment[1..^1], out var idx)
                ? path.Append(new JsonPathSegment.Index(idx))
                : path.Append(new JsonPathSegment.Key(segment));
        }
        return path;
    }
}
