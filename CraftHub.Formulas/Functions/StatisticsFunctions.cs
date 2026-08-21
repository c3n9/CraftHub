using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

public static class StatisticsFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(new SimpleFunction(
            new FunctionMetadata("AVERAGE", FunctionCategory.Statistics, "Mean of its arguments, skipping null/missing values.",
                "AVERAGE(A1:A10)", new[] { new ArgSpec("values", "Numbers or ranges.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("AVERAGE", args, 1, int.MaxValue, out var arity)) return arity;
                if (!FunctionArgs.TryAggregateNumbers(args, ctx, out var nums, out var err)) return err;
                return nums.Count == 0
                    ? FormulaValue.Of(FormulaErrorCode.DivZero, "AVERAGE of an empty range.")
                    : FormulaValue.Of(nums.Average());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("MEDIAN", FunctionCategory.Statistics, "Middle value; the mean of the two middle values for an even count.",
                "MEDIAN(1,2,3,4) = 2.5", new[] { new ArgSpec("values", "Numbers or ranges.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("MEDIAN", args, 1, int.MaxValue, out var arity)) return arity;
                if (!FunctionArgs.TryAggregateNumbers(args, ctx, out var nums, out var err)) return err;
                if (nums.Count == 0) return FormulaValue.Of(FormulaErrorCode.Value, "MEDIAN of an empty range.");
                nums.Sort();
                var mid = nums.Count / 2;
                var median = nums.Count % 2 == 1 ? nums[mid] : (nums[mid - 1] + nums[mid]) / 2m;
                return FormulaValue.Of(median);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("MIN", FunctionCategory.Statistics, "Smallest value, skipping null/missing values; 0 if none.",
                "MIN(A1:A10)", new[] { new ArgSpec("values", "Numbers or ranges.", Repeating: true) }),
            (args, ctx) => MinMax(args, ctx, "MIN", nums => nums.Min())));

        r.Add(new SimpleFunction(
            new FunctionMetadata("MAX", FunctionCategory.Statistics, "Largest value, skipping null/missing values; 0 if none.",
                "MAX(A1:A10)", new[] { new ArgSpec("values", "Numbers or ranges.", Repeating: true) }),
            (args, ctx) => MinMax(args, ctx, "MAX", nums => nums.Max())));

        r.Add(new SimpleFunction(
            new FunctionMetadata("COUNT", FunctionCategory.Statistics, "Counts arguments that are numbers.",
                "COUNT(1,\"x\",3) = 2", new[] { new ArgSpec("values", "Values or ranges.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryAggregateValues(args, ctx, out var values, out var err)) return err;
                return FormulaValue.Of(values.Count(v => v.Kind == FormulaValueKind.Number));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("COUNTA", FunctionCategory.Statistics, "Counts arguments that aren't missing (null counts as present).",
                "COUNTA(A1:A10)", new[] { new ArgSpec("values", "Values or ranges.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryAggregateValues(args, ctx, out var values, out var err)) return err;
                return FormulaValue.Of(values.Count(v => !v.IsMissing));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("COUNTBLANK", FunctionCategory.Statistics, "Counts arguments that are missing or null.",
                "COUNTBLANK(A1:A10)", new[] { new ArgSpec("values", "Values or ranges.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryAggregateValues(args, ctx, out var values, out var err)) return err;
                return FormulaValue.Of(values.Count(v => v.IsMissingOrNull));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("COUNTIF", FunctionCategory.Statistics, "Counts range cells matching a criteria (e.g. \">10\", \"apple\").",
                "COUNTIF(A1:A10, \">5\")",
                new[] { new ArgSpec("range", "Values to test."), new ArgSpec("criteria", "Match criteria.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("COUNTIF", args, 2, 2, out var arity)) return arity;
                if (!TryCriteria(args[1], ctx, out var criteria, out var err)) return err;
                if (!FunctionArgs.TryAggregateValues(new[] { args[0] }, ctx, out var values, out var err2)) return err2;
                return FormulaValue.Of(values.Count(v => MatchesCriteria(v, criteria)));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("SUMIF", FunctionCategory.Statistics, "Sums a (optionally different) range where the criteria range matches.",
                "SUMIF(A1:A10, \">5\", B1:B10)",
                new[]
                {
                    new ArgSpec("range", "Values to test."), new ArgSpec("criteria", "Match criteria."),
                    new ArgSpec("sumRange", "Values to sum (defaults to range).", Optional: true)
                }),
            (args, ctx) => IfAggregate(args, ctx, "SUMIF", nums => nums.Sum())));

        r.Add(new SimpleFunction(
            new FunctionMetadata("AVERAGEIF", FunctionCategory.Statistics, "Averages a (optionally different) range where the criteria range matches.",
                "AVERAGEIF(A1:A10, \">5\")",
                new[]
                {
                    new ArgSpec("range", "Values to test."), new ArgSpec("criteria", "Match criteria."),
                    new ArgSpec("avgRange", "Values to average (defaults to range).", Optional: true)
                }),
            (args, ctx) => IfAggregate(args, ctx, "AVERAGEIF", nums => nums.Count == 0 ? (decimal?)null : nums.Average())));

        r.Add(new SimpleFunction(
            new FunctionMetadata("STDEV", FunctionCategory.Statistics, "Sample standard deviation (N-1). Needs at least 2 values.",
                "STDEV(A1:A10)", new[] { new ArgSpec("values", "Numbers or ranges.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("STDEV", args, 1, int.MaxValue, out var arity)) return arity;
                if (!FunctionArgs.TryAggregateNumbers(args, ctx, out var nums, out var err)) return err;
                if (nums.Count < 2) return FormulaValue.Of(FormulaErrorCode.DivZero, "STDEV needs at least 2 values.");

                var mean = (double)nums.Average();
                var variance = nums.Sum(n => Math.Pow((double)n - mean, 2)) / (nums.Count - 1);
                return FormulaValue.Of((decimal)Math.Sqrt(variance));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("LARGE", FunctionCategory.Statistics, "The k-th largest value (k=1 is the largest).",
                "LARGE(A1:A10, 2)",
                new[] { new ArgSpec("range", "Numbers or ranges."), new ArgSpec("k", "1-based rank from the top.") }),
            (args, ctx) => Nth(args, ctx, "LARGE", descending: true)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("SMALL", FunctionCategory.Statistics, "The k-th smallest value (k=1 is the smallest).",
                "SMALL(A1:A10, 2)",
                new[] { new ArgSpec("range", "Numbers or ranges."), new ArgSpec("k", "1-based rank from the bottom.") }),
            (args, ctx) => Nth(args, ctx, "SMALL", descending: false)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("RANK", FunctionCategory.Statistics, "Rank of number within range; order=0 (default) is descending (1=largest).",
                "RANK(B2, A1:A10)",
                new[]
                {
                    new ArgSpec("number", "Value to rank."), new ArgSpec("range", "Numbers or ranges."),
                    new ArgSpec("order", "0 = descending (default), non-zero = ascending.", Optional: true)
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("RANK", args, 2, 3, out var arity)) return arity;
                if (!FunctionArgs.TryNumber(args[0], ctx, out var target, out var e1)) return e1;
                if (!FunctionArgs.TryAggregateNumbers(new[] { args[1] }, ctx, out var nums, out var e2)) return e2;

                var ascending = false;
                if (args.Count == 3)
                {
                    if (!FunctionArgs.TryNumber(args[2], ctx, out var orderNum, out var e3)) return e3;
                    ascending = orderNum != 0;
                }

                if (!nums.Contains(target))
                    return FormulaValue.Of(FormulaErrorCode.NA, "The value was not found in the range.");

                var rank = ascending
                    ? nums.Count(n => n < target) + 1
                    : nums.Count(n => n > target) + 1;
                return FormulaValue.Of(rank);
            }));
    }

    private static FormulaValue MinMax(IReadOnlyList<FormulaAst> args, FunctionCallContext ctx, string name, Func<List<decimal>, decimal> pick)
    {
        if (!FunctionArgs.TryArity(name, args, 1, int.MaxValue, out var arity)) return arity;
        if (!FunctionArgs.TryAggregateNumbers(args, ctx, out var nums, out var err)) return err;
        return FormulaValue.Of(nums.Count == 0 ? 0m : pick(nums));
    }

    private static FormulaValue Nth(IReadOnlyList<FormulaAst> args, FunctionCallContext ctx, string name, bool descending)
    {
        if (!FunctionArgs.TryArity(name, args, 2, 2, out var arity)) return arity;
        if (!FunctionArgs.TryAggregateNumbers(new[] { args[0] }, ctx, out var nums, out var e1)) return e1;
        if (!FunctionArgs.TryInt(args[1], ctx, out var k, out var e2)) return e2;

        if (k < 1 || k > nums.Count)
            return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s k is out of range for {nums.Count} value(s).");

        nums.Sort();
        if (descending) nums.Reverse();
        return FormulaValue.Of(nums[k - 1]);
    }

    private static bool TryCriteria(FormulaAst arg, FunctionCallContext ctx, out string criteria, out FormulaValue error)
    {
        var v = ctx.EvalArg(arg);
        if (v.IsError) { criteria = ""; error = v; return false; }
        criteria = CriteriaText(v);
        error = default;
        return true;
    }

    private static string CriteriaText(FormulaValue v) => v.Kind switch
    {
        FormulaValueKind.Text => v.AsText,
        FormulaValueKind.Number => v.AsNumber.ToString(CultureInfo.InvariantCulture),
        FormulaValueKind.Boolean => v.AsBoolean ? "TRUE" : "FALSE",
        _ => ""
    };

    private static readonly string[] CriteriaOperators = { ">=", "<=", "<>", ">", "<", "=" };

    private static bool MatchesCriteria(FormulaValue value, string criteria)
    {
        var op = "=";
        var rest = criteria;
        foreach (var candidate in CriteriaOperators)
        {
            if (criteria.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                rest = criteria[candidate.Length..];
                break;
            }
        }

        int cmp;
        if (value.Kind == FormulaValueKind.Number && decimal.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            cmp = value.AsNumber.CompareTo(num);
        else if (value.Kind == FormulaValueKind.Text)
            cmp = string.Compare(value.AsText, rest, StringComparison.OrdinalIgnoreCase);
        else
            return false;

        return op switch
        {
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            "<>" => cmp != 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            _ => cmp == 0
        };
    }

    private static FormulaValue IfAggregate(IReadOnlyList<FormulaAst> args, FunctionCallContext ctx, string name,
        Func<List<decimal>, decimal?> aggregate)
    {
        if (!FunctionArgs.TryArity(name, args, 2, 3, out var arity)) return arity;
        if (!TryCriteria(args[1], ctx, out var criteria, out var errC)) return errC;

        if (!FunctionArgs.TryAggregateValues(new[] { args[0] }, ctx, out var testValues, out var e1)) return e1;
        var sumSource = args.Count == 3 ? args[2] : args[0];
        if (!FunctionArgs.TryAggregateValues(new[] { sumSource }, ctx, out var sumValues, out var e2)) return e2;

        if (testValues.Count != sumValues.Count)
            return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s ranges must be the same size.");

        var matched = new List<decimal>();
        for (var i = 0; i < testValues.Count; i++)
        {
            if (!MatchesCriteria(testValues[i], criteria)) continue;
            var v = sumValues[i];
            if (v.IsMissingOrNull) continue;
            if (v.Kind != FormulaValueKind.Number)
                return FormulaValue.Of(FormulaErrorCode.Value, $"{name} expects numbers in the sum range.");
            matched.Add(v.AsNumber);
        }

        var result = aggregate(matched);
        return result is null
            ? FormulaValue.Of(FormulaErrorCode.DivZero, $"{name} matched no numeric values.")
            : FormulaValue.Of(result.Value);
    }
}
