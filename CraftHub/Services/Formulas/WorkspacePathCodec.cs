using System.Collections.Generic;
using System.Linq;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Addressing;

namespace CraftHub.Services.Formulas;

/// <summary>Shared (row index, column key) &lt;-&gt; <see cref="JsonPath"/> text conversion, used by
/// both <see cref="WorkspaceValueSource"/> (reading) and <see cref="FormulaSessionService"/>
/// (bookkeeping) so the two never drift apart on what a path "means".</summary>
internal static class WorkspacePathCodec
{
    /// <summary>Rejoins path segments after the leading row index into the original column key
    /// (reversing how <see cref="WorkspaceTableShape.PathForCell"/> split it).</summary>
    public static string? ColumnKeyFor(IReadOnlyList<JsonPathSegment> segmentsAfterRow)
    {
        if (segmentsAfterRow.Count == 0) return null;
        var parts = new List<string>(segmentsAfterRow.Count);
        foreach (var seg in segmentsAfterRow)
        {
            switch (seg)
            {
                case JsonPathSegment.Key k: parts.Add(k.Name); break;
                case JsonPathSegment.Index i: parts.Add($"<{i.Value}>"); break;
                default: return null;
            }
        }
        return string.Join(JsonFieldMapping.PathSeparator, parts);
    }

    /// <summary>Parses a cellFormulas/state key back into the (row, column) it targets. False for
    /// anything that isn't a concrete <c>$[N]....</c> cell path — a template, or a path outside the
    /// table entirely.</summary>
    public static bool TryTargetCell(string pathText, out int rowIndex, out string columnKey)
    {
        rowIndex = 0;
        columnKey = "";

        JsonPath parsed;
        try
        {
            parsed = JsonPath.Parse(pathText);
        }
        catch (System.FormatException)
        {
            return false;
        }

        if (parsed.Segments.Count < 2 || parsed.Segments[0] is not JsonPathSegment.Index idx)
            return false;

        var key = ColumnKeyFor(parsed.Segments.Skip(1).ToList());
        if (key is null) return false;

        rowIndex = idx.Value;
        columnKey = key;
        return true;
    }
}
