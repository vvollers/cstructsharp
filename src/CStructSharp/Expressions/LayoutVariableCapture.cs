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
///         pointer's stored address - becomes an exact literal inside the signed 128-bit expression domain (every
///         integer up to 64 bits fits), and outside it (an unsigned 128-bit value at or above 2^127) a variable that
///         fails with the exact number when an expression uses it (<see cref="WideValueVariable"/>).</item>
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
        // A pointer's address and an enum's number are unwrapped without boxing them again.
        Int128 captured;
        bool converted = value switch
        {
            Pointer pointer => Converted(pointer.Address, out captured),
            EnumValueResult enumValue => ExpressionValueCapture.TryFromBigInteger(enumValue.Value, out captured),
            _ => ExpressionValueCapture.TryConvert(value, out captured),
        };
        if (converted)
        {
            return SmallLiteral(captured) ?? new Literal(captured);
        }

        object? wide = value is EnumValueResult result ? result.Value : value;
        return wide is UInt128 or BigInteger
                   ? new WideValueVariable(wide)
                   : null;
    }

    /// <summary>Stores a value that is already in the domain.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">Receives <paramref name="value"/>.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    private static bool Converted(long value, out Int128 result)
    {
        result = value;
        return true;
    }

    /// <summary>The shared literal of a value in the cached range (-128 to 1023), or <see langword="null"/> outside it.</summary>
    /// <param name="value">The captured value.</param>
    /// <returns>The literal, created on first use (a benign race may create two equal ones), or <see langword="null"/>.</returns>
    private static Literal? SmallLiteral(Int128 value)
    {
        if (value < SmallestCached || value >= SmallestCached + SmallLiterals.Length)
        {
            return null;
        }

        int index = (int)value - SmallestCached;
        return SmallLiterals[index] ??= new Literal(value);
    }
}
