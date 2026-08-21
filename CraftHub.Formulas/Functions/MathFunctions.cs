using System;
using System.Collections.Generic;
using System.Linq;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

public static class MathFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(new SimpleFunction(
            new FunctionMetadata("SUM", FunctionCategory.Math, "Adds its arguments, skipping null/missing values.",
                "SUM(A1:A10, 5)", new[] { new ArgSpec("values", "Numbers or ranges to add.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("SUM", args, 1, int.MaxValue, out var arity)) return arity;
                return FunctionArgs.TryAggregateNumbers(args, ctx, out var nums, out var err)
                    ? FormulaValue.Of(nums.Sum())
                    : err;
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("PRODUCT", FunctionCategory.Math, "Multiplies its arguments, skipping null/missing values.",
                "PRODUCT(A1:A3)", new[] { new ArgSpec("values", "Numbers or ranges to multiply.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("PRODUCT", args, 1, int.MaxValue, out var arity)) return arity;
                if (!FunctionArgs.TryAggregateNumbers(args, ctx, out var nums, out var err)) return err;
                return FormulaValue.Of(nums.Aggregate(1m, (acc, n) => acc * n));
            }));

        r.Add(Unary("ABS", "Absolute value.", "ABS(-5) = 5", Math.Abs));
        r.Add(Unary("SIGN", "-1, 0, or 1 depending on the sign of the number.", "SIGN(-5) = -1",
            x => x switch { > 0 => 1m, < 0 => -1m, _ => 0m }));
        r.Add(Unary("SQRT", "Square root. #VALUE! for a negative number.", "SQRT(9) = 3", SqrtChecked));
        r.Add(Unary("INT", "Rounds down to the nearest integer (toward negative infinity).", "INT(-1.5) = -2",
            x => Math.Floor(x)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("ROUND", FunctionCategory.Math, "Rounds to the given number of digits, half away from zero.",
                "ROUND(2.5, 0) = 3",
                new[] { new ArgSpec("number", "Value to round."), new ArgSpec("digits", "Decimal places.") }),
            (args, ctx) => RoundLike(args, ctx, "ROUND", (n, d) => Math.Round(n, Math.Max(0, d), MidpointRounding.AwayFromZero))));

        r.Add(new SimpleFunction(
            new FunctionMetadata("ROUNDUP", FunctionCategory.Math, "Rounds away from zero to the given number of digits.",
                "ROUNDUP(2.1, 0) = 3",
                new[] { new ArgSpec("number", "Value to round."), new ArgSpec("digits", "Decimal places.") }),
            (args, ctx) => RoundLike(args, ctx, "ROUNDUP", RoundUp)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("ROUNDDOWN", FunctionCategory.Math, "Truncates toward zero to the given number of digits.",
                "ROUNDDOWN(2.9, 0) = 2",
                new[] { new ArgSpec("number", "Value to round."), new ArgSpec("digits", "Decimal places.") }),
            (args, ctx) => RoundLike(args, ctx, "ROUNDDOWN", Truncate)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("TRUNC", FunctionCategory.Math, "Truncates toward zero; digits defaults to 0.",
                "TRUNC(8.9) = 8",
                new[] { new ArgSpec("number", "Value to truncate."), new ArgSpec("digits", "Decimal places.", Optional: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TRUNC", args, 1, 2, out var arity)) return arity;
                if (!FunctionArgs.TryNumber(args[0], ctx, out var n, out var e1)) return e1;
                var digits = 0;
                if (args.Count == 2 && !FunctionArgs.TryInt(args[1], ctx, out digits, out var e2)) return e2;
                return FormulaValue.Of(Truncate(n, digits));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("FLOOR", FunctionCategory.Math, "Rounds down to the nearest multiple of significance.",
                "FLOOR(2.5, 1) = 2",
                new[] { new ArgSpec("number", "Value to round."), new ArgSpec("significance", "Multiple to round to.") }),
            (args, ctx) => FloorCeiling(args, ctx, "FLOOR", Math.Floor)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("CEILING", FunctionCategory.Math, "Rounds up to the nearest multiple of significance.",
                "CEILING(2.1, 1) = 3",
                new[] { new ArgSpec("number", "Value to round."), new ArgSpec("significance", "Multiple to round to.") }),
            (args, ctx) => FloorCeiling(args, ctx, "CEILING", Math.Ceiling)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("MOD", FunctionCategory.Math, "Remainder of number/divisor; follows the divisor's sign.",
                "MOD(7, 3) = 1",
                new[] { new ArgSpec("number", "Dividend."), new ArgSpec("divisor", "Divisor.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("MOD", args, 2, 2, out var arity)) return arity;
                if (!FunctionArgs.TryNumber(args[0], ctx, out var n, out var e1)) return e1;
                if (!FunctionArgs.TryNumber(args[1], ctx, out var d, out var e2)) return e2;
                if (d == 0m) return FormulaValue.Of(FormulaErrorCode.DivZero, "MOD by zero.");
                var result = n - d * Math.Floor(n / d);
                return FormulaValue.Of(result);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("POWER", FunctionCategory.Math, "Raises a number to a power. Same as the ^ operator.",
                "POWER(2, 10) = 1024",
                new[] { new ArgSpec("number", "Base."), new ArgSpec("power", "Exponent.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("POWER", args, 2, 2, out var arity)) return arity;
                var baseVal = ctx.EvalArg(args[0]);
                var expVal = ctx.EvalArg(args[1]);
                return TypeRules.Power(baseVal, expVal);
            }));
    }

    private static SimpleFunction Unary(string name, string description, string example, Func<decimal, decimal> op) =>
        new(new FunctionMetadata(name, FunctionCategory.Math, description, example,
                new[] { new ArgSpec("number", "Value.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity(name, args, 1, 1, out var arity)) return arity;
                if (!FunctionArgs.TryNumber(args[0], ctx, out var n, out var err)) return err;
                try
                {
                    return FormulaValue.Of(op(n));
                }
                catch (ArgumentOutOfRangeException)
                {
                    return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s argument is out of range.");
                }
            });

    private static decimal SqrtChecked(decimal x)
    {
        if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
        return (decimal)Math.Sqrt((double)x);
    }

    private static FormulaValue RoundLike(IReadOnlyList<Ast.FormulaAst> args, FunctionCallContext ctx, string name,
        Func<decimal, int, decimal> round)
    {
        if (!FunctionArgs.TryArity(name, args, 2, 2, out var arity)) return arity;
        if (!FunctionArgs.TryNumber(args[0], ctx, out var n, out var e1)) return e1;
        if (!FunctionArgs.TryInt(args[1], ctx, out var digits, out var e2)) return e2;

        try
        {
            return FormulaValue.Of(round(n, digits));
        }
        catch (ArgumentOutOfRangeException)
        {
            return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s digit count is out of range.");
        }
    }

    private static decimal RoundUp(decimal n, int digits)
    {
        var factor = Pow10(digits);
        var scaled = n * factor;
        var rounded = n >= 0 ? Math.Ceiling(scaled) : Math.Floor(scaled);
        return rounded / factor;
    }

    private static decimal Truncate(decimal n, int digits)
    {
        var factor = Pow10(digits);
        return decimal.Truncate(n * factor) / factor;
    }

    private static decimal Pow10(int digits)
    {
        if (digits >= 0) return (decimal)Math.Pow(10, digits);
        return 1m / (decimal)Math.Pow(10, -digits);
    }

    private static FormulaValue FloorCeiling(IReadOnlyList<Ast.FormulaAst> args, FunctionCallContext ctx, string name,
        Func<decimal, decimal> roundToInt)
    {
        if (!FunctionArgs.TryArity(name, args, 2, 2, out var arity)) return arity;
        if (!FunctionArgs.TryNumber(args[0], ctx, out var n, out var e1)) return e1;
        if (!FunctionArgs.TryNumber(args[1], ctx, out var sig, out var e2)) return e2;

        if (sig == 0m) return FormulaValue.Of(0m);
        if (Math.Sign(n) != 0 && Math.Sign(n) != Math.Sign(sig))
            return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s number and significance must have the same sign.");

        return FormulaValue.Of(roundToInt(n / sig) * sig);
    }
}
