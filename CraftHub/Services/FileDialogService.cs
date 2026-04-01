using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CraftHub.Services;

public class FileDialogService : IFileDialogService
{
    private IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.StorageProvider;
        return null;
    }

    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<FilePickerFileType> filters)
    {
        var sp = GetStorageProvider();
        if (sp == null) return null;

        var result = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });

        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> SaveFileAsync(string title, IReadOnlyList<FilePickerFileType> filters)
    {
        var sp = GetStorageProvider();
        if (sp == null) return null;

        var result = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            FileTypeChoices = filters
        });

        return result?.Path.LocalPath;
    }
}
