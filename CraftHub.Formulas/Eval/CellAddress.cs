namespace CraftHub.Formulas.Eval;

/// <summary>Which cell a formula is being evaluated "as" — needed to resolve relative references
/// (<c>@[price]</c>, a bare <c>A1</c>, <c>$[r+1]</c>). Opaque to the evaluator itself: it only ever
/// passes this through to <see cref="IValueSource"/>, never inspects it — giving it meaning is
/// entirely CraftHub.Formulas.Addressing's job.</summary>
public readonly record struct CellAddress(int RowIndex, string ColumnKey);
