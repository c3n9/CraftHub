

using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.Core;

public interface IDialogService
{
    /// <summary>Show the JSON field mapping dialog and return user-selected mappings.</summary>
    Task<List<JsonFieldMapping>?> ShowFieldMappingDialogAsync(List<JsonFieldMapping> fields);

    /// <summary>Show a message box.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Show a confirmation dialog.</summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>Show an input dialog and return user text.</summary>
    Task<string?> ShowInputDialogAsync(string title, string message, string initialValue, string? placeholder = null);

    /// <summary>Copy text to clipboard.</summary>
    Task CopyToClipboardAsync(string text);

    /// <summary>Open a visual nested editor for JSON.</summary>
    Task<string?> ShowJsonEditorDialogAsync(string title, string initialJson, JsonFieldType type, IJsonService jsonService);

    Task<ProgressResult> ShowProgressDialogAsync(string title, Func<IProgress<UpdateProgress>, CancellationToken, Task> task);
}
