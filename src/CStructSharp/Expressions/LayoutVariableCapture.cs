namespace CStructSharp.Expressions;

using System;
using System.Numerics;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Values;

/// <summary>
///     The one rule for turning a field's decoded or written value into a layout variable, used by the compiled
///     engine's reader, writer and path resolver, and by the static plans:
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
    /// <summary>Converts a field's decoded or written value into the slot value its layout variable holds, by the rule above.</summary>
    /// <param name="field">The compiled field, which decides whether the value is an integer at all.</param>
    /// <param name="value">The decoded or written value: a scalar, <see cref="Pointer"/>, <see cref="EnumValueResult"/>, or enum number.</param>
    /// <returns>The slot value: unusable for a field that is not an integer, otherwise <see cref="ToSlotValue(object?)"/>.</returns>
    public static SlotValue ToSlotValue(CompiledField field, object? value)
        => field.NotANumberReason is { } reason ? SlotValue.FromUnusable(new NotANumberVariable(reason)) : ToSlotValue(value);

    /// <summary>
    ///     Converts an integer field's value into the slot value the compiled engine stores for it: an integer in the
    ///     128-bit domain is a literal, a wider one an unusable value that fails naming the number, and anything with no
    ///     integer meaning removes the name (an undefined slot).
    /// </summary>
    /// <param name="value">The value, possibly wrapped as a pointer or an enum result.</param>
    /// <returns>The slot value.</returns>
    public static SlotValue ToSlotValue(object? value)
    {
        Int128 captured;
        bool converted = value switch
        {
            Pointer pointer => Converted(pointer.Address, out captured),
            EnumValueResult enumValue => ExpressionValueCapture.TryFromBigInteger(enumValue.Value, out captured),
            _ => ExpressionValueCapture.TryConvert(value, out captured),
        };
        if (converted)
        {
            return SlotValue.FromLiteral(captured);
        }

        object? wide = value is EnumValueResult result ? result.Value : value;
        return wide is UInt128 or BigInteger ? SlotValue.FromUnusable(new WideValueVariable(wide)) : SlotValue.Undefined;
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
}
