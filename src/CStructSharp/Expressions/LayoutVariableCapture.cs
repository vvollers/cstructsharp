namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using System.Numerics;
using CStructSharp.Compilation;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     The one rule for turning a field's decoded or written value into a layout variable, used by every reader,
///     writer and path resolver:
///     <list type="bullet">
///         <item>An integer field's value - an integer, a character's code, <c>bool</c> as 1 or 0, an enum's number, a
///         pointer's stored address - becomes an ordinary literal inside the Int32 expression domain, and outside it a
///         variable that fails with the exact number when an expression uses it (<see cref="WideValueVariable"/>).</item>
///         <item>A field that is not an integer (text, an array, a struct, a floating-point value, ...) makes the name
///         unusable (<see cref="NotANumberVariable"/>): layout construction already rejects a name only such fields
///         supply, so this covers a name a numeric field or a definition shares.</item>
///         <item>A value with no integer meaning (a caller object a writer could not have encoded) removes the entry, so
///         an older caller or definition value cannot masquerade as the field's data.</item>
///     </list>
/// </summary>
internal static class LayoutVariableCapture
{
    // Literals for the small integers counts, lengths and kinds usually hold, created on first use and shared: a
    // Literal is immutable, so every capture of the same value can use one instance instead of allocating another.
    private const int SmallestCached = -128;
    private static readonly Literal?[] SmallLiterals = new Literal?[1152];

    /// <summary>Stores the layout variable of <paramref name="field"/>'s value under <paramref name="name"/>.</summary>
    /// <param name="variables">The operation's layout variables.</param>
    /// <param name="name">The field's name (the declared name, or the root name for a root field).</param>
    /// <param name="field">The compiled field, which decides whether the value is an integer at all.</param>
    /// <param name="value">The decoded or written value: a scalar, <see cref="Pointer"/>, <see cref="EnumValueResult"/>, or enum number.</param>
    public static void Capture(Dictionary<string, Expr> variables, string name, CompiledField field, object? value)
    {
        if (field.NotANumberReason is { } reason)
        {
            variables[name] = new NotANumberVariable(reason);
            return;
        }

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
    ///     Converts an integer field's value into its layout-variable expression, or <see langword="null"/> when the value
    ///     has no integer meaning. Wide integers keep their exact value so an expression that selects them can report
    ///     the actual number instead of an undefined identifier.
    /// </summary>
    /// <param name="value">The value, possibly wrapped as a pointer or an enum result.</param>
    /// <returns>The expression, or <see langword="null"/>.</returns>
    public static Expr? ToExpression(object? value)
    {
        value = value switch
        {
            Pointer pointer => pointer.Address,
            EnumValueResult enumValue => enumValue.Value,
            bool flag => flag ? 1 : 0,
            _ => value,
        };

        // An enum's number is a BigInteger, which the general Int32 conversion does not know.
        if (value is BigInteger big && big >= int.MinValue && big <= int.MaxValue)
        {
            return SmallLiteral((int)big) ?? new Literal((int)big);
        }

        if (Int32Capture.TryConvert(value, out int captured))
        {
            return SmallLiteral(captured) ?? new Literal(captured);
        }

        return value is uint or long or ulong or Int128 or UInt128 or BigInteger
                   ? new WideValueVariable(value)
                   : null;
    }

    /// <summary>The shared literal of a value in the cached range (-128 to 1023), or <see langword="null"/> outside it.</summary>
    /// <param name="value">The captured value.</param>
    /// <returns>The literal, created on first use (a benign race may create two equal ones), or <see langword="null"/>.</returns>
    private static Literal? SmallLiteral(int value)
    {
        int index = value - SmallestCached;
        if ((uint)index >= (uint)SmallLiterals.Length)
        {
            return null;
        }

        return SmallLiterals[index] ??= new Literal(value);
    }
}
