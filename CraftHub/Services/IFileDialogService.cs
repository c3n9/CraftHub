using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace CraftHub.Services;

public interface IFileDialogService
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FilePickerFileType> filters);
    Task<string?> SaveFileAsync(string title, IReadOnlyList<FilePickerFileType> filters);
}
