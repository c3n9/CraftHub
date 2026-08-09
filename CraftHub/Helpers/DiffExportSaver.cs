using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CraftHub.Core;
using CraftHub.Models;

namespace CraftHub.Helpers;

/// <summary>
/// The save-to-file half of a diff export, shared by the "show changes" window and each pair of the
/// JSON comparer — only the content builder differs between them.
/// </summary>
public static class DiffExportSaver
{
    /// <summary>
    /// Builds off the UI thread, then prompts for a path. Produces nothing when the builder returns
    /// empty: that means there was either no change to export or no valid JSON to work from, and a
    /// zero-byte file would be worse than doing nothing.
    /// </summary>
    public static async Task SaveAsync(
        IFileDialogService fileDialogService,
        Func<string> build,
        string titleKey,
        string filterName,
        string[] patterns,
        string suggestedName,
        string extension)
    {
        var content = await Task.Run(build);
        if (string.IsNullOrEmpty(content)) return;

        var filters = new List<FileFilter> { new(filterName, patterns) };
        var name = string.IsNullOrWhiteSpace(suggestedName) ? "diff" : suggestedName;

        var path = await fileDialogService.SaveFileAsync(
            Localizer.Get(titleKey), filters, $"{name}{extension}");
        if (path == null) return;

        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }
}
