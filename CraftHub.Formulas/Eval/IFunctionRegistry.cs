using System.Diagnostics.CodeAnalysis;

namespace CraftHub.Formulas.Eval;

/// <summary>Looks up a function by name (already upper-cased by the parser). An unknown name is
/// how <c>#NAME?</c> happens — see <c>FormulaParser</c>'s doc comment: the parser never validates
/// function names, so this is the actual point of truth for "does this function exist".</summary>
public interface IFunctionRegistry
{
    bool TryGet(string name, [NotNullWhen(true)] out IFormulaFunction? function);
}
