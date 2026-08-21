using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

public static class TextFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(new SimpleFunction(
            new FunctionMetadata("CONCAT", FunctionCategory.Text, "Joins its arguments as text, with no separator. Same coercion as &.",
                "CONCAT(\"a\", 1, TRUE) = \"a1TRUE\"", new[] { new ArgSpec("values", "Text, numbers, or booleans.", Repeating: true) }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("CONCAT", args, 1, int.MaxValue, out var arity)) return arity;
                var sb = new StringBuilder();
                foreach (var arg in args)
                {
                    var v = ctx.EvalArg(arg);
                    if (v.IsError) return v;
                    if (!TryStringify(v, out var s)) return FormulaValue.Of(FormulaErrorCode.Value, $"Cannot concatenate a {v.TypeName}.");
                    sb.Append(s);
                }
                return FormulaValue.Of(sb.ToString());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("TEXTJOIN", FunctionCategory.Text, "Joins values with a delimiter, optionally skipping empty strings.",
                "TEXTJOIN(\", \", TRUE, A1:A5)",
                new[]
                {
                    new ArgSpec("delimiter", "Text inserted between values."), new ArgSpec("ignoreEmpty", "Skip empty strings when TRUE."),
                    new ArgSpec("values", "Text, numbers, or booleans.", Repeating: true)
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TEXTJOIN", args, 3, int.MaxValue, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var delimiter, out var e1)) return e1;
                if (!FunctionArgs.TryBoolean(args[1], ctx, out var ignoreEmpty, out var e2)) return e2;

                var parts = new System.Collections.Generic.List<string>();
                for (var i = 2; i < args.Count; i++)
                {
                    foreach (var v in ctx.EvalArgMany(args[i]))
                    {
                        if (v.IsError) return v;
                        if (v.IsMissingOrNull) continue;
                        if (!TryStringify(v, out var s)) return FormulaValue.Of(FormulaErrorCode.Value, $"Cannot join a {v.TypeName}.");
                        if (ignoreEmpty && s.Length == 0) continue;
                        parts.Add(s);
                    }
                }
                return FormulaValue.Of(string.Join(delimiter, parts));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("LEFT", FunctionCategory.Text, "First n characters (default 1).", "LEFT(\"hello\", 2) = \"he\"",
                new[] { new ArgSpec("text", "Source text."), new ArgSpec("n", "Character count.", Optional: true) }),
            (args, ctx) => Slice(args, ctx, "LEFT", fromLeft: true)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("RIGHT", FunctionCategory.Text, "Last n characters (default 1).", "RIGHT(\"hello\", 2) = \"lo\"",
                new[] { new ArgSpec("text", "Source text."), new ArgSpec("n", "Character count.", Optional: true) }),
            (args, ctx) => Slice(args, ctx, "RIGHT", fromLeft: false)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("MID", FunctionCategory.Text, "Substring starting at a 1-based position.", "MID(\"hello\", 2, 3) = \"ell\"",
                new[] { new ArgSpec("text", "Source text."), new ArgSpec("start", "1-based start position."), new ArgSpec("length", "Character count.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("MID", args, 3, 3, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var e1)) return e1;
                if (!FunctionArgs.TryInt(args[1], ctx, out var start, out var e2)) return e2;
                if (!FunctionArgs.TryInt(args[2], ctx, out var length, out var e3)) return e3;
                if (start < 1 || length < 0) return FormulaValue.Of(FormulaErrorCode.Value, "MID's start/length are out of range.");

                var zeroStart = start - 1;
                if (zeroStart >= text.Length) return FormulaValue.Of("");
                var take = Math.Min(length, text.Length - zeroStart);
                return FormulaValue.Of(text.Substring(zeroStart, take));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("LEN", FunctionCategory.Text, "Character count.", "LEN(\"hello\") = 5",
                new[] { new ArgSpec("text", "Source text.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("LEN", args, 1, 1, out var arity)) return arity;
                return FunctionArgs.TryText(args[0], ctx, out var text, out var err) ? FormulaValue.Of(text.Length) : err;
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("TRIM", FunctionCategory.Text, "Removes leading/trailing whitespace and collapses internal runs to a single space.",
                "TRIM(\"  a   b  \") = \"a b\"", new[] { new ArgSpec("text", "Source text.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TRIM", args, 1, 1, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var err)) return err;
                return FormulaValue.Of(Regex.Replace(text.Trim(), @"\s+", " "));
            }));

        r.Add(Unary("UPPER", "Converts to upper case.", "UPPER(\"abc\") = \"ABC\"", s => s.ToUpperInvariant()));
        r.Add(Unary("LOWER", "Converts to lower case.", "LOWER(\"ABC\") = \"abc\"", s => s.ToLowerInvariant()));

        r.Add(new SimpleFunction(
            new FunctionMetadata("REPT", FunctionCategory.Text, "Repeats text n times.", "REPT(\"ab\", 3) = \"ababab\"",
                new[] { new ArgSpec("text", "Text to repeat."), new ArgSpec("times", "Repeat count.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("REPT", args, 2, 2, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var e1)) return e1;
                if (!FunctionArgs.TryInt(args[1], ctx, out var times, out var e2)) return e2;
                if (times < 0) return FormulaValue.Of(FormulaErrorCode.Value, "REPT's count cannot be negative.");
                return FormulaValue.Of(string.Concat(Enumerable.Repeat(text, times)));
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("SUBSTITUTE", FunctionCategory.Text, "Replaces occurrences of old text with new text; optionally only the k-th occurrence.",
                "SUBSTITUTE(\"a-b-c\", \"-\", \":\") = \"a:b:c\"",
                new[]
                {
                    new ArgSpec("text", "Source text."), new ArgSpec("old", "Text to find."), new ArgSpec("new", "Replacement text."),
                    new ArgSpec("instance", "1-based occurrence to replace; all if omitted.", Optional: true)
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("SUBSTITUTE", args, 3, 4, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var e1)) return e1;
                if (!FunctionArgs.TryText(args[1], ctx, out var oldText, out var e2)) return e2;
                if (!FunctionArgs.TryText(args[2], ctx, out var newText, out var e3)) return e3;
                if (oldText.Length == 0) return FormulaValue.Of(text);

                if (args.Count == 3)
                    return FormulaValue.Of(text.Replace(oldText, newText, StringComparison.Ordinal));

                if (!FunctionArgs.TryInt(args[3], ctx, out var instance, out var e4)) return e4;
                if (instance < 1) return FormulaValue.Of(FormulaErrorCode.Value, "SUBSTITUTE's instance must be >= 1.");

                var index = -1;
                for (var occurrence = 0; occurrence < instance; occurrence++)
                {
                    index = text.IndexOf(oldText, index + 1, StringComparison.Ordinal);
                    if (index < 0) return FormulaValue.Of(text); // fewer occurrences than requested — nothing to replace
                }
                return FormulaValue.Of(text[..index] + newText + text[(index + oldText.Length)..]);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("REPLACE", FunctionCategory.Text, "Replaces a run of characters at a 1-based position.",
                "REPLACE(\"hello\", 2, 3, \"XY\") = \"hXYo\"",
                new[]
                {
                    new ArgSpec("text", "Source text."), new ArgSpec("start", "1-based start position."),
                    new ArgSpec("length", "Character count to remove."), new ArgSpec("new", "Replacement text.")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("REPLACE", args, 4, 4, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var e1)) return e1;
                if (!FunctionArgs.TryInt(args[1], ctx, out var start, out var e2)) return e2;
                if (!FunctionArgs.TryInt(args[2], ctx, out var length, out var e3)) return e3;
                if (!FunctionArgs.TryText(args[3], ctx, out var newText, out var e4)) return e4;
                if (start < 1 || length < 0) return FormulaValue.Of(FormulaErrorCode.Value, "REPLACE's start/length are out of range.");

                var zeroStart = Math.Min(start - 1, text.Length);
                var take = Math.Min(length, text.Length - zeroStart);
                return FormulaValue.Of(text[..zeroStart] + newText + text[(zeroStart + take)..]);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("FIND", FunctionCategory.Text, "1-based position of text within another, case-sensitive; #VALUE! if not found.",
                "FIND(\"lo\", \"hello\") = 4",
                new[] { new ArgSpec("find", "Text to find."), new ArgSpec("within", "Text to search."), new ArgSpec("start", "1-based start position.", Optional: true) }),
            (args, ctx) => FindLike(args, ctx, "FIND", StringComparison.Ordinal)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("SEARCH", FunctionCategory.Text, "1-based position of text within another, case-insensitive; #VALUE! if not found.",
                "SEARCH(\"LO\", \"hello\") = 4",
                new[] { new ArgSpec("find", "Text to find."), new ArgSpec("within", "Text to search."), new ArgSpec("start", "1-based start position.", Optional: true) }),
            (args, ctx) => FindLike(args, ctx, "SEARCH", StringComparison.OrdinalIgnoreCase)));

        r.Add(new SimpleFunction(
            new FunctionMetadata("SPLIT", FunctionCategory.Text, "Splits text by a delimiter into an array of text values.",
                "SPLIT(\"a,b,c\", \",\")", new[] { new ArgSpec("text", "Source text."), new ArgSpec("delimiter", "Separator.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("SPLIT", args, 2, 2, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var e1)) return e1;
                if (!FunctionArgs.TryText(args[1], ctx, out var sep, out var e2)) return e2;

                var parts = sep.Length == 0
                    ? text.Select(c => c.ToString())
                    : text.Split(sep);
                return FormulaValue.Of(parts.Select(FormulaValue.Of).ToList());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("VALUE", FunctionCategory.Text, "Parses text as a number (invariant '.' decimal point). #VALUE! if not parseable.",
                "VALUE(\"123.5\") = 123.5", new[] { new ArgSpec("text", "Text to parse.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("VALUE", args, 1, 1, out var arity)) return arity;
                if (!FunctionArgs.TryText(args[0], ctx, out var text, out var err)) return err;
                return decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                    ? FormulaValue.Of(n)
                    : FormulaValue.Of(FormulaErrorCode.Value, $"\"{text}\" is not a number.");
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("TEXT", FunctionCategory.Text, "Formats a number as text. Supports \"0\", \"0.00\"-style, and a trailing \"%\".",
                "TEXT(1234.5, \"0.00\") = \"1234.50\"",
                new[] { new ArgSpec("number", "Value to format."), new ArgSpec("format", "Format code.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TEXT", args, 2, 2, out var arity)) return arity;
                if (!FunctionArgs.TryNumber(args[0], ctx, out var n, out var e1)) return e1;
                if (!FunctionArgs.TryText(args[1], ctx, out var format, out var e2)) return e2;
                return FormatNumber(n, format);
            }));
    }

    private static SimpleFunction Unary(string name, string description, string example, Func<string, string> op) =>
        new(new FunctionMetadata(name, FunctionCategory.Text, description, example, new[] { new ArgSpec("text", "Source text.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity(name, args, 1, 1, out var arity)) return arity;
                return FunctionArgs.TryText(args[0], ctx, out var text, out var err) ? FormulaValue.Of(op(text)) : err;
            });

    private static bool TryStringify(FormulaValue v, out string text)
    {
        switch (v.Kind)
        {
            case FormulaValueKind.Text: text = v.AsText; return true;
            case FormulaValueKind.Number: text = v.AsNumber.ToString(CultureInfo.InvariantCulture); return true;
            case FormulaValueKind.Boolean: text = v.AsBoolean ? "TRUE" : "FALSE"; return true;
            default: text = ""; return false;
        }
    }

    private static FormulaValue Slice(System.Collections.Generic.IReadOnlyList<Ast.FormulaAst> args, FunctionCallContext ctx, string name, bool fromLeft)
    {
        if (!FunctionArgs.TryArity(name, args, 1, 2, out var arity)) return arity;
        if (!FunctionArgs.TryText(args[0], ctx, out var text, out var e1)) return e1;

        var n = 1;
        if (args.Count == 2 && !FunctionArgs.TryInt(args[1], ctx, out n, out var e2)) return e2;
        if (n < 0) return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s count cannot be negative.");

        var take = Math.Min(n, text.Length);
        return FormulaValue.Of(fromLeft ? text[..take] : text[(text.Length - take)..]);
    }

    private static FormulaValue FindLike(System.Collections.Generic.IReadOnlyList<Ast.FormulaAst> args, FunctionCallContext ctx,
        string name, StringComparison comparison)
    {
        if (!FunctionArgs.TryArity(name, args, 2, 3, out var arity)) return arity;
        if (!FunctionArgs.TryText(args[0], ctx, out var find, out var e1)) return e1;
        if (!FunctionArgs.TryText(args[1], ctx, out var within, out var e2)) return e2;

        var start = 1;
        if (args.Count == 3 && !FunctionArgs.TryInt(args[2], ctx, out start, out var e3)) return e3;
        if (start < 1 || start > within.Length + 1)
            return FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s start position is out of range.");

        var index = within.IndexOf(find, start - 1, comparison);
        return index < 0
            ? FormulaValue.Of(FormulaErrorCode.Value, $"\"{find}\" was not found.")
            : FormulaValue.Of(index + 1);
    }

    private static FormulaValue FormatNumber(decimal n, string format)
    {
        var isPercent = format.EndsWith('%');
        var core = isPercent ? format[..^1] : format;
        if (isPercent) n *= 100m;

        var dotIndex = core.IndexOf('.');
        var decimals = dotIndex < 0 ? 0 : core.Length - dotIndex - 1;
        if (dotIndex >= 0 && !core[..dotIndex].All(c => c == '0') || core.Any(c => c != '0' && c != '.'))
            return FormulaValue.Of(FormulaErrorCode.Value, $"Unsupported TEXT format \"{format}\".");

        var text = n.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return FormulaValue.Of(isPercent ? text + "%" : text);
    }
}
