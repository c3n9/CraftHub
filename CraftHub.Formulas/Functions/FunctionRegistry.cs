using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CraftHub.Formulas.Eval;

namespace CraftHub.Formulas.Functions;

/// <summary>
/// Extensible function lookup: <see cref="Add"/> a <see cref="SimpleFunction"/> and it's callable
/// and shows up in <see cref="All"/> (what autocomplete enumerates) — no parser or evaluator change
/// needed. <see cref="CreateStandard"/> builds the registry with every stage-1 function; the
/// per-category <c>Register*</c> methods (in <c>MathFunctions</c>, <c>StatisticsFunctions</c>, ...)
/// are also usable standalone, which is how the tests exercise one category at a time.
/// </summary>
public sealed class FunctionRegistry : IFunctionRegistry
{
    private readonly Dictionary<string, SimpleFunction> _functions = new();

    public void Add(SimpleFunction function) => _functions[function.Name] = function;

    public bool TryGet(string name, [NotNullWhen(true)] out IFormulaFunction? function)
    {
        if (_functions.TryGetValue(name, out var found))
        {
            function = found;
            return true;
        }
        function = null;
        return false;
    }

    public bool TryGetMetadata(string name, [NotNullWhen(true)] out FunctionMetadata? metadata)
    {
        if (_functions.TryGetValue(name, out var found))
        {
            metadata = found.Metadata;
            return true;
        }
        metadata = null;
        return false;
    }

    public IEnumerable<FunctionMetadata> All()
    {
        foreach (var fn in _functions.Values)
            yield return fn.Metadata;
    }

    /// <summary>Every stage-1 function (math, statistics, logic, text, JSON), ready to use.</summary>
    public static FunctionRegistry CreateStandard()
    {
        var registry = new FunctionRegistry();
        MathFunctions.Register(registry);
        StatisticsFunctions.Register(registry);
        LogicFunctions.Register(registry);
        TextFunctions.Register(registry);
        JsonFunctions.Register(registry);
        return registry;
    }
}
