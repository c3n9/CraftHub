using System;
using System.Globalization;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Eval;

/// <summary>
/// JSON-first arithmetic, comparison, and concatenation — see docs/TYPES.md for the full
/// rationale and worked examples. In one line: Excel's syntax, not its type coercion. Text never
/// silently becomes a number, booleans never silently become 0/1; the function registry is where
/// SUM/AVERAGE/etc. skip Missing/Null instead of erroring — that's aggregate behavior, distinct
/// from what a bare <c>+</c> does with them here.
/// </summary>
public static class TypeRules
{
    public static FormulaValue Add(FormulaValue a, FormulaValue b) => Arithmetic(a, b, static (x, y) => x + y);
    public static FormulaValue Subtract(FormulaValue a, FormulaValue b) => Arithmetic(a, b, static (x, y) => x - y);
    public static FormulaValue Multiply(FormulaValue a, FormulaValue b) => Arithmetic(a, b, static (x, y) => x * y);

    public static FormulaValue Divide(FormulaValue a, FormulaValue b)
    {
        if (TryPropagateError(a, b, out var err)) return err;
        if (!TryAsNumber(a, out var x)) return TypeError(a);
        if (!TryAsNumber(b, out var y)) return TypeError(b);
        if (y == 0m) return FormulaValue.Of(FormulaErrorCode.DivZero, "Division by zero.");
        return FormulaValue.Of(x / y);
    }

    public static FormulaValue Power(FormulaValue a, FormulaValue b)
    {
        if (TryPropagateError(a, b, out var err)) return err;
        if (!TryAsNumber(a, out var x)) return TypeError(a);
        if (!TryAsNumber(b, out var y)) return TypeError(b);

        var result = Math.Pow((double)x, (double)y);
        if (double.IsNaN(result) || double.IsInfinity(result))
            return FormulaValue.Of(FormulaErrorCode.Value, "Result is not a real number.");

        try
        {
            return FormulaValue.Of((decimal)result);
        }
        catch (OverflowException)
        {
            return FormulaValue.Of(FormulaErrorCode.Value, "Result is too large to represent.");
        }
    }

    public static FormulaValue Percent(FormulaValue a)
    {
        if (a.IsError) return a;
        return TryAsNumber(a, out var x) ? FormulaValue.Of(x / 100m) : TypeError(a);
    }

    public static FormulaValue Negate(FormulaValue a)
    {
        if (a.IsError) return a;
        return TryAsNumber(a, out var x) ? FormulaValue.Of(-x) : TypeError(a);
    }

    public static FormulaValue Concat(FormulaValue a, FormulaValue b)
    {
        if (TryPropagateError(a, b, out var err)) return err;
        if (!TryAsConcatText(a, out var x)) return TypeError(a);
        if (!TryAsConcatText(b, out var y)) return TypeError(b);
        return FormulaValue.Of(x + y);
    }

    public static FormulaValue Equal(FormulaValue a, FormulaValue b) => CompareEquality(a, b, negate: false);
    public static FormulaValue NotEqual(FormulaValue a, FormulaValue b) => CompareEquality(a, b, negate: true);

    public static FormulaValue Less(FormulaValue a, FormulaValue b) => Order(a, b, static c => c < 0);
    public static FormulaValue Greater(FormulaValue a, FormulaValue b) => Order(a, b, static c => c > 0);
    public static FormulaValue LessOrEqual(FormulaValue a, FormulaValue b) => Order(a, b, static c => c <= 0);
    public static FormulaValue GreaterOrEqual(FormulaValue a, FormulaValue b) => Order(a, b, static c => c >= 0);

    // ---- helpers ----

    private static FormulaValue Arithmetic(FormulaValue a, FormulaValue b, Func<decimal, decimal, decimal> op)
    {
        if (TryPropagateError(a, b, out var err)) return err;
        if (!TryAsNumber(a, out var x)) return TypeError(a);
        if (!TryAsNumber(b, out var y)) return TypeError(b);

        try
        {
            return FormulaValue.Of(op(x, y));
        }
        catch (OverflowException)
        {
            return FormulaValue.Of(FormulaErrorCode.Value, "Result overflows the numeric range.");
        }
    }

