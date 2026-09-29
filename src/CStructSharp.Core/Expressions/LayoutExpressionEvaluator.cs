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

    /// <summary>Creates a wrapper that evaluates through the layout's bounded evaluator.</summary>
    /// <param name="expressionEvaluator">
    ///     The evaluator that compiles expressions and applies the layout's limits.
    /// </param>
    public LayoutExpressionEvaluator(ExpressionEvaluator expressionEvaluator)
    {
        this.expressionEvaluator = expressionEvaluator;
    }

    /// <summary>Recognizes supported expression-domain failures without hiding unrelated programming defects.</summary>
    /// <param name="exception">The exception thrown while evaluating an expression.</param>
    /// <returns>
    ///     <see langword="true"/> for the invalid-operation, arithmetic, unknown-name, and unsupported-node failures
    ///     an expression can cause; <see langword="false"/> for anything else.
    /// </returns>
    public static bool IsExpressionFailure(Exception exception)
    {
        return exception is InvalidOperationException or ArithmeticException or
               KeyNotFoundException or NotSupportedException;
    }

    /// <summary>The <c>Cannot evaluate {context}: {reason}</c> text every expression failure carries.</summary>
    /// <param name="context">What was being evaluated, such as a field's array length, in the caller's words.</param>
    /// <param name="exception">The failure; an overflow is described as leaving the 128-bit range.</param>
    /// <returns>The complete failure message.</returns>
    public static string DescribeFailure(string context, Exception exception)
        => $"Cannot evaluate {context}: {Describe(exception)}";

    /// <summary>Narrows an evaluated result to the 32-bit integer its consumer stores.</summary>
    /// <param name="value">The evaluated result.</param>
    /// <param name="context">What was evaluated, used in the failure message.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The value as an <see cref="int"/>.</returns>
    /// <exception cref="CStructException">The value is outside the signed 32-bit range; the message names it.</exception>
    public static int RequireInt32(Int128 value, string context, ExpressionFailureDomain domain)
    {
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw CreateFailure(domain, LayoutFailures.OutsideInt32(context, value), null);
        }

        return (int)value;
    }

    /// <summary>
    ///     Evaluates <paramref name="expression"/> and reports a failure as the exception of
    ///     <paramref name="domain"/> with the message <c>Cannot evaluate {context}: {reason}</c>.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="variables">The names identifiers resolve to.</param>
    /// <param name="context">What is being evaluated, used in the failure message.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The signed 128-bit value of the expression.</returns>
    /// <exception cref="CStructLayoutException">
    ///     The expression exceeds the evaluator's limits (in any domain), or fails in the layout domain.
    /// </exception>
    /// <exception cref="CStructReadException">The expression fails in the read domain.</exception>
    /// <exception cref="CStructWriteException">The expression fails in the write domain.</exception>
    public Int128 Evaluate(
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

    /// <summary>
    ///     Evaluates an expression whose consumer stores the result as a 32-bit integer (an alignment, an offset
    ///     assertion, a bitfield width, a compile-time array length): the expression itself uses the full 128-bit
    ///     domain, and only its final value must fit.
    /// </summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <param name="variables">The names identifiers resolve to.</param>
    /// <param name="context">What is being evaluated, used in the failure message.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The value as an <see cref="int"/>.</returns>
    /// <exception cref="CStructException">
    ///     The expression fails as <see cref="Evaluate"/> describes, or its value is outside the signed 32-bit range;
    ///     that message names the value.
    /// </exception>
    public int EvaluateInt32(
        Expr expression,
        IReadOnlyDictionary<string, Expr> variables,
        string context,
        ExpressionFailureDomain domain = ExpressionFailureDomain.Layout)
        => RequireInt32(this.Evaluate(expression, variables, context, domain), context, domain);

    /// <summary>Creates the exception of <paramref name="domain"/> with the given message and optional cause.</summary>
    /// <param name="domain">Whether the failure is a layout, read, or write exception.</param>
    /// <param name="message">The complete message.</param>
    /// <param name="inner">The expression failure that caused it, or <see langword="null"/>.</param>
    /// <returns>The exception to throw.</returns>
    internal static CStructException CreateFailure(ExpressionFailureDomain domain, string message, Exception? inner)
    {
        if (inner is null)
        {
            return domain switch
            {
                ExpressionFailureDomain.Read => new CStructReadException(message),
                ExpressionFailureDomain.Write => new CStructWriteException(message),
                _ => new CStructLayoutException(message),
            };
        }

        return domain switch
        {
            ExpressionFailureDomain.Read => new CStructReadException(message, inner),
            ExpressionFailureDomain.Write => new CStructWriteException(message, inner),
            _ => new CStructLayoutException(message, inner),
        };
    }

    /// <summary>Replaces framework wording with the layout expression's own terms.</summary>
    private static string Describe(Exception exception)
    {
        return exception is OverflowException
                   ? "the result is outside the 128-bit range that layout expressions support."
                   : exception.Message;
    }
}
