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
}
