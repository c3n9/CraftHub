namespace CraftHub.Domain.Models
{
    /// <summary>Result of the JSON diff dialog when shown as a save confirmation gate.
    /// In informational (non-confirm) mode, <see cref="Proceed"/> is always true.</summary>
    public record JsonDiffResult(bool Proceed, bool DontShowAgain);
}
