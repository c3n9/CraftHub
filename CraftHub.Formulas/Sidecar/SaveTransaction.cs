using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CraftHub.Formulas.Sidecar;

/// <summary>Outcome of a <see cref="SaveTransaction"/>. <see cref="MainFileWritten"/> and
/// <see cref="SidecarFileWritten"/> describe what's actually on disk right now — after a failure,
/// the caller needs this to know whether it's safe to just retry or whether the user needs to be
/// told plainly what state their files are in.</summary>
public sealed record SaveTransactionResult(bool Success, string? FailureMessage, bool MainFileWritten, bool SidecarFileWritten)
{
    public static SaveTransactionResult Ok { get; } = new(true, null, true, true);

    public static SaveTransactionResult Failed(string message, bool mainWritten, bool sidecarWritten) =>
        new(false, message, mainWritten, sidecarWritten);
}

/// <summary>
/// Writes the main document and its sidecar together, as close to atomically as two independent
/// files on a filesystem can get. Each file is written via a temp-file-then-replace so a crash
/// mid-write never leaves a half-written file in the real path; if the main file's write succeeds
/// but the sidecar's fails, the main file is rolled back to what it was before this save started —
/// the two files never end up representing two different points in time. There is no way to make
/// this fully atomic across two files without a journal, which is why the failure message always
/// says exactly what's on disk rather than promising "nothing changed."
/// </summary>
public static class SaveTransaction
{
    public static async Task<SaveTransactionResult> ExecuteAsync(
        string mainPath, string mainContent,
        string sidecarPath, string sidecarContent,
        CancellationToken ct = default)
    {
        string? mainBackup = null;
        var mainExisted = File.Exists(mainPath);

        if (mainExisted)
        {
            mainBackup = mainPath + ".before-save.bak";
            File.Copy(mainPath, mainBackup, overwrite: true);
        }

        try
        {
            await WriteAtomicAsync(mainPath, mainContent, ct);
        }
        catch (Exception ex)
        {
            TryDelete(mainBackup);
            return SaveTransactionResult.Failed(
                $"Could not write '{Path.GetFileName(mainPath)}': {ex.Message}", mainWritten: false, sidecarWritten: false);
        }

        try
        {
            await WriteAtomicAsync(sidecarPath, sidecarContent, ct);
        }
        catch (Exception ex)
        {
            if (mainBackup is not null)
            {
                File.Copy(mainBackup, mainPath, overwrite: true);
                TryDelete(mainBackup);
                return SaveTransactionResult.Failed(
                    $"The formula sidecar failed to write, so '{Path.GetFileName(mainPath)}' was rolled back to its previous content. {ex.Message}",
                    mainWritten: false, sidecarWritten: false);
            }

            // First-ever save of this document — there's no earlier content to roll back to, so
            // the main file stays as written. Say so explicitly rather than deleting a first save.
            return SaveTransactionResult.Failed(
                $"'{Path.GetFileName(mainPath)}' was saved, but the formula sidecar failed to write: {ex.Message}",
                mainWritten: true, sidecarWritten: false);
        }

        TryDelete(mainBackup);
        return SaveTransactionResult.Ok;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var tmpPath = path + ".tmp";
        await File.WriteAllTextAsync(tmpPath, content, Encoding.UTF8, ct);

        if (File.Exists(path))
        {
            var replaceBackup = tmpPath + ".old";
            File.Replace(tmpPath, path, replaceBackup, ignoreMetadataErrors: true);
            TryDelete(replaceBackup);
        }
        else
        {
            File.Move(tmpPath, path);
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { /* best-effort cleanup of our own temp/backup file */ }
    }
}
