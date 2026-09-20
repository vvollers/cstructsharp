namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Which public failure an expression evaluated in that context maps to.</summary>
internal enum ExpressionFailureDomain
{
    /// <summary>The expression is evaluated while compiling the layout; a failure means the layout is invalid.</summary>
    Layout,

    /// <summary>The expression is evaluated against decoded data during a read; a failure means the data cannot be read.</summary>
    Read,

    /// <summary>The expression is evaluated against supplied values during a write; a failure means the values cannot be written.</summary>
    Write,
}

/// <summary>
///     Evaluates one core layout expression and normalizes deterministic failures to the public exception family.
///     The same expression can fail at construction (a <c>#define</c> that overflows) or during an operation (a
///     decoded count that does not fit); the <see cref="ExpressionFailureDomain"/> keeps the exception type honest
///     about which of the two happened.
/// </summary>
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

    /// <summary>The <c>Cannot evaluate {context}: {reason}</c> text every expression failure carries.</summary>
    public static string DescribeFailure(string context, Exception exception)
        => $"Cannot evaluate {context}: {Describe(exception)}";

    /// <summary>
    ///     Evaluates <paramref name="expression"/> and reports a failure as the exception of
    ///     <paramref name="domain"/> with the message <c>Cannot evaluate {context}: {reason}</c>.
    /// </summary>
    public int Evaluate(
        Expr expression,
        IReadOnlyDictionary<string, Expr> variables,
        string context,
        ExpressionFailureDomain domain = ExpressionFailureDomain.Layout)
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
            throw CreateFailure(domain, DescribeFailure(context, exception), exception);
        }
    }

    /// <summary>Replaces framework wording with the layout expression's own terms.</summary>
    private static string Describe(Exception exception)
    {
        return exception is OverflowException
                   ? "the result is outside the 32-bit range that layout expressions support."
                   : exception.Message;
    }

    private static CStructException CreateFailure(ExpressionFailureDomain domain, string message, Exception inner)
    {
        return domain switch
        {
            ExpressionFailureDomain.Read => new CStructReadException(message, inner),
            ExpressionFailureDomain.Write => new CStructWriteException(message, inner),
            _ => new CStructLayoutException(message, inner),
        };
    }
}
