using System;
using System.Collections.Generic;
using System.Linq;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Values;

namespace CraftHub.Formulas.Eval;

/// <summary>
/// Walks a parsed formula and produces a value. Stateless and reusable across calls — all
/// per-evaluation state (recursion depth, deadline) is threaded through parameters, never stored
/// on the instance, so one <see cref="Evaluator"/> can safely serve concurrent recalculations.
///
/// Reference nodes (A1 forms, <c>[col]</c>, <c>$.path</c>, ...) are never interpreted here — they
/// go straight to <see cref="EvalContext.Values"/>. This class knows the grammar's operators and
/// how to call a function; it does not know what a JSON path or a table column is.
/// </summary>
public sealed class Evaluator
{
    public FormulaValue Evaluate(FormulaAst node, EvalContext context)
    {
        var deadline = DateTime.UtcNow + context.Limits.Timeout;
        return EvaluateInternal(node, context, depth: 0, deadline);
    }

    private FormulaValue EvaluateInternal(FormulaAst node, EvalContext context, int depth, DateTime deadline)
    {
        context.Cancellation.ThrowIfCancellationRequested();

        if (DateTime.UtcNow > deadline)
            return FormulaValue.Of(FormulaErrorCode.Value, "Formula evaluation timed out.");

        if (depth > context.Limits.MaxDepth)
            return FormulaValue.Of(FormulaErrorCode.Value, $"Formula nesting exceeds the limit of {context.Limits.MaxDepth}.");

        switch (node)
        {
            case NumberLiteral n:
                return FormulaValue.Of(n.Value);

            case TextLiteral t:
                return FormulaValue.Of(t.Value);

            case BoolLiteral b:
                return FormulaValue.Of(b.Value);

            case ErrorLiteral e:
                return FormulaValue.Of(e.Code, AstPrinter.Print(e));

            case UnaryExpr u:
            {
                var operand = EvaluateInternal(u.Operand, context, depth + 1, deadline);
                return u.Op == UnaryOp.Negate ? TypeRules.Negate(operand) : operand; // unary '+' just passes the value/error through
            }

            case PercentExpr p:
                return TypeRules.Percent(EvaluateInternal(p.Operand, context, depth + 1, deadline));

            case BinaryExpr bin:
            {
                var left = EvaluateInternal(bin.Left, context, depth + 1, deadline);
                var right = EvaluateInternal(bin.Right, context, depth + 1, deadline);
                return ApplyBinary(bin.Op, left, right);
            }

            case CallExpr call:
                return EvaluateCall(call, context, depth, deadline);

            case CellRefSyntax or RangeRefSyntax or ColumnBandSyntax or RowBandSyntax
                or ColumnRefSyntax or CurrentColumnRefSyntax or JsonPathSyntax:
                return context.Values.Resolve(node, context);

            default:
                return FormulaValue.Of(FormulaErrorCode.Value, $"Cannot evaluate node of type '{node.GetType().Name}'.");
        }
    }

    private static FormulaValue ApplyBinary(BinaryOp op, FormulaValue left, FormulaValue right) => op switch
    {
        BinaryOp.Add => TypeRules.Add(left, right),
        BinaryOp.Subtract => TypeRules.Subtract(left, right),
        BinaryOp.Multiply => TypeRules.Multiply(left, right),
        BinaryOp.Divide => TypeRules.Divide(left, right),
        BinaryOp.Power => TypeRules.Power(left, right),
        BinaryOp.Concat => TypeRules.Concat(left, right),
        BinaryOp.Eq => TypeRules.Equal(left, right),
        BinaryOp.Ne => TypeRules.NotEqual(left, right),
        BinaryOp.Lt => TypeRules.Less(left, right),
        BinaryOp.Gt => TypeRules.Greater(left, right),
        BinaryOp.Le => TypeRules.LessOrEqual(left, right),
        BinaryOp.Ge => TypeRules.GreaterOrEqual(left, right),
        _ => FormulaValue.Of(FormulaErrorCode.Value, $"Unknown operator '{op}'.")
    };

    private FormulaValue EvaluateCall(CallExpr call, EvalContext context, int depth, DateTime deadline)
    {
        if (!context.Functions.TryGet(call.FunctionName, out var fn))
            return FormulaValue.Of(FormulaErrorCode.Name, $"Unknown function '{call.FunctionName}'.");

        var callContext = new FunctionCallContext
        {
            Eval = context,
            EvalArg = arg => EvaluateInternal(arg, context, depth + 1, deadline),
            EvalArgMany = arg => EvaluateArgMany(arg, context, depth + 1, deadline)
        };

        try
        {
            return fn.Invoke(call.Arguments, callContext);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A bug in one function's implementation shouldn't take down the whole recalculation —
            // surface it as this cell's own error and let the rest of the sheet keep working.
            return FormulaValue.Of(FormulaErrorCode.Value, $"'{call.FunctionName}' failed: {ex.Message}");
        }
    }

    // A reference argument may be multi-cell (SUM(A1:A10)); anything else is just one value in a
    // sequence of one, so a function can always call EvalArgMany uniformly without special-casing
    // "was this a range or a scalar".
    private IEnumerable<FormulaValue> EvaluateArgMany(FormulaAst node, EvalContext context, int depth, DateTime deadline)
    {
        if (!IsReferenceNode(node))
            return new[] { EvaluateInternal(node, context, depth, deadline) };

        var limit = context.Limits.MaxRangeCells;
        var values = context.Values.ResolveMany(node, context).Take(limit + 1).ToList();

        return values.Count > limit
            ? new[] { FormulaValue.Of(FormulaErrorCode.Value, $"Range exceeds the {limit}-cell limit.") }
            : values;
    }

    private static bool IsReferenceNode(FormulaAst node) => node
        is CellRefSyntax or RangeRefSyntax or ColumnBandSyntax or RowBandSyntax
        or ColumnRefSyntax or CurrentColumnRefSyntax or JsonPathSyntax;
}
