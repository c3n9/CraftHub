using System;
using System.Globalization;

namespace CraftHub.Formulas.Values;

/// <summary>How an ISO 8601 string spelled its UTC offset — remembered so a date that goes through
/// a formula comes back out looking the way it went in.</summary>
public enum IsoOffsetForm
{
    /// <summary>No suffix at all: <c>2024-03-15T10:30:00</c>. A wall-clock time whose zone is
    /// simply not recorded in the document — see <see cref="IsoDateTime"/> for why that isn't
    /// quietly turned into UTC or local.</summary>
    None,

    /// <summary><c>Z</c>.</summary>
    Zulu,

    /// <summary><c>+03:00</c> / <c>-05:30</c>. Also how <c>+00:00</c> is kept: a document that
    /// spelled out a zero offset gets a zero offset back, not a <c>Z</c>.</summary>
    Numeric
}

/// <summary>
/// A date (or date-time) as this engine models one: JSON has no date type, so dates are ISO 8601
/// *strings*, not Excel's serial-day floats — see docs/TYPES.md.
///
/// The point of this type is shape preservation. Every field beyond the calendar value itself
/// (<see cref="HasTime"/>, <see cref="FractionalDigits"/>, <see cref="OffsetForm"/>) exists so that
/// <c>EDATE("2024-03-15", 1)</c> gives back <c>"2024-04-15"</c> and
/// <c>EDATE("2024-03-15T10:30:00+03:00", 1)</c> gives back <c>"2024-04-15T10:30:00+03:00"</c> —
/// the same spelling, one month later. A document's own date format is data, and a formula that
/// silently rewrote every timestamp into UTC (or into local time, or into a different precision)
/// would be corrupting that data while appearing to do arithmetic.
///
/// <see cref="Value"/> is always the wall-clock reading, with <see cref="Offset"/> kept beside it
/// rather than applied to it. Date-shifting operations (EDATE, DATEADD, ...) move the wall clock
/// and carry the offset along unchanged, which is what a person means by "the same time next
/// month" even across a DST boundary.
/// </summary>
public readonly record struct IsoDateTime(
    DateTime Value,
    bool HasTime,
    int FractionalDigits,
    IsoOffsetForm OffsetForm,
    TimeSpan Offset)
{
    /// <summary>A date with no time part, no offset — what <c>DATE()</c> and <c>TODAY()</c> build.</summary>
    public static IsoDateTime FromDate(DateTime date) =>
        new(date.Date, HasTime: false, FractionalDigits: 0, IsoOffsetForm.None, TimeSpan.Zero);

    /// <summary>A wall-clock date-time with no recorded offset — what <c>NOW()</c> and
    /// <c>DATETIME()</c> build.</summary>
    public static IsoDateTime FromDateTime(DateTime value, int fractionalDigits = 0) =>
        new(value, HasTime: true, fractionalDigits, IsoOffsetForm.None, TimeSpan.Zero);

    /// <summary>True when this value records a UTC offset (<c>Z</c> or numeric). Comparing an
    /// offset-bearing value against an offset-less one is meaningless — there's no instant to
    /// compare against — which is why the difference functions check this rather than assuming.</summary>
    public bool HasOffset => OffsetForm != IsoOffsetForm.None;

    /// <summary>Same wall clock, new calendar value — the shape-preserving way for date arithmetic
    /// to produce its result. Promotes to a date-time if <paramref name="alsoHasTime"/> says so.</summary>
    public IsoDateTime With(DateTime value, bool? alsoHasTime = null) =>
        this with { Value = value, HasTime = alsoHasTime ?? HasTime };

    /// <summary>Drops the time and offset, keeping the calendar date — for functions like
    /// <c>EOMONTH</c> whose answer is a date, whatever the input carried.</summary>
    public IsoDateTime ToDateOnly() => FromDate(Value.Date);

    // The exact set of accepted spellings, per docs/TYPES.md. Deliberately an explicit whitelist
    // parsed with TryParseExact rather than DateTime.Parse: the latter accepts a sprawl of locale
    // and legacy formats ("3/15/2024", "March 15") whose meaning depends on the host machine, and
    // silently accepting those here would make a formula's behavior differ per user.
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly string[] LocalFormats =
    {
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.f",
        "yyyy-MM-ddTHH:mm:ss.ff",
        "yyyy-MM-ddTHH:mm:ss.fff"
    };

    private static readonly string[] OffsetFormats =
    {
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ss.fzzz",
        "yyyy-MM-ddTHH:mm:ss.ffzzz",
        "yyyy-MM-ddTHH:mm:ss.fffzzz"
    };

    /// <summary>Parses one of the accepted ISO 8601 spellings. Returns false — rather than
    /// throwing or guessing — for anything else, so callers can turn it into a
    /// <c>#VALUE!</c> with their own message.</summary>
    public static bool TryParse(string text, out IsoDateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(text)) return false;

        const DateTimeStyles styles = DateTimeStyles.None;

        if (DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, styles, out var dateOnly))
        {
            result = FromDate(dateOnly);
            return true;
        }

        for (var i = 0; i < LocalFormats.Length; i++)
        {
            if (!DateTime.TryParseExact(text, LocalFormats[i], CultureInfo.InvariantCulture, styles, out var local))
                continue;
            result = new IsoDateTime(local, HasTime: true, FractionalDigitsOf(i), IsoOffsetForm.None, TimeSpan.Zero);
            return true;
        }

        // "Z" isn't a zzz-parseable offset, so the Zulu spellings are handled by rewriting the
        // suffix to +00:00 and remembering that it was a Z — keeping one parse path for both.
        var isZulu = text.EndsWith('Z');
        var candidate = isZulu ? string.Concat(text.AsSpan(0, text.Length - 1), "+00:00") : text;

        for (var i = 0; i < OffsetFormats.Length; i++)
        {
            if (!DateTimeOffset.TryParseExact(candidate, OffsetFormats[i], CultureInfo.InvariantCulture, styles, out var withOffset))
                continue;
            result = new IsoDateTime(withOffset.DateTime, HasTime: true, FractionalDigitsOf(i),
                isZulu ? IsoOffsetForm.Zulu : IsoOffsetForm.Numeric, withOffset.Offset);
            return true;
        }

        return false;
    }

    private static int FractionalDigitsOf(int formatIndex) => formatIndex; // formats are ordered 0,1,2,3 digits

    /// <summary>Renders back to ISO 8601 in exactly the shape this value was parsed (or built) in.</summary>
    public string Format()
    {
        if (!HasTime) return Value.ToString(DateFormat, CultureInfo.InvariantCulture);

        var pattern = "yyyy-MM-ddTHH:mm:ss" + FractionalDigits switch
        {
            1 => ".f",
            2 => ".ff",
            3 => ".fff",
            _ => ""
        };

        var text = Value.ToString(pattern, CultureInfo.InvariantCulture);
        return OffsetForm switch
        {
            IsoOffsetForm.Zulu => text + "Z",
            IsoOffsetForm.Numeric => text + FormatOffset(Offset),
            _ => text
        };
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var abs = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Hours:D2}:{abs.Minutes:D2}");
    }

    public override string ToString() => Format();
}
