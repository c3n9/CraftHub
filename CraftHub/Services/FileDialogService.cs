using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CraftHub.Core;

namespace CraftHub.Services;

public class FileDialogService : IFileDialogService
{
    private IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.StorageProvider;
        return null;
    }

    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var sp = GetStorageProvider();
        if (sp == null) return null;

        var avaloniaFilters = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToList();

        var result = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = avaloniaFilters
        });

        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<IReadOnlyList<string>> OpenMultipleFilesAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var sp = GetStorageProvider();
        if (sp == null) return Array.Empty<string>();

        var avaloniaFilters = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToList();

        var result = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = avaloniaFilters
        });

        return result.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> SaveFileAsync(string title, IReadOnlyList<FileFilter> filters, string suggestedFileName)
    {
        var sp = GetStorageProvider();
        if (sp == null) return null;

        var avaloniaFilters = filters.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToList();

        if (string.IsNullOrWhiteSpace(suggestedFileName))
            suggestedFileName = "newFile";

        var result = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = avaloniaFilters
        });

        return result?.Path.LocalPath;
    }
}
