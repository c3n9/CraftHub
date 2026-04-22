

using System.Collections.ObjectModel;
using CraftHub.Domain.Enums;
using CraftHub.Domain.Models;

namespace CraftHub.Core;

public interface IDialogService
{
    /// <summary>Show the JSON field mapping dialog and return user-selected mappings.</summary>
    Task<List<JsonFieldMapping>?> ShowFieldMappingDialogAsync(List<JsonFieldMapping> fields, string? fileName = null);

    /// <summary>Show a message box.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Show a confirmation dialog.</summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>Show an input dialog and return user text.</summary>
    Task<string?> ShowInputDialogAsync(string title, string message, string initialValue, string? placeholder = null);

    /// <summary>Show a selection dialog and return the chosen item, or null if cancelled.</summary>
    Task<string?> ShowSelectDialogAsync(string title, string message, string fileName, List<string> options);

    /// <summary>Copy text to clipboard.</summary>
    Task CopyToClipboardAsync(string text);

    /// <summary>Open a visual nested editor for JSON.</summary>
    /// <param name="sharedProperties">
    ///   Schema shared across all cells of the same column.
    ///   When non-null the editor will use (and modify) this collection instead of
    ///   auto-detecting fields, so every cell in the column stays in sync.
    /// </param>
    Task<string?> ShowJsonEditorDialogAsync(string title, string initialJson, JsonFieldType type, IJsonService jsonService, IReadOnlyList<JsonPropertyDefinition>? sharedProperties = null);

    Task<ProgressResult> ShowProgressDialogAsync(string title, Func<IProgress<UpdateProgress>, CancellationToken, Task> task);
    Task<string?> GetFromClipboardAsync();
}
