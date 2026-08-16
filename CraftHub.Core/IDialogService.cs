

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

    /// <summary>Show the releases (changelog) window listing all GitHub releases.</summary>
    Task ShowReleasesDialogAsync(string? currentVersion);

    /// <summary>
    /// Shows a git-desktop-style line diff between <paramref name="oldText"/> and
    /// <paramref name="newText"/>. When <paramref name="isConfirm"/> is true the window shows
    /// Save/Cancel + a "don't show again" checkbox (used as a gate before writing to disk);
    /// otherwise it's a single Close button and the result is informational only.
    /// </summary>
    /// <summary>
    /// Shows the diff window as a modal gate before writing to disk: the same side-by-side/unified
    /// and structural views as <see cref="ShowJsonChangesWindowAsync"/>, plus Save/Cancel and a
    /// "don't show again" opt-out. Cancelling (or closing the window) reports Proceed = false.
    /// </summary>
    Task<JsonDiffResult> ShowJsonDiffAsync(string title, string oldLabel, string newLabel, string oldText, string newText);

    /// <summary>
    /// Opens the full "show changes" window comparing <paramref name="oldText"/> (last saved) with
    /// <paramref name="newText"/> (current). Non-modal: the caller keeps working while it's open,
    /// so this returns as soon as the window is shown, not when it closes.
    /// </summary>
    Task ShowJsonChangesWindowAsync(string title, string oldLabel, string newLabel, string oldText, string newText);

    /// <summary>
    /// Opens the standalone JSON comparer. Non-modal, like the changes window. The two delegates
    /// let its quick-fill buttons pull the active editor tab's current and last-saved text without
    /// this service knowing about workspaces.
    /// </summary>
    Task ShowJsonComparerAsync(Func<Task<string?>>? getCurrentDocument, Func<Task<string?>>? getBaselineDocument);

    /// <summary>
    /// Opens the formula reference — writing formulas, addressing cells and columns, and the full
    /// function list. Non-modal, so it can be left open beside the table while writing a formula.
    /// </summary>
    Task ShowFormulaReferenceAsync();

    /// <summary>
    /// Opens the settings window. Modal: settings change the whole app (theme, language), so
    /// letting the user keep editing behind them invites confusion about what applied when.
    /// The delegates back the About section: the releases and GitHub flows already live on the
    /// shell, so settings borrows them rather than growing a second copy.
    /// </summary>
    Task ShowSettingsAsync(string currentVersion, Func<Task>? showReleases, Action? openGitHub);
}
