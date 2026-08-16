using System;
using System.IO;

namespace CraftHub.Tests;

/// <summary>A scratch directory that cleans itself up — used by tests that need real files on
/// disk (the formula sidecar's save/load round trip).</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "crafthub-app-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(Path);
    }

    public string Combine(string fileName) => System.IO.Path.Combine(Path, fileName);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
