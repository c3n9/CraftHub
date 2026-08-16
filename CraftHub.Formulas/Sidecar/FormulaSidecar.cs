using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace CraftHub.Formulas.Sidecar;

public sealed record FormulaEntry(string Formula);

/// <summary>Cached result of the last evaluation of one cell — not a source of truth (the sidecar
/// is fully rebuildable from ColumnFormulas/CellFormulas alone), just what lets a reopened document
/// show a red cell with a real tooltip immediately, before any recalculation runs.</summary>
public sealed record CellState(string ErrorCode, string Message, DateTime ComputedAtUtc);

public enum RecalcOnOpenPolicy { Never, IfHashMismatch, Always }

public sealed record SidecarLimits(int MaxRangeCells, int MaxDepth, int FormulaTimeoutMs)
{
    public static SidecarLimits Default { get; } = new(1_000_000, 64, 250);
}

public sealed record SidecarOptions(RecalcOnOpenPolicy RecalcOnOpen, SidecarLimits Limits)
{
    public static SidecarOptions Default { get; } = new(RecalcOnOpenPolicy.IfHashMismatch, SidecarLimits.Default);
}

/// <summary>Which main document this sidecar belongs to, and what it looked like the last time
/// this sidecar was written — see <see cref="TargetHash"/> for what "Hash" actually covers.</summary>
public sealed record TargetInfo(string FileName, string Hash, string HashInput, DateTime SavedAtUtc);

public sealed record GeneratorInfo(string App, string Version);

/// <summary>
/// In-memory model of a <c>*.formulas.json</c> sidecar — see docs/SIDECAR.md for the on-disk shape
/// and a full example. A few things worth knowing about the model specifically (as opposed to the
/// file format):
///
/// <list type="bullet">
/// <item><see cref="ColumnFormulas"/> is keyed by the bare column key ("price"), not by the
/// <c>$[*].price</c> template text the file uses — <c>[*]</c> isn't a resolvable path (there's no
/// concrete row), so typing it as a real path would be a category error. Only
/// <see cref="SidecarJsonSerializer"/> knows about the on-disk <c>$[*].</c> spelling.</item>
/// <item><see cref="CellFormulas"/> and <see cref="State"/> ARE keyed by genuine path text
/// (<c>$[3].total</c>) — these resolve to a real cell, so <c>JsonPath.Parse</c> works on them.</item>
/// <item>Mutable by design (plain <see cref="Dictionary{TKey,TValue}"/>, not immutable
/// collections) — undo/redo needs to snapshot and restore this alongside the row data it goes with.</item>
/// </list>
/// </summary>
public sealed class FormulaSidecar
{
    public int SchemaVersion { get; set; } = 1;
    public GeneratorInfo Generator { get; set; } = new("CraftHub", "");
    public required TargetInfo Target { get; set; }
    public SidecarOptions Options { get; set; } = SidecarOptions.Default;

    public Dictionary<string, FormulaEntry> ColumnFormulas { get; } = new();
    public Dictionary<string, FormulaEntry> CellFormulas { get; } = new();
    public Dictionary<string, CellState> State { get; } = new();

    /// <summary>Top-level JSON fields this version of the app doesn't recognize, preserved
    /// verbatim on re-save — a sidecar written by a newer app version doesn't lose data just
    /// because an older version happened to open and re-save it.</summary>
    public JsonObject? UnknownFields { get; set; }

    public bool HasAnyFormulas => ColumnFormulas.Count > 0 || CellFormulas.Count > 0;

    /// <summary>"Отсоединить формулы" — every computed cell's last value already lives in the
    /// document's normal data (that's what a formula result IS), so detaching needs no data
    /// mutation at all: just stop treating any path as a formula. The caller still deletes the
    /// sidecar file itself (see <see cref="SidecarFileIO.Delete"/>) and should push this as part of
    /// an undoable action, since simply clearing these dictionaries is easy to reverse.</summary>
    public void DetachAll()
    {
        ColumnFormulas.Clear();
        CellFormulas.Clear();
        State.Clear();
    }
}
