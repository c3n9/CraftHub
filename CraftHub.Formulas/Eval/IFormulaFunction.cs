using System.Collections.Generic;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Eval;

/// <summary>
/// One callable formula function. This is the minimal contract the evaluator itself needs — a
/// name to dispatch on, and a way to run it against its (still unevaluated) argument expressions.
/// Metadata for autocomplete and docs (category, arity, per-argument types, description, example)
/// is added by <c>FunctionRegistry</c> in Step 4, which wraps implementations of this interface;
/// nothing about argument evaluation changes when that lands.
/// </summary>
public interface IFormulaFunction
{
    string Name { get; }

    /// <summary>
    /// Arguments arrive as raw AST nodes, not pre-evaluated values — a function decides for itself,
    /// per argument, whether to call <see cref="FunctionCallContext.EvalArg"/> (scalar) or
    /// <see cref="FunctionCallContext.EvalArgMany"/> (range-shaped, e.g. <c>SUM(A1:A10)</c>).
    /// This is also what lets <c>IF</c>/<c>IFERROR</c>-style functions skip evaluating a branch
    /// they don't take — evaluating a formula's error side effect (there are none today, but the
    /// principle still matters for cost: an unevaluated branch costs nothing) is never forced.
    /// </summary>
    FormulaValue Invoke(IReadOnlyList<FormulaAst> arguments, FunctionCallContext context);
}

/// <summary>What a function implementation has access to: the ambient evaluation context, and the
/// two ways to pull a value out of one of its own argument expressions.</summary>
public sealed class FunctionCallContext
{
    public required EvalContext Eval { get; init; }
    public required System.Func<FormulaAst, FormulaValue> EvalArg { get; init; }
    public required System.Func<FormulaAst, IEnumerable<FormulaValue>> EvalArgMany { get; init; }
}
