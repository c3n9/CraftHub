using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CraftHub.Formulas.Sidecar;

public abstract record SidecarLoadResult
{
    /// <summary>No sidecar file exists — open the document as plain JSON, no formulas, no error.</summary>
    public sealed record Absent : SidecarLoadResult;

    public sealed record Loaded(FormulaSidecar Sidecar) : SidecarLoadResult;

    /// <summary>The sidecar existed but wasn't valid — it has already been moved to
    /// <see cref="BackupPath"/> so the document still opens and no data is lost; the caller should
    /// warn the user and mention where the original file went.</summary>
    public sealed record Corrupt(string BackupPath, string Reason) : SidecarLoadResult;
}

/// <summary>
/// File-system operations for the sidecar, independent of its content — naming convention, load
/// (with the "missing is fine, corrupt gets backed up" policy from CLAUDE.md's design notes),
/// tag-along for copy/move/rename, and delete. Uses <see cref="Path.Combine"/> throughout and never
/// assumes case-sensitive or case-insensitive filesystem behavior.
/// </summary>
public static class SidecarFileIO
{
    private const string SidecarSuffix = ".formulas.json";

    /// <summary>"data.json" -> "data.formulas.json", alongside the main file.</summary>
    public static string PathFor(string mainPath)
    {
        var dir = Path.GetDirectoryName(mainPath);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(mainPath);
        var fileName = nameWithoutExt + SidecarSuffix;
        return string.IsNullOrEmpty(dir) ? fileName : Path.Combine(dir, fileName);
    }

    public static bool Exists(string mainPath) => File.Exists(PathFor(mainPath));

    public static async Task<SidecarLoadResult> LoadAsync(string mainPath, CancellationToken ct = default)
    {
        var sidecarPath = PathFor(mainPath);
        if (!File.Exists(sidecarPath)) return new SidecarLoadResult.Absent();

        string text;
        try
        {
            text = await File.ReadAllTextAsync(sidecarPath, ct);
        }
        catch (IOException ex)
        {
            return QuarantineAndReportCorrupt(sidecarPath, ex.Message);
        }

        try
        {
            return new SidecarLoadResult.Loaded(SidecarJsonSerializer.Deserialize(text));
        }
        catch (FormulaSidecarFormatException ex)
        {
            return QuarantineAndReportCorrupt(sidecarPath, ex.Message);
        }
    }

    private static SidecarLoadResult QuarantineAndReportCorrupt(string sidecarPath, string reason)
    {
        var backupPath = NextBackupPath(sidecarPath);
        try
        {
            File.Move(sidecarPath, backupPath);
        }
        catch
        {
            // Couldn't even rename it — the document still opens without formulas either way, so
            // report the failure but don't block on it.
            return new SidecarLoadResult.Corrupt(sidecarPath, reason);
        }
        return new SidecarLoadResult.Corrupt(backupPath, reason);
    }

    private static string NextBackupPath(string sidecarPath)
    {
        var candidate = sidecarPath + ".bak";
        var n = 1;
        while (File.Exists(candidate))
            candidate = $"{sidecarPath}.bak{++n}";
        return candidate;
    }

    /// <summary>Moves or copies the sidecar to sit alongside a main document that itself just got
    /// copied/moved/renamed to a new path (Save As, explorer copy, rename). A no-op if the source
    /// document has no sidecar — never throws for that case, since "no formulas here" is valid.</summary>
    public static void TagAlong(string oldMainPath, string newMainPath, bool move)
    {
        var oldSidecar = PathFor(oldMainPath);
        if (!File.Exists(oldSidecar)) return;

        var newSidecar = PathFor(newMainPath);
        if (string.Equals(Path.GetFullPath(oldSidecar), Path.GetFullPath(newSidecar), StringComparison.OrdinalIgnoreCase))
            return;

        if (move) File.Move(oldSidecar, newSidecar, overwrite: true);
        else File.Copy(oldSidecar, newSidecar, overwrite: true);
    }

    public static void Delete(string mainPath)
    {
        var path = PathFor(mainPath);
        if (File.Exists(path)) File.Delete(path);
    }
}
