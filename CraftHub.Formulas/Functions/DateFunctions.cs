using System;
using System.Collections.Generic;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

/// <summary>
/// Dates, JSON-style: every one of these takes and returns ISO 8601 <em>text</em>, never Excel's
/// serial day number — see docs/TYPES.md. That single decision explains most of what looks unusual
/// here compared to Excel:
///
/// <list type="bullet">
/// <item>There is no date arithmetic through <c>+</c>/<c>-</c>, because a date is text and text
/// doesn't do arithmetic in this engine. <see cref="Register"/>'s <c>DATEADD</c>/<c>EDATE</c>/
/// <c>DAYS</c>/<c>DATEDIF</c> are the explicit doors, which is the same trade the rest of the type
/// system makes (<c>VALUE("123")</c> rather than silent coercion).</item>
/// <item>Results keep the exact shape of their input — offset, fractional-second precision, and
/// date-vs-date-time — see <see cref="IsoDateTime"/>.</item>
/// <item><c>HOUR</c> of a date-only value is an error, not <c>0</c>. Excel answers 0 because
/// every Excel date secretly *is* a date-time with a zero time; <c>"2024-03-15"</c> genuinely has
/// no hour, and inventing midnight would be exactly the kind of quiet fiction docs/TYPES.md exists
/// to prevent.</item>
/// </list>
/// </summary>
public static class DateFunctions
{
    public static void Register(FunctionRegistry r)
    {
        r.Add(new SimpleFunction(
            new FunctionMetadata("TODAY", FunctionCategory.Date, "Today's date on this machine, as \"yyyy-MM-dd\".",
                "TODAY() = \"2024-03-15\"", Array.Empty<ArgSpec>(), Volatile: true),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("TODAY", args, 0, 0, out var arity)) return arity;
                return FormulaValue.Of(IsoDateTime.FromDate(ctx.Eval.Now.LocalDateTime.Date).Format());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("NOW", FunctionCategory.Date, "The current local date and time, as \"yyyy-MM-ddTHH:mm:ss\" with no offset.",
                "NOW() = \"2024-03-15T14:30:00\"", Array.Empty<ArgSpec>(), Volatile: true),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("NOW", args, 0, 0, out var arity)) return arity;
                var local = ctx.Eval.Now.LocalDateTime;
                // Truncated to the second: sub-second precision the user never asked for would
                // make every recalculation "change" the cell and mark the document dirty.
                var whole = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second);
                return FormulaValue.Of(IsoDateTime.FromDateTime(whole).Format());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("DATE", FunctionCategory.Date, "Builds a date from year, month and day.",
                "DATE(2024, 3, 15) = \"2024-03-15\"",
                new[]
                {
                    new ArgSpec("year", "Four-digit year."),
                    new ArgSpec("month", "1-12."),
                    new ArgSpec("day", "1-31, within the given month.")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("DATE", args, 3, 3, out var arity)) return arity;
                if (!FunctionArgs.TryInt(args[0], ctx, out var year, out var e1)) return e1;
                if (!FunctionArgs.TryInt(args[1], ctx, out var month, out var e2)) return e2;
                if (!FunctionArgs.TryInt(args[2], ctx, out var day, out var e3)) return e3;

                // No roll-over: Excel turns DATE(2024,13,1) into January 2025, which quietly hides
                // an off-by-one in whatever computed the 13. Here it's an error the user can see.
                if (!TryBuild(year, month, day, 0, 0, 0, out var built))
                    return FormulaValue.Of(FormulaErrorCode.Value, $"{year}-{month}-{day} isn't a real date.");
                return FormulaValue.Of(IsoDateTime.FromDate(built).Format());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("DATETIME", FunctionCategory.Date, "Builds a date-time from year, month, day, hour, minute and second.",
                "DATETIME(2024, 3, 15, 14, 30, 0) = \"2024-03-15T14:30:00\"",
                new[]
                {
                    new ArgSpec("year", "Four-digit year."),
                    new ArgSpec("month", "1-12."),
                    new ArgSpec("day", "1-31, within the given month."),
                    new ArgSpec("hour", "0-23."),
                    new ArgSpec("minute", "0-59."),
                    new ArgSpec("second", "0-59.", Optional: true)
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("DATETIME", args, 5, 6, out var arity)) return arity;
                if (!FunctionArgs.TryInt(args[0], ctx, out var year, out var e1)) return e1;
                if (!FunctionArgs.TryInt(args[1], ctx, out var month, out var e2)) return e2;
                if (!FunctionArgs.TryInt(args[2], ctx, out var day, out var e3)) return e3;
                if (!FunctionArgs.TryInt(args[3], ctx, out var hour, out var e4)) return e4;
                if (!FunctionArgs.TryInt(args[4], ctx, out var minute, out var e5)) return e5;
                var second = 0;
                if (args.Count == 6 && !FunctionArgs.TryInt(args[5], ctx, out second, out var e6)) return e6;

                if (!TryBuild(year, month, day, hour, minute, second, out var built))
                    return FormulaValue.Of(FormulaErrorCode.Value,
                        $"{year}-{month}-{day} {hour}:{minute}:{second} isn't a real date and time.");
                return FormulaValue.Of(IsoDateTime.FromDateTime(built).Format());
            }));

        r.Add(Part("YEAR", "The year of a date.", "YEAR(\"2024-03-15\") = 2024", requiresTime: false, d => d.Value.Year));
        r.Add(Part("MONTH", "The month of a date, 1-12.", "MONTH(\"2024-03-15\") = 3", requiresTime: false, d => d.Value.Month));
        r.Add(Part("DAY", "The day of the month, 1-31.", "DAY(\"2024-03-15\") = 15", requiresTime: false, d => d.Value.Day));
        r.Add(Part("HOUR", "The hour, 0-23. #VALUE! if the value has no time part.",
            "HOUR(\"2024-03-15T14:30:00\") = 14", requiresTime: true, d => d.Value.Hour));
        r.Add(Part("MINUTE", "The minute, 0-59. #VALUE! if the value has no time part.",
            "MINUTE(\"2024-03-15T14:30:00\") = 30", requiresTime: true, d => d.Value.Minute));
        r.Add(Part("SECOND", "The second, 0-59. #VALUE! if the value has no time part.",
            "SECOND(\"2024-03-15T14:30:09\") = 9", requiresTime: true, d => d.Value.Second));

        r.Add(new SimpleFunction(
            new FunctionMetadata("WEEKDAY", FunctionCategory.Date, "Day of the week. Type 1 (default) Sunday=1..Saturday=7; type 2 Monday=1..Sunday=7; type 3 Monday=0..Sunday=6.",
                "WEEKDAY(\"2024-03-15\", 2) = 5",
                new[]
                {
                    new ArgSpec("date", "ISO date or date-time text."),
                    new ArgSpec("type", "1, 2 or 3. Defaults to 1.", Optional: true)
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("WEEKDAY", args, 1, 2, out var arity)) return arity;
                if (!TryDate("WEEKDAY", args[0], ctx, out var date, out var err)) return err;
                var type = 1;
                if (args.Count == 2 && !FunctionArgs.TryInt(args[1], ctx, out type, out var e2)) return e2;

                var sundayZero = (int)date.Value.DayOfWeek; // Sunday = 0
                return type switch
                {
                    1 => FormulaValue.Of((decimal)(sundayZero + 1)),
                    2 => FormulaValue.Of((decimal)((sundayZero + 6) % 7 + 1)),
                    3 => FormulaValue.Of((decimal)((sundayZero + 6) % 7)),
                    _ => FormulaValue.Of(FormulaErrorCode.Value, "WEEKDAY's type must be 1, 2 or 3.")
                };
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("EDATE", FunctionCategory.Date, "Shifts a date by whole months, clamping to the end of a shorter month. Keeps the input's time and offset.",
                "EDATE(\"2024-01-31\", 1) = \"2024-02-29\"",
                new[]
                {
                    new ArgSpec("date", "ISO date or date-time text."),
                    new ArgSpec("months", "Months to add; negative goes back.")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("EDATE", args, 2, 2, out var arity)) return arity;
                if (!TryDate("EDATE", args[0], ctx, out var date, out var err)) return err;
                if (!FunctionArgs.TryInt(args[1], ctx, out var months, out var e2)) return e2;
                return TryShiftMonths(date, months, out var shifted)
                    ? FormulaValue.Of(shifted.Format())
                    : OutOfRange("EDATE");
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("EOMONTH", FunctionCategory.Date, "The last day of the month, that many months away. Always returns a date, with no time part.",
                "EOMONTH(\"2024-03-15\", 0) = \"2024-03-31\"",
                new[]
                {
                    new ArgSpec("date", "ISO date or date-time text."),
                    new ArgSpec("months", "Months to add; negative goes back. 0 means this month.")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("EOMONTH", args, 2, 2, out var arity)) return arity;
                if (!TryDate("EOMONTH", args[0], ctx, out var date, out var err)) return err;
                if (!FunctionArgs.TryInt(args[1], ctx, out var months, out var e2)) return e2;
                if (!TryShiftMonths(date.ToDateOnly(), months, out var shifted)) return OutOfRange("EOMONTH");

                var v = shifted.Value;
                var lastDay = DateTime.DaysInMonth(v.Year, v.Month);
                return FormulaValue.Of(IsoDateTime.FromDate(new DateTime(v.Year, v.Month, lastDay)).Format());
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("DAYS", FunctionCategory.Date, "Whole days from start to end (negative if end is earlier). Compares the calendar dates as written.",
                "DAYS(\"2024-03-15\", \"2024-03-01\") = 14",
                new[]
                {
                    new ArgSpec("end", "The later date."),
                    new ArgSpec("start", "The earlier date.")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("DAYS", args, 2, 2, out var arity)) return arity;
                if (!TryDate("DAYS", args[0], ctx, out var end, out var e1)) return e1;
                if (!TryDate("DAYS", args[1], ctx, out var start, out var e2)) return e2;
                if (!SameOffsetBasis(start, end, out var mismatch)) return mismatch;
                return FormulaValue.Of((decimal)(end.Value.Date - start.Value.Date).Days);
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("DATEDIF", FunctionCategory.Date, "Complete years (\"y\"), months (\"m\") or days (\"d\") between two dates.",
                "DATEDIF(\"2024-01-31\", \"2024-03-01\", \"m\") = 1",
                new[]
                {
                    new ArgSpec("start", "The earlier date."),
                    new ArgSpec("end", "The later date."),
                    new ArgSpec("unit", "\"y\", \"m\" or \"d\".")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("DATEDIF", args, 3, 3, out var arity)) return arity;
                if (!TryDate("DATEDIF", args[0], ctx, out var start, out var e1)) return e1;
                if (!TryDate("DATEDIF", args[1], ctx, out var end, out var e2)) return e2;
                if (!FunctionArgs.TryText(args[2], ctx, out var unit, out var e3)) return e3;
                if (!SameOffsetBasis(start, end, out var mismatch)) return mismatch;

                var a = start.Value.Date;
                var b = end.Value.Date;
                if (b < a) return FormulaValue.Of(FormulaErrorCode.Value, "DATEDIF's start date must not be after its end date.");

                return unit.ToLowerInvariant() switch
                {
                    "d" => FormulaValue.Of((decimal)(b - a).Days),
                    "m" => FormulaValue.Of((decimal)CompleteMonths(a, b)),
                    "y" => FormulaValue.Of((decimal)(CompleteMonths(a, b) / 12)),
                    _ => FormulaValue.Of(FormulaErrorCode.Value, $"DATEDIF's unit must be \"y\", \"m\" or \"d\", got \"{unit}\".")
                };
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("DATEADD", FunctionCategory.Date, "Adds an amount of some unit to a date. Keeps the input's offset and precision; adding a time unit to a plain date turns it into a date-time.",
                "DATEADD(\"2024-03-15\", \"day\", 10) = \"2024-03-25\"",
                new[]
                {
                    new ArgSpec("date", "ISO date or date-time text."),
                    new ArgSpec("unit", "\"year\", \"month\", \"day\", \"hour\", \"minute\" or \"second\"."),
                    new ArgSpec("amount", "How many; negative goes back.")
                }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("DATEADD", args, 3, 3, out var arity)) return arity;
                if (!TryDate("DATEADD", args[0], ctx, out var date, out var e1)) return e1;
                if (!FunctionArgs.TryText(args[1], ctx, out var unit, out var e2)) return e2;
                if (!FunctionArgs.TryInt(args[2], ctx, out var amount, out var e3)) return e3;

                try
                {
                    // A time unit promotes a date-only value to a date-time — "the day after
                    // tomorrow at 3am" needs somewhere to put the 3am. A date unit leaves the
                    // shape alone.
                    return unit.ToLowerInvariant() switch
                    {
                        "year" => TryShiftMonths(date, checked(amount * 12), out var y)
                            ? FormulaValue.Of(y.Format()) : OutOfRange("DATEADD"),
                        "month" => TryShiftMonths(date, amount, out var m)
                            ? FormulaValue.Of(m.Format()) : OutOfRange("DATEADD"),
                        "day" => FormulaValue.Of(date.With(date.Value.AddDays(amount)).Format()),
                        "hour" => FormulaValue.Of(date.With(date.Value.AddHours(amount), alsoHasTime: true).Format()),
                        "minute" => FormulaValue.Of(date.With(date.Value.AddMinutes(amount), alsoHasTime: true).Format()),
                        "second" => FormulaValue.Of(date.With(date.Value.AddSeconds(amount), alsoHasTime: true).Format()),
                        _ => FormulaValue.Of(FormulaErrorCode.Value,
                            $"DATEADD's unit must be \"year\", \"month\", \"day\", \"hour\", \"minute\" or \"second\", got \"{unit}\".")
                    };
                }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
                {
                    return OutOfRange("DATEADD");
                }
            }));

        r.Add(new SimpleFunction(
            new FunctionMetadata("ISDATE", FunctionCategory.Date, "TRUE if the value is text this engine can read as an ISO 8601 date or date-time.",
                "ISDATE(\"2024-03-15\") = TRUE",
                new[] { new ArgSpec("value", "Any value.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity("ISDATE", args, 1, 1, out var arity)) return arity;
                var v = ctx.EvalArg(args[0]);
                if (v.IsError) return v;
                // Answers about a value's shape, so — like ISNUMBER/ISTEXT — a non-text value is
                // FALSE rather than an error. That's what makes it usable as a validation guard.
                return FormulaValue.Of(v.Kind == FormulaValueKind.Text && IsoDateTime.TryParse(v.AsText, out _));
            }));
    }

    // -----------------------------------------------------------------------

    private static SimpleFunction Part(string name, string description, string example, bool requiresTime,
        Func<IsoDateTime, int> select) =>
        new(new FunctionMetadata(name, FunctionCategory.Date, description, example,
                new[] { new ArgSpec("date", "ISO date or date-time text.") }),
            (args, ctx) =>
            {
                if (!FunctionArgs.TryArity(name, args, 1, 1, out var arity)) return arity;
                if (!TryDate(name, args[0], ctx, out var date, out var err)) return err;
                if (requiresTime && !date.HasTime)
                    return FormulaValue.Of(FormulaErrorCode.Value,
                        $"{name} needs a date-time; \"{date.Format()}\" has no time part.");
                return FormulaValue.Of((decimal)select(date));
            });

    /// <summary>Evaluates an argument as ISO date text. Anything that isn't text, or is text this
    /// engine doesn't recognize, is <c>#VALUE!</c> naming the offending string — the alternative
    /// (a locale-dependent best guess) is exactly what docs/TYPES.md rules out.</summary>
    private static bool TryDate(string functionName, FormulaAst arg, FunctionCallContext ctx,
        out IsoDateTime date, out FormulaValue error)
    {
        date = default;
        if (!FunctionArgs.TryText(arg, ctx, out var text, out error)) return false;
        if (IsoDateTime.TryParse(text, out date)) return true;

        error = FormulaValue.Of(FormulaErrorCode.Value,
            $"{functionName} expects an ISO 8601 date like \"2024-03-15\" or \"2024-03-15T14:30:00\", got \"{text}\".");
        return false;
    }

    /// <summary>Guards the two-date functions against comparing a value that records a UTC offset
    /// with one that doesn't: there is no instant to put the offset-less one at, so any answer
    /// would be a guess dressed up as arithmetic.</summary>
    private static bool SameOffsetBasis(IsoDateTime a, IsoDateTime b, out FormulaValue error)
    {
        if (a.HasOffset == b.HasOffset)
        {
            error = default;
            return true;
        }
        error = FormulaValue.Of(FormulaErrorCode.Value,
            $"Can't compare \"{a.Format()}\" with \"{b.Format()}\": one records a UTC offset and the other doesn't.");
        return false;
    }

    private static bool TryBuild(int year, int month, int day, int hour, int minute, int second, out DateTime result)
    {
        result = default;
        if (year is < 1 or > 9999 || month is < 1 or > 12) return false;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59) return false;
        result = new DateTime(year, month, day, hour, minute, second);
        return true;
    }

    /// <summary>Month arithmetic with end-of-month clamping (Jan 31 + 1 month = Feb 29, not Mar 2)
    /// — the one place where following Excel is right, because "a month later" genuinely has no
    /// better answer and everyone already expects this one.</summary>
    private static bool TryShiftMonths(IsoDateTime date, int months, out IsoDateTime result)
    {
        result = default;
        try
        {
            result = date.With(date.Value.AddMonths(months));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    private static int CompleteMonths(DateTime start, DateTime end)
    {
        var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        // Excel-compatible: a partial month at the end doesn't count. Compared on day-of-month
        // rather than by re-adding, so Jan 31 → Feb 29 counts as the 0 complete months it is.
        if (end.Day < start.Day) months--;
        return Math.Max(0, months);
    }

    private static FormulaValue OutOfRange(string name) =>
        FormulaValue.Of(FormulaErrorCode.Value, $"{name}'s result falls outside the range of representable dates.");
}
