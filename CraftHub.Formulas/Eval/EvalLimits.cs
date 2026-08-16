using System;

namespace CraftHub.Formulas.Eval;

/// <summary>Guards against runaway formulas — a self-referential range, a pathologically deep
/// nested call, a range that's accidentally the whole document. Defaults are generous for normal
/// use and cheap to override per-evaluation (e.g. a shorter timeout while live-typing in the
/// formula bar vs. a full sheet recalculation).</summary>
public sealed class EvalLimits
{
    public int MaxDepth { get; init; } = 64;
    public int MaxRangeCells { get; init; } = 1_000_000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(250);

    public static EvalLimits Default { get; } = new();
}
