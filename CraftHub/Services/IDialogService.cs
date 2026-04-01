using System.Collections.Generic;
using System.Threading.Tasks;
using CraftHub.Models;

namespace CraftHub.Services;

public interface IDialogService
{
    /// <summary>Show the JSON field mapping dialog and return user-selected mappings.</summary>
    Task<List<JsonFieldMapping>?> ShowFieldMappingDialogAsync(List<JsonFieldMapping> fields);

    /// <summary>Show a message box.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Show a confirmation dialog.</summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>Copy text to clipboard.</summary>
    Task CopyToClipboardAsync(string text);
}
