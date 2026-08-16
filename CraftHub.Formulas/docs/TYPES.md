# Type rules — JSON-first, not Excel-first

This engine copies Excel's formula *syntax* and *ergonomics*. It deliberately does not copy
Excel's type coercion, because that coercion was designed around a grid of untyped cells, not a
document that already has a real type system (JSON's). Where the two disagree, JSON wins. This
document is the single source of truth for that behavior; `TypeRules` implements exactly what's
written here, and every rule below is covered by a test in `CraftHub.Formulas.Tests`.

## The value kinds

A `FormulaValue` is one of: `Missing`, `Null`, `Number` (`decimal`), `Boolean`, `Text`, `Array`,
`Object`, `Error`. Six of these map onto JSON directly; `Missing` does not exist in JSON — it's
this engine's name for "the key isn't in the object at all."

## Missing vs. Null vs. empty string

Three different things, never conflated:

| | Meaning | `ISBLANK` | `ISNULL` | In `SUM`/`AVERAGE`/`COUNT` |
|---|---|---|---|---|
| Missing | the key doesn't exist on this object | `TRUE` | `FALSE` | skipped |
| Null | the key exists, value is JSON `null` | `FALSE` | `TRUE` | skipped |
| `""` | the key exists, value is an empty string | `FALSE` | `FALSE` | skipped (not text) |

Aggregates skip Missing and Null rather than treating them as zero. Excel treats a blank cell as
0 in arithmetic and as "skip" in aggregates simultaneously — inconsistent, and wrong for JSON where
`null` is meaningful data, not "nothing here yet."

## Arithmetic (`+ - * / ^ %` and unary `-`)

Only `Number` participates. Everything else is an error, not a silent conversion:

- `Text` (even `"123"`) → `#VALUE!`. Use `VALUE("123")` to convert explicitly.
- `Boolean` → `#TYPE!`. Use `IF(x, 1, 0)` to convert explicitly.
- `Null` / `Missing` → `#VALUE!`. (Aggregates are the exception — see above — because skipping
  them there is itself the meaningful behavior, not a coercion.)
- `Array` / `Object` → `#VALUE!`.

An error operand always propagates unchanged, ahead of any type check.

Division by zero → `#DIV/0!`. A `POWER`/`^` result that isn't a real number (e.g. a negative base
with a fractional exponent) → `#VALUE!`, not `NaN` leaking into the sheet.

## Concatenation (`&`)

More permissive than arithmetic, because building display text is a different operation from
computing a value: `Number`, `Boolean`, and `Text` all convert to text and concatenate (numbers via
invariant decimal formatting, booleans as `TRUE`/`FALSE`). `Null`, `Missing`, `Array`, and `Object`
are still `#VALUE!` here — an empty string is a real, different value from "nothing," and silently
turning a missing field into `""` mid-formula would hide exactly the distinction this whole type
system exists to preserve.

## Comparison (`= <>` and `< > <= >=`)

`=`/`<>` never error: values of different kinds (or `Boolean`/`Missing`/`Null` compared to
anything of a different kind) are simply unequal, matching how Excel itself treats mismatched
comparisons. `Array`/`Object` are the one exception — comparing them isn't meaningful either way,
so it's `#VALUE!`.

`< > <= >=` require both sides to be the *same* comparable kind — `Number`-`Number`,
`Text`-`Text` (ordinal), or `Boolean`-`Boolean` (`FALSE < TRUE`). Comparing across kinds is
`#VALUE!` rather than falling back to Excel's implicit type-ordering (numbers < text < booleans),
which is exactly the kind of magic this engine avoids.

## Booleans are not 1/0

`TRUE`/`FALSE` never equal `1`/`0`, in arithmetic or in `=`. There is one explicit door between the
two: `IF(x, 1, 0)` (or the reverse, `x <> 0` for a number-to-bool test the user writes themselves).
No implicit conversion either direction.

## Number parsing and formatting

Literals and `VALUE()` parse with `.` as the decimal point and `,` as the argument separator,
always — `CultureInfo.InvariantCulture`, never the host machine's locale. All arithmetic runs on
`decimal` (28–29 significant digits), so money doesn't drift the way `double` would; the few
functions that are inherently irrational (`SQRT`, `POWER` with a fractional exponent, `STDEV`)
compute in `double` internally and convert back, producing `#VALUE!` instead of `NaN`/`±∞` if the
result isn't representable.

## Writing results back to JSON

A formula's result is written using `System.Text.Json`'s native number handling
(`JsonValue.Create(decimal)`), which preserves the value exactly as typed — an integer result
serializes as `5`, never `5.0`, and `19.90` keeps its trailing zero rather than being normalized to
`19.9`. The result's `FormulaValueKind` maps onto a JSON node directly: `Number` → JSON number,
`Boolean` → JSON bool, `Text` → JSON string, `Array`/`Object` → the corresponding JSON node
(from `TOJSON`/`PARSEJSON`-family functions), `Null` → JSON `null`. `Missing` cannot be a formula
*result* (there's no operation that produces "no key"); it only ever describes what a *reference*
found. A formula whose result doesn't losslessly fit the column's declared type (e.g. a decimal
result on an `Int` column) is `#TYPE!` — a formula never silently changes a column's declared
type.

Errors are written as JSON `null` in the main document (never a string like `"#DIV/0!"`, which
would corrupt whatever type the column actually is); the error code and message are kept in the
sidecar's `state` section instead, so re-opening the file shows the error immediately without a
recalculation pass. See `FormulaSidecar`'s own doc comments for the exact shape.

## Dates

JSON has no date type — dates are ISO 8601 strings, not Excel's serial-day-number floats. Stage-2
date functions (`NOW`, `TODAY`, `DATE`, `YEAR`, ...) both accept and return ISO 8601 text.
Parsing accepts `yyyy-MM-dd` and `yyyy-MM-ddTHH:mm:ss[.fff]`, with or without a UTC/offset suffix,
via `DateTime.TryParseExact`/`DateTimeOffset` with `DateTimeStyles.RoundtripKind` — a string with no
offset stays offset-less through every operation (`Unspecified` kind), and a string with an offset
keeps it; nothing is silently normalized to UTC or to local time. `TODAY()`/`NOW()` read the host
machine's local clock and are marked `volatile` in the function registry (recomputed only on a full
recalculation, not on every incremental one — otherwise the sheet would never settle as "not
dirty").
