using System;
using System.Collections.Generic;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Eval;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Functions;

/// <summary>Wraps a plain delegate as an <see cref="IFormulaFunction"/> — every stage-1 function is
/// one of these, registered with its metadata, rather than a hand-rolled class per function.</summary>
public sealed class SimpleFunction : IFormulaFunction
{
    public string Name { get; }
    public FunctionMetadata Metadata { get; }
    private readonly Func<IReadOnlyList<FormulaAst>, FunctionCallContext, FormulaValue> _invoke;

    public SimpleFunction(FunctionMetadata metadata, Func<IReadOnlyList<FormulaAst>, FunctionCallContext, FormulaValue> invoke)
    {
        Name = metadata.Name;
        Metadata = metadata;
        _invoke = invoke;
    }

    public FormulaValue Invoke(IReadOnlyList<FormulaAst> arguments, FunctionCallContext context) =>
        _invoke(arguments, context);
}
