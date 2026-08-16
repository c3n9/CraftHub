using System;
using System.Collections.Generic;

namespace CraftHub.Formulas.Values;

/// <summary>
/// A formula value, JSON-first rather than Excel-first: see docs/TYPES.md for the full rationale.
/// The short version — <see cref="Missing"/> and <see cref="Null"/> are different things (both
/// distinct from an empty <see cref="Text"/>), arithmetic never silently coerces text or booleans,
/// and numbers are <c>decimal</c> so money doesn't drift.
/// </summary>
public readonly struct FormulaValue
{
    public FormulaValueKind Kind { get; }

    private readonly decimal _number;
    private readonly bool _boolean;
    private readonly string? _text;
    private readonly IReadOnlyList<FormulaValue>? _array;
    private readonly IReadOnlyDictionary<string, FormulaValue>? _object;
    private readonly FormulaError _error;

    private FormulaValue(FormulaValueKind kind, decimal number = default, bool boolean = default,
        string? text = null, IReadOnlyList<FormulaValue>? array = null,
        IReadOnlyDictionary<string, FormulaValue>? obj = null, FormulaError error = default)
    {
        Kind = kind;
        _number = number;
        _boolean = boolean;
        _text = text;
        _array = array;
        _object = obj;
        _error = error;
    }

    public static readonly FormulaValue Missing = new(FormulaValueKind.Missing);
    public static readonly FormulaValue Null = new(FormulaValueKind.Null);
    public static readonly FormulaValue True = new(FormulaValueKind.Boolean, boolean: true);
    public static readonly FormulaValue False = new(FormulaValueKind.Boolean, boolean: false);

    public static FormulaValue Of(decimal value) => new(FormulaValueKind.Number, number: value);
    public static FormulaValue Of(bool value) => value ? True : False;
    public static FormulaValue Of(string value) => new(FormulaValueKind.Text, text: value);
    public static FormulaValue Of(IReadOnlyList<FormulaValue> items) => new(FormulaValueKind.Array, array: items);
    public static FormulaValue Of(IReadOnlyDictionary<string, FormulaValue> members) => new(FormulaValueKind.Object, obj: members);
    public static FormulaValue Of(FormulaError error) => new(FormulaValueKind.Error, error: error);
    public static FormulaValue Of(FormulaErrorCode code, string message) => Of(new FormulaError(code, message));

    public bool IsError => Kind == FormulaValueKind.Error;
    public bool IsMissing => Kind == FormulaValueKind.Missing;
    public bool IsNull => Kind == FormulaValueKind.Null;

    /// <summary>True for Missing or Null — the two kinds SUM/AVERAGE/etc. skip rather than treat
    /// as zero. Not the same as ISBLANK, which means Missing specifically.</summary>
    public bool IsMissingOrNull => Kind is FormulaValueKind.Missing or FormulaValueKind.Null;

    public decimal AsNumber => Kind == FormulaValueKind.Number
        ? _number
        : throw new InvalidOperationException($"Value is {Kind}, not Number.");

    public bool AsBoolean => Kind == FormulaValueKind.Boolean
        ? _boolean
        : throw new InvalidOperationException($"Value is {Kind}, not Boolean.");

    public string AsText => Kind == FormulaValueKind.Text
        ? _text!
        : throw new InvalidOperationException($"Value is {Kind}, not Text.");

    public IReadOnlyList<FormulaValue> AsArray => Kind == FormulaValueKind.Array
        ? _array!
        : throw new InvalidOperationException($"Value is {Kind}, not Array.");

    public IReadOnlyDictionary<string, FormulaValue> AsObject => Kind == FormulaValueKind.Object
        ? _object!
        : throw new InvalidOperationException($"Value is {Kind}, not Object.");

    public FormulaError AsError => Kind == FormulaValueKind.Error
        ? _error
        : throw new InvalidOperationException($"Value is {Kind}, not Error.");

    /// <summary>Lowercase kind name for TYPEOF: "number"/"string"/"bool"/"null"/"array"/"object".
    /// TYPEOF has no separate spelling for Missing — see TYPEOF's own doc comment for why.</summary>
    public string TypeName => Kind switch
    {
        FormulaValueKind.Missing => "null",
        FormulaValueKind.Null => "null",
        FormulaValueKind.Number => "number",
        FormulaValueKind.Boolean => "bool",
        FormulaValueKind.Text => "string",
        FormulaValueKind.Array => "array",
        FormulaValueKind.Object => "object",
        FormulaValueKind.Error => "error",
        _ => "unknown"
    };

    public override string ToString() => Kind switch
    {
        FormulaValueKind.Missing => "",
        FormulaValueKind.Null => "null",
        FormulaValueKind.Number => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FormulaValueKind.Boolean => _boolean ? "TRUE" : "FALSE",
        FormulaValueKind.Text => _text!,
        FormulaValueKind.Array => "[array]",
        FormulaValueKind.Object => "[object]",
        FormulaValueKind.Error => _error.Symbol,
        _ => ""
    };
}
