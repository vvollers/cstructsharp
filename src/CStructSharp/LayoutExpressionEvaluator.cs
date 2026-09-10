namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>Evaluates one core layout expression and normalizes deterministic failures to the layout domain.</summary>
internal sealed class LayoutExpressionEvaluator
{
    private readonly ExpressionEvaluator expressionEvaluator;

    public LayoutExpressionEvaluator(ExpressionEvaluator expressionEvaluator)
    {
        this.expressionEvaluator = expressionEvaluator;
    }

    /// <summary>Recognizes supported expression-domain failures without hiding unrelated programming defects.</summary>
    public static bool IsExpressionFailure(Exception exception)
    {
        return exception is InvalidOperationException or ArithmeticException or
               KeyNotFoundException or NotSupportedException;
    }

    public int Evaluate(
        Expr expression,
        IReadOnlyDictionary<string, Expr> variables,
        string context)
    {
        try
        {
            return this.expressionEvaluator.Evaluate(expression, variables);
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpressionFailure(exception))
        {
            throw new CStructLayoutException(
                $"Cannot evaluate {context}: {exception.Message}",
                exception);
        }
    }
}