    private static bool TryAsNumber(FormulaValue v, out decimal number)
    {
        if (v.Kind == FormulaValueKind.Number)
        {
            number = v.AsNumber;
            return true;
        }
        number = 0m;
        return false;
    }

    private static bool TryAsConcatText(FormulaValue v, out string text)
    {
        switch (v.Kind)
        {
            case FormulaValueKind.Text:
                text = v.AsText;
                return true;
            case FormulaValueKind.Number:
                text = v.AsNumber.ToString(CultureInfo.InvariantCulture);
                return true;
            case FormulaValueKind.Boolean:
                text = v.AsBoolean ? "TRUE" : "FALSE";
                return true;
            default:
                text = "";
                return false;
        }
    }

    private static FormulaValue CompareEquality(FormulaValue a, FormulaValue b, bool negate)
    {
        if (TryPropagateError(a, b, out var err)) return err;

        if (a.Kind is FormulaValueKind.Array or FormulaValueKind.Object
            || b.Kind is FormulaValueKind.Array or FormulaValueKind.Object)
            return FormulaValue.Of(FormulaErrorCode.Value, "Arrays and objects cannot be compared.");

        var equal = (a.Kind, b.Kind) switch
        {
            (FormulaValueKind.Number, FormulaValueKind.Number) => a.AsNumber == b.AsNumber,
            (FormulaValueKind.Text, FormulaValueKind.Text) => a.AsText == b.AsText,
            (FormulaValueKind.Boolean, FormulaValueKind.Boolean) => a.AsBoolean == b.AsBoolean,
            (FormulaValueKind.Null, FormulaValueKind.Null) => true,
            (FormulaValueKind.Missing, FormulaValueKind.Missing) => true,
            _ => false // different kinds (including Missing vs Null) are simply unequal, not an error
        };

        return FormulaValue.Of(negate ? !equal : equal);
    }

    private static FormulaValue Order(FormulaValue a, FormulaValue b, Func<int, bool> test)
    {
        if (TryPropagateError(a, b, out var err)) return err;

        int cmp;
        if (a.Kind == FormulaValueKind.Number && b.Kind == FormulaValueKind.Number)
            cmp = a.AsNumber.CompareTo(b.AsNumber);
        else if (a.Kind == FormulaValueKind.Text && b.Kind == FormulaValueKind.Text)
            cmp = string.CompareOrdinal(a.AsText, b.AsText);
        else if (a.Kind == FormulaValueKind.Boolean && b.Kind == FormulaValueKind.Boolean)
            cmp = a.AsBoolean.CompareTo(b.AsBoolean);
        else
            return FormulaValue.Of(FormulaErrorCode.Value, "Cannot order values of different or unsupported types.");

        return FormulaValue.Of(test(cmp));
    }

    private static bool TryPropagateError(FormulaValue a, FormulaValue b, out FormulaValue error)
    {
        if (a.IsError) { error = a; return true; }
        if (b.IsError) { error = b; return true; }
        error = default;
        return false;
    }

    private static FormulaValue TypeError(FormulaValue v) => v.Kind switch
    {
        FormulaValueKind.Text => FormulaValue.Of(FormulaErrorCode.Value, "Text cannot be used in arithmetic; use VALUE() to convert explicitly."),
        FormulaValueKind.Boolean => FormulaValue.Of(FormulaErrorCode.Type, "A boolean cannot be used in arithmetic; use IF(x,1,0) to convert explicitly."),
        FormulaValueKind.Null => FormulaValue.Of(FormulaErrorCode.Value, "null cannot be used in arithmetic."),
        FormulaValueKind.Missing => FormulaValue.Of(FormulaErrorCode.Value, "A missing value cannot be used in arithmetic."),
        FormulaValueKind.Array => FormulaValue.Of(FormulaErrorCode.Value, "An array cannot be used in arithmetic."),
        FormulaValueKind.Object => FormulaValue.Of(FormulaErrorCode.Value, "An object cannot be used in arithmetic."),
        _ => FormulaValue.Of(FormulaErrorCode.Value, "Unsupported value in arithmetic.")
    };
}
