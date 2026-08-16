using System.Collections.Generic;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Eval;

/// <summary>
/// Resolves a reference AST node into value(s). This is the only seam between the evaluator and
/// the actual document — the evaluator never interprets A1 syntax, JSON paths, or table layout;
/// all of that is CraftHub.Formulas.Addressing's job, wired in by whoever implements this interface
/// (the app's WorkspaceValueSource, in practice, using A1Translator + ITableShape internally).
/// </summary>
public interface IValueSource
{
    /// <summary>Single-value form. A reference that's inherently multi-cell (a range, a whole
    /// column, a wildcard path) resolves to a #VALUE! error here rather than picking an arbitrary
    /// cell — Excel's implicit intersection is exactly the kind of implicit behavior this engine
    /// avoids (see docs/TYPES.md).</summary>
    FormulaValue Resolve(FormulaAst reference, EvalContext context);

    /// <summary>Multi-value form, for references used where a range makes sense (function
    /// arguments like SUM/AVERAGE). Works for single-cell references too — they just yield one
    /// value — so functions can always call this and never need to special-case scalars.</summary>
    IEnumerable<FormulaValue> ResolveMany(FormulaAst reference, EvalContext context);
}
