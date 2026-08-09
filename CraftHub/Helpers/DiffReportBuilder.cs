using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using CraftHub.Domain.Models;

namespace CraftHub.Helpers;

/// <summary>
/// Human-readable reports of a structural comparison — for sending to someone who doesn't have
/// CraftHub (or the files) in front of them. Listing changes by JSON path rather than by line is
/// what makes them readable out of context.
/// </summary>
public static class DiffReportBuilder
{
    public static string BuildMarkdown(string title, string oldLabel, string newLabel, JsonDiffNode root)
    {
        var rows = Flatten(root);
        var (added, removed, changed) = Count(rows);

        var sb = new StringBuilder();
        sb.Append("# ").Append(title).Append("\n\n");
        sb.Append("- ").Append(Localizer.Get("ReportOldSide")).Append(": `").Append(oldLabel).Append("`\n");
        sb.Append("- ").Append(Localizer.Get("ReportNewSide")).Append(": `").Append(newLabel).Append("`\n");
        sb.Append("- ").Append(Localizer.Get("ReportGenerated")).Append(": ")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\n\n");
        sb.Append($"**+{added} / −{removed} / ~{changed}**\n\n");

        if (rows.Count == 0)
        {
            sb.Append(Localizer.Get("DiffNoChanges")).Append('\n');
            return sb.ToString();
        }

        sb.Append($"| {Localizer.Get("StructColFullPath")} | {Localizer.Get("StructColChange")} ")
          .Append($"| {Localizer.Get("StructColOld")} | {Localizer.Get("StructColNew")} |\n");
        sb.Append("|---|---|---|---|\n");

        foreach (var node in rows)
        {
            sb.Append("| `").Append(node.Path).Append("` | ")
              .Append(ChangeLabel(node.ChangeType)).Append(" | ")
              .Append(Cell(node.OldValue)).Append(" | ")
              .Append(Cell(node.NewValue)).Append(" |\n");
        }

        return sb.ToString();

        // A pipe or newline inside a value would break the table row.
        static string Cell(string? value) =>
            string.IsNullOrEmpty(value)
                ? "—"
                : "`" + value.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ") + "`";
    }

    public static string BuildHtml(string title, string oldLabel, string newLabel, JsonDiffNode root)
    {
        var rows = Flatten(root);
        var (added, removed, changed) = Count(rows);

        var sb = new StringBuilder();

        // Self-contained: inline CSS only, so the file opens anywhere with no assets beside it.
        sb.Append("<!doctype html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>")
          .Append(Html(title)).Append("</title>\n");
        sb.Append("""
            <style>
              body { font: 14px -apple-system, "Segoe UI", Roboto, sans-serif; margin: 32px; color: #0b1220; }
              h1 { font-size: 20px; margin: 0 0 14px; }
              .meta { color: #64748b; font-size: 13px; margin-bottom: 14px; }
              .meta code, td code { font-family: "Cascadia Code", Consolas, Menlo, monospace;
                                    font-size: 12.5px; background: #f1f5fa; padding: 1px 4px; border-radius: 3px; }
              .counts span { font-weight: 600; margin-right: 16px; }
              .added { color: #16a34a; } .removed { color: #dc2626; } .changed { color: #b45309; }
              table { border-collapse: collapse; width: 100%; margin-top: 18px; }
              th, td { border-bottom: 1px solid #e2e8f0; padding: 7px 10px;
                       text-align: left; vertical-align: top; font-size: 13px; }
              th { background: #f1f5fa; font-weight: 600; }
              .empty { color: #94a3b8; }
            </style>
            """);
        sb.Append("\n</head>\n<body>\n<h1>").Append(Html(title)).Append("</h1>\n");

        sb.Append("<div class=\"meta\">")
          .Append(Html(Localizer.Get("ReportOldSide"))).Append(": <code>").Append(Html(oldLabel))
          .Append("</code> &rarr; ")
          .Append(Html(Localizer.Get("ReportNewSide"))).Append(": <code>").Append(Html(newLabel))
          .Append("</code><br>")
          .Append(Html(Localizer.Get("ReportGenerated"))).Append(": ")
          .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
          .Append("</div>\n");

        sb.Append("<div class=\"counts\">")
          .Append($"<span class=\"added\">+{added}</span>")
          .Append($"<span class=\"removed\">−{removed}</span>")
          .Append($"<span class=\"changed\">~{changed}</span>")
          .Append("</div>\n");

        if (rows.Count == 0)
        {
            sb.Append("<p class=\"empty\">").Append(Html(Localizer.Get("DiffNoChanges")))
              .Append("</p>\n</body>\n</html>\n");
            return sb.ToString();
        }

        sb.Append("<table>\n<tr>")
          .Append("<th>").Append(Html(Localizer.Get("StructColFullPath"))).Append("</th>")
          .Append("<th>").Append(Html(Localizer.Get("StructColChange"))).Append("</th>")
          .Append("<th>").Append(Html(Localizer.Get("StructColOld"))).Append("</th>")
          .Append("<th>").Append(Html(Localizer.Get("StructColNew"))).Append("</th>")
          .Append("</tr>\n");

        foreach (var node in rows)
        {
            sb.Append("<tr>")
              .Append("<td><code>").Append(Html(node.Path)).Append("</code></td>")
              .Append("<td class=\"").Append(CssClass(node.ChangeType)).Append("\">")
              .Append(Html(ChangeLabel(node.ChangeType))).Append("</td>")
              .Append("<td>").Append(Cell(node.OldValue)).Append("</td>")
              .Append("<td>").Append(Cell(node.NewValue)).Append("</td>")
              .Append("</tr>\n");
        }

        sb.Append("</table>\n</body>\n</html>\n");
        return sb.ToString();

        static string Cell(string? value) =>
            string.IsNullOrEmpty(value) ? "<span class=\"empty\">—</span>" : "<code>" + Html(value) + "</code>";
    }

    private static string Html(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>Every changed leaf in document order — a report is a flat list, not a tree.</summary>
    private static List<JsonDiffNode> Flatten(JsonDiffNode root)
    {
        var result = new List<JsonDiffNode>();

        void Walk(JsonDiffNode node)
        {
            if (node.ChangeType != JsonDiffChangeType.Unchanged)
            {
                result.Add(node);
                return;
            }

            foreach (var child in node.Children) Walk(child);
        }

        Walk(root);
        return result;
    }

    /// <summary>Counts come from the tree, not the line diff, so they match the rows listed below
    /// them: one entry per changed value rather than per changed line of text.</summary>
    private static (int Added, int Removed, int Changed) Count(List<JsonDiffNode> rows)
    {
        var added = 0;
        var removed = 0;
        var changed = 0;

        foreach (var node in rows)
        {
            switch (node.ChangeType)
            {
                case JsonDiffChangeType.Added: added++; break;
                case JsonDiffChangeType.Removed: removed++; break;
                default: changed++; break;
            }
        }

        return (added, removed, changed);
    }

    private static string CssClass(JsonDiffChangeType type) => type switch
    {
        JsonDiffChangeType.Added => "added",
        JsonDiffChangeType.Removed => "removed",
        _ => "changed"
    };

    private static string ChangeLabel(JsonDiffChangeType type) => type switch
    {
        JsonDiffChangeType.Added => Localizer.Get("StructChangeAdded"),
        JsonDiffChangeType.Removed => Localizer.Get("StructChangeRemoved"),
        JsonDiffChangeType.Replaced => Localizer.Get("StructChangeReplaced"),
        JsonDiffChangeType.TypeChanged => Localizer.Get("StructChangeTypeChanged"),
        _ => string.Empty
    };
}
