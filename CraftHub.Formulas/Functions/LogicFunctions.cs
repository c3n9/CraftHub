using System.Linq;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

public static class LogicFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(new SimpleFunction(
            new FunctionMetadata("IF", FunctionCategory.Logic, "Returns one of two values depending on a condition. The untaken branch is never evaluated.",
                "IF(A1>0, \"positive\", \"not positive\")",
                new[] { new ArgSpec("condition", "Boolean test."), new ArgSpec("then", "Result if true."), new ArgSpec("else", "Result if false.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("IF", args, 3, 3, out var arity)) return arity;
                if (!FunctionArgs.TryBoolean(args[0], ctx, out var cond, out var err)) return err;
                return ctx.EvalArg(cond ? args[1] : args[2]);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("IFS", FunctionCategory.Logic, "Returns the result for the first true condition, checked in order.",
                "IFS(A1<0,\"neg\", A1=0,\"zero\", TRUE,\"pos\")",
                new[] { new ArgSpec("condition/result pairs", "One or more condition, result pairs.", Repeating: true) }),
            (args, ctx) =>
            {
                if (args.Count < 2 || args.Count % 2 != 0)
                    return FormulaValue.Of(FormulaErrorCode.Value, "IFS expects pairs of (condition, result).");

                for (var i = 0; i < args.Count; i += 2)
                {
                    if (!FunctionArgs.TryBoolean(args[i], ctx, out var cond, out var err)) return err;
                    if (cond) return ctx.EvalArg(args[i + 1]);
                }
                return FormulaValue.Of(FormulaErrorCode.NA, "IFS: no condition was true.");
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("AND", FunctionCategory.Logic, "TRUE if every argument is TRUE.",
                "AND(A1>0, A1<10)", new[] { new ArgSpec("conditions", "Boolean values.", Repeating: true) }),
            (args, ctx) => Aggregate(args, ctx, "AND", (a, b) => a && b, seed: true)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("OR", FunctionCategory.Logic, "TRUE if any argument is TRUE.",
                "OR(A1<0, A1>100)", new[] { new ArgSpec("conditions", "Boolean values.", Repeating: true) }),
            (args, ctx) => Aggregate(args, ctx, "OR", (a, b) => a || b, seed: false)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("XOR", FunctionCategory.Logic, "TRUE if an odd number of arguments are TRUE.",
                "XOR(TRUE, TRUE, FALSE) = TRUE", new[] { new ArgSpec("conditions", "Boolean values.", Repeating: true) }),
            (args, ctx) => Aggregate(args, ctx, "XOR", (a, b) => a ^ b, seed: false)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("NOT", FunctionCategory.Logic, "Inverts a boolean.", "NOT(TRUE) = FALSE",
                new[] { new ArgSpec("condition", "Boolean value.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("NOT", args, 1, 1, out var arity)) return arity;
                return FunctionArgs.TryBoolean(args[0], ctx, out var v, out var err) ? FormulaValue.Of(!v) : err;
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("IFERROR", FunctionCategory.Logic, "Returns a fallback if the first argument is an error, otherwise the value itself.",
                "IFERROR(1/A1, 0)", new[] { new ArgSpec("value", "Expression to try."), new ArgSpec("fallback", "Used if value is an error.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("IFERROR", args, 2, 2, out var arity)) return arity;
                var value = ctx.EvalArg(args[0]);
                return value.IsError ? ctx.EvalArg(args[1]) : value;
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("SWITCH", FunctionCategory.Logic, "Compares an expression against a list of values, returning the first match's result.",
                "SWITCH(A1, 1,\"one\", 2,\"two\", \"other\")",
                new[] { new ArgSpec("expression, value/result pairs, [default]", "Expression, then value/result pairs, then an optional default.", Repeating: true) }),
            (args, ctx) =>
            {
                if (args.Count < 3) return FormulaValue.Of(FormulaErrorCode.Value, "SWITCH expects an expression and at least one value/result pair.");

                var expr = ctx.EvalArg(args[0]);
                if (expr.IsError) return expr;

                var i = 1;
                for (; i + 1 < args.Count; i += 2)
                {
                    var candidate = ctx.EvalArg(args[i]);
                    if (candidate.IsError) return candidate;
                    var equal = TypeRules.Equal(expr, candidate);
                    if (equal.IsError) return equal;
                    if (equal.AsBoolean) return ctx.EvalArg(args[i + 1]);
                }

                return i < args.Count
                    ? ctx.EvalArg(args[i]) // trailing default
                    : FormulaValue.Of(FormulaErrorCode.NA, "SWITCH: no value matched and there is no default.");
            }));

        r.Add(Predicate("ISBLANK", "TRUE if the value is missing (the key doesn't exist).", "ISBLANK(A1)",
            v => v.IsMissing));
        r.Add(Predicate("ISNULL", "TRUE if the value is an explicit JSON null.", "ISNULL(A1)",
            v => v.IsNull));
        r.Add(Predicate("ISNUMBER", "TRUE if the value is a number.", "ISNUMBER(A1)",
            v => v.Kind == FormulaValueKind.Number));
        r.Add(Predicate("ISTEXT", "TRUE if the value is text.", "ISTEXT(A1)",
            v => v.Kind == FormulaValueKind.Text));
        r.Add(Predicate("ISERROR", "TRUE if the value is an error.", "ISERROR(A1/0)",
            v => v.IsError));
    }

    private static SimpleFunction Predicate(string name, string description, string example, System.Func<FormulaValue, bool> test) =>
        new(new FunctionMetadata(name, FunctionCategory.Logic, description, example, new[] { new ArgSpec("value", "Value to test.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity(name, args, 1, 1, out var arity)) return arity;
                // Deliberately does NOT propagate errors from the argument (unlike arithmetic) —
                // ISERROR specifically needs to observe an error rather than short-circuit past it.
                return FormulaValue.Of(test(ctx.EvalArg(args[0])));
            });

    private static FormulaValue Aggregate(System.Collections.Generic.IReadOnlyList<Ast.FormulaAst> args,
        FunctionCallContext ctx, string name, System.Func<bool, bool, bool> combine, bool seed)
    {
        if (!FunctionArgs.TryArity(name, args, 1, int.MaxValue, out var arity)) return arity;

        var result = seed;
        foreach (var arg in args)
        {
            if (!FunctionArgs.TryBoolean(arg, ctx, out var v, out var err)) return err;
            result = combine(result, v);
        }
        return FormulaValue.Of(result);
    }
}
