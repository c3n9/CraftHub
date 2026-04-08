using System.Collections.Generic;
using System.Threading.Tasks;

namespace CraftHub.Core;

public interface IFileDialogService
{
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter> filters);
    Task<string?> SaveFileAsync(string title, IReadOnlyList<FileFilter> filters);
}
