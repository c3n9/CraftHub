using System.Collections.Generic;

namespace CraftHub.Formulas.Functions;

public enum FunctionCategory
{
    Math,
    Statistics,
    Logic,
    Text,
    Json,
    Date,      // stage 2
    Lookup,    // stage 2
    RegularExpression, // stage 2
    Array      // stage 2
}

/// <summary>One named parameter, for autocomplete's per-argument hint — not validated by the
/// engine itself (each function validates its own arguments; this is documentation).</summary>
public sealed record ArgSpec(string Name, string Description, bool Optional = false, bool Repeating = false);

/// <summary>Everything the UI needs to show a function in autocomplete and hover help, without the
/// registry having to special-case any one function — new functions become available to
/// autocomplete purely by being registered with this metadata, no parser or UI changes required.</summary>
/// <param name="Volatile">True for a function whose result can change without any of its inputs
/// changing — <c>TODAY</c> and <c>NOW</c>, which read the clock. Incremental recalculation walks
/// the dependency graph, and a volatile formula has no dependencies to be reached through, so it is
/// recomputed only by a full recalculation. That's deliberate rather than a limitation: a sheet
/// where <c>NOW()</c> re-fired on every keystroke would never settle as "not dirty," and every save
/// would differ from the last. This flag is what lets the UI say so instead of leaving the user to
/// wonder why a timestamp didn't move.</param>
public sealed record FunctionMetadata(
    string Name,
    FunctionCategory Category,
    string Description,
    string Example,
    IReadOnlyList<ArgSpec> Arguments,
    bool Volatile = false);
