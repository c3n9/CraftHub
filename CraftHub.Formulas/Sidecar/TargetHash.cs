using System;
using System.Security.Cryptography;
using System.Text;

namespace CraftHub.Formulas.Sidecar;

/// <summary>
/// Computes and checks the sidecar's <c>target.hash</c> — proof that <c>data.json</c> hasn't been
/// edited outside the app since this sidecar was last written. Hashes the CANONICALIZED text, not
/// raw bytes: the caller supplies canonicalization (the app already has one, used for its own diff
/// view), specifically so re-formatting the file with a different tool — pretty-printing,
/// minifying — doesn't look like a data edit just because the whitespace changed.
/// </summary>
public static class TargetHash
{
    /// <summary>Written into <c>target.hashInput</c> so a future version that changes how
    /// canonicalization works can tell an old hash apart from a new one instead of silently
    /// misinterpreting it.</summary>
    public const string HashInputId = "canonical-json-v1";

    public static string Compute(string canonicalText)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalText);
        var hash = SHA256.HashData(bytes);
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    public static bool Matches(string canonicalText, string storedHash) =>
        string.Equals(Compute(canonicalText), storedHash, StringComparison.Ordinal);
}
