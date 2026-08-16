using System.Collections.Generic;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

/// <summary>Shared argument-evaluation helpers so every function isn't re-implementing "evaluate
/// this, check it's a number, propagate the error if not." Every helper returns errors as values
/// (via the <c>out FormulaValue? error</c> pattern) rather than throwing — consistent with the rest
/// of the engine's "errors are values" rule.</summary>
internal static class FunctionArgs
{
    public static bool TryArity(string name, IReadOnlyList<FormulaAst> args, int min, int max, out FormulaValue error)
    {
        if (args.Count >= min && args.Count <= max)
        {
            error = default;
            return true;
        }

        var expected = min == max ? $"{min}" : $"{min}-{max}";
        error = FormulaValue.Of(FormulaErrorCode.Value, $"{name} expects {expected} argument(s), got {args.Count}.");
        return false;
    }

    public static bool TryNumber(FormulaAst arg, FunctionCallContext ctx, out decimal number, out FormulaValue error)
    {
        var v = ctx.EvalArg(arg);
        if (v.IsError) { number = 0; error = v; return false; }
        if (v.Kind == FormulaValueKind.Number) { number = v.AsNumber; error = default; return true; }
        number = 0;
        error = FormulaValue.Of(FormulaErrorCode.Value, $"Expected a number, got {v.TypeName}.");
        return false;
    }

    public static bool TryInt(FormulaAst arg, FunctionCallContext ctx, out int value, out FormulaValue error)
    {
        if (!TryNumber(arg, ctx, out var n, out error)) { value = 0; return false; }
        value = (int)n;
        return true;
    }

    public static bool TryText(FormulaAst arg, FunctionCallContext ctx, out string text, out FormulaValue error)
    {
        var v = ctx.EvalArg(arg);
        if (v.IsError) { text = ""; error = v; return false; }
        if (v.Kind == FormulaValueKind.Text) { text = v.AsText; error = default; return true; }
        text = "";
        error = FormulaValue.Of(FormulaErrorCode.Value, $"Expected text, got {v.TypeName}.");
        return false;
    }

    public static bool TryBoolean(FormulaAst arg, FunctionCallContext ctx, out bool value, out FormulaValue error)
    {
        var v = ctx.EvalArg(arg);
        if (v.IsError) { value = false; error = v; return false; }
        if (v.Kind == FormulaValueKind.Boolean) { value = v.AsBoolean; error = default; return true; }
        value = false;
        error = FormulaValue.Of(FormulaErrorCode.Value, $"Expected TRUE/FALSE, got {v.TypeName}.");
        return false;
    }

    /// <summary>Evaluates every argument (expanding ranges via EvalArgMany), skipping Missing and
    /// Null, and collects the numbers. Any Text/Boolean/Array/Object value found is a hard error —
    /// see docs/TYPES.md: aggregates skip "nothing here," they don't skip "here, but wrong type."</summary>
    public static bool TryAggregateNumbers(IReadOnlyList<FormulaAst> args, FunctionCallContext ctx,
        out List<decimal> numbers, out FormulaValue error)
    {
        numbers = new List<decimal>();
        foreach (var arg in args)
        {
            foreach (var v in ctx.EvalArgMany(arg))
            {
                if (v.IsError) { error = v; return false; }
                if (v.IsMissingOrNull) continue;
                if (v.Kind != FormulaValueKind.Number)
                {
                    error = FormulaValue.Of(FormulaErrorCode.Value, $"Expected a number, got {v.TypeName}.");
                    return false;
                }
                numbers.Add(v.AsNumber);
            }
        }
        error = default;
        return true;
    }

    /// <summary>Same as <see cref="TryAggregateNumbers"/> but keeps every non-error value (any
    /// kind) instead of requiring numbers — for COUNTA/ISBLANK-family functions that count or
    /// inspect regardless of type.</summary>
    public static bool TryAggregateValues(IReadOnlyList<FormulaAst> args, FunctionCallContext ctx,
        out List<FormulaValue> values, out FormulaValue error)
    {
        values = new List<FormulaValue>();
        foreach (var arg in args)
        {
            foreach (var v in ctx.EvalArgMany(arg))
            {
                if (v.IsError) { error = v; return false; }
                values.Add(v);
            }
        }
        error = default;
        return true;
    }
}
