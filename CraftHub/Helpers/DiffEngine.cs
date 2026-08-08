using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace CraftHub.Helpers;

/// <summary>
/// Pure text-diff operations shared by the "show changes" window and the JSON comparer.
/// Synchronous by design so it stays trivially testable — callers wrap it in Task.Run.
/// </summary>
public static class DiffEngine
{
    /// <summary>
    /// Row-aligned two-column diff. Modified lines carry word-level <c>SubPieces</c>, which is what
    /// drives the within-line highlighting — no hand-rolled word diff needed.
    /// </summary>
    public static SideBySideDiffModel ComputeSideBySide(string oldText, string newText)
        => SideBySideDiffBuilder.Diff(oldText ?? string.Empty, newText ?? string.Empty);

    /// <summary>Single-stream diff (old and new interleaved), for the unified view.</summary>
    public static DiffPaneModel ComputeUnified(string oldText, string newText)
        => InlineDiffBuilder.Diff(oldText ?? string.Empty, newText ?? string.Empty);

    /// <summary>
    /// Counts for the "+N added, −M removed, ~K changed" header. A modified line appears on both
    /// sides of a <see cref="SideBySideDiffModel"/>, so it's counted once (from the new side) and
    /// deliberately not double-counted as an add plus a remove.
    /// </summary>
    public static (int Added, int Removed, int Changed) CountChanges(SideBySideDiffModel model)
    {
        var changed = model.NewText.Lines.Count(l => l.Type == ChangeType.Modified);
        var added = model.NewText.Lines.Count(l => l.Type == ChangeType.Inserted);
        var removed = model.OldText.Lines.Count(l => l.Type == ChangeType.Deleted);
        return (added, removed, changed);
    }

    /// <summary>
    /// Renders a standard unified patch (`--- / +++ / @@` hunks) for "copy diff" and ".patch"
    /// export. DiffPlex only models the diff, it doesn't emit patch text, so hunks are assembled
    /// here: consecutive changes are grouped and padded with <paramref name="contextLines"/> of
    /// unchanged context on each side, merging groups that overlap once padded.
    /// </summary>
    public static string BuildUnifiedPatch(
        string oldText, string newText, string oldLabel = "a", string newLabel = "b", int contextLines = 3)
    {
        var model = ComputeUnified(oldText, newText);
        var lines = model.Lines;

        var changedIdx = new List<int>();
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Type is ChangeType.Inserted or ChangeType.Deleted or ChangeType.Modified)
                changedIdx.Add(i);

        var sb = new StringBuilder();
        if (changedIdx.Count == 0) return string.Empty;

        sb.Append("--- ").Append(oldLabel).Append('\n');
        sb.Append("+++ ").Append(newLabel).Append('\n');

        // Group changed line indices into hunks, merging any two groups whose context windows touch.
        var hunks = new List<(int Start, int End)>();
        var start = Math.Max(0, changedIdx[0] - contextLines);
        var end = Math.Min(lines.Count - 1, changedIdx[0] + contextLines);
        for (var k = 1; k < changedIdx.Count; k++)
        {
            var s = Math.Max(0, changedIdx[k] - contextLines);
            var e = Math.Min(lines.Count - 1, changedIdx[k] + contextLines);
            if (s <= end + 1)
            {
                end = e;
            }
            else
            {
                hunks.Add((start, end));
                start = s;
                end = e;
            }
        }
        hunks.Add((start, end));

        // Unified patch line numbers are 1-based and counted per side, skipping lines absent there.
        var oldNo = 0;
        var newNo = 0;
        var lineNumbers = new (int Old, int New)[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            switch (lines[i].Type)
            {
                case ChangeType.Inserted:
                    lineNumbers[i] = (0, ++newNo);
                    break;
                case ChangeType.Deleted:
                    lineNumbers[i] = (++oldNo, 0);
                    break;
                default:
                    lineNumbers[i] = (++oldNo, ++newNo);
                    break;
            }
        }

        foreach (var (hStart, hEnd) in hunks)
        {
            var oldCount = 0;
            var newCount = 0;
            for (var i = hStart; i <= hEnd; i++)
            {
                if (lines[i].Type != ChangeType.Inserted) oldCount++;
                if (lines[i].Type != ChangeType.Deleted) newCount++;
            }

            var oldStart = FirstNumber(lineNumbers, lines, hStart, hEnd, forOld: true);
            var newStart = FirstNumber(lineNumbers, lines, hStart, hEnd, forOld: false);

            sb.Append("@@ -").Append(oldStart).Append(',').Append(oldCount)
              .Append(" +").Append(newStart).Append(',').Append(newCount).Append(" @@\n");

            for (var i = hStart; i <= hEnd; i++)
            {
                var prefix = lines[i].Type switch
                {
                    ChangeType.Inserted => '+',
                    ChangeType.Deleted => '-',
                    _ => ' '
                };
                sb.Append(prefix).Append(lines[i].Text ?? string.Empty).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static int FirstNumber(
        (int Old, int New)[] numbers, IReadOnlyList<DiffPiece> lines, int start, int end, bool forOld)
    {
        for (var i = start; i <= end; i++)
        {
            var skip = forOld ? lines[i].Type == ChangeType.Inserted : lines[i].Type == ChangeType.Deleted;
            if (skip) continue;
            return forOld ? numbers[i].Old : numbers[i].New;
        }
        return 1; // hunk is entirely one-sided; the side with no lines starts at 1 by convention
    }
}
