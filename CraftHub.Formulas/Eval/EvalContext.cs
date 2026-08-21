using System;
using System.Threading;

namespace CraftHub.Formulas.Eval;

/// <summary>Everything the evaluator needs to run one formula: where it lives (for relative
/// references), how to fetch values for its references, which functions exist, and the guardrails
/// (depth/timeout/range-size) to run it under.</summary>
public sealed class EvalContext
{
    public required CellAddress CurrentCell { get; init; }
    public required IValueSource Values { get; init; }
    public required IFunctionRegistry Functions { get; init; }
    public EvalLimits Limits { get; init; } = EvalLimits.Default;
    public CancellationToken Cancellation { get; init; } = CancellationToken.None;

    /// <summary>What <c>TODAY()</c>/<c>NOW()</c> read. Defaults to the host machine's local clock
    /// (per docs/TYPES.md — dates here are wall-clock ISO text, not UTC instants); overridable so
    /// tests can pin a moment instead of asserting against a clock that moves mid-run.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = static () => DateTimeOffset.Now;

    private DateTimeOffset? _readingOfNow;

    /// <summary>The single moment this formula evaluation happens "at". <see cref="Clock"/> is read
    /// once and then held, so two <c>NOW()</c> calls in one formula can't disagree — without this,
    /// <c>NOW() = NOW()</c> would be false whenever the two reads straddled a tick, and
    /// <c>DAYS(TODAY(), TODAY())</c> could be 1 across midnight. A context is built per formula
    /// evaluation, so this caches for exactly as long as it should.</summary>
    public DateTimeOffset Now => _readingOfNow ??= Clock();
}
