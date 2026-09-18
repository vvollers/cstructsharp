namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using System.Numerics;
using CStructSharp.Syntax;

/// <summary>
///     The one rule for turning a decoded or supplied scalar into a layout variable: a value inside the Int32
///     expression domain becomes an ordinary literal, an integer outside it becomes an exact literal that fails with
///     a precise message the moment an expression uses it, and anything else (NaN, text, objects) removes the stale
///     entry so a caller or definition value cannot masquerade as the field's data.
/// </summary>
internal static class LayoutVariableCapture
{
    /// <summary>Stores <paramref name="value"/> under <paramref name="name"/> in <paramref name="variables"/> using the capture rule.</summary>
    public static void Capture(Dictionary<string, Expr> variables, string name, object? value)
    {
        Expr? expression = ToExpression(value);
        if (expression is null)
        {
            variables.Remove(name);
        }
        else
        {
            variables[name] = expression;
        }
    }

    /// <summary>
    ///     Converts a scalar into its layout-variable expression, or <see langword="null"/> when the value cannot take
    ///     part in expressions at all. Wide integers keep their exact value so an expression that selects them can
    ///     report the actual number instead of an undefined identifier.
    /// </summary>
    public static Expr? ToExpression(object? value)
    {
        if (Int32Capture.TryConvert(value, out int captured))
        {
            return new Literal(captured);
        }

        BigInteger? exact = value switch
        {
            uint u => u,
            long l => l,
            ulong ul => ul,
            Int128 i => (BigInteger)i,
            UInt128 u => (BigInteger)u,
            BigInteger big => big,
            _ => null,
        };
        return exact is { } wide ? new Literal(wide) : null;
    }
}
