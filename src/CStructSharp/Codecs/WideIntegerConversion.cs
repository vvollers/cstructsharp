namespace CStructSharp.Codecs;

using System;
using System.Globalization;
using System.Numerics;

/// <summary>Caller-value conversions for the codecs whose CLR results are wider than <see cref="long"/> or narrower than <see cref="float"/>.</summary>
internal static class WideIntegerConversion
{
    /// <summary>
    ///     Converts a caller value to a signed 128-bit integer: 128-bit and big integers, other CLR integer
    ///     types, or invariant-culture decimal text.
    /// </summary>
    /// <param name="value">The caller value.</param>
    /// <returns>The same number as <see cref="Int128"/>.</returns>
    /// <exception cref="OverflowException">The number lies outside the <see cref="Int128"/> range.</exception>
    /// <exception cref="FormatException">The text is not an integer.</exception>
    /// <exception cref="InvalidCastException">The value is not an integer or integer text.</exception>
    public static Int128 ToInt128(object value)
    {
        return value switch
        {
            Int128 exact => exact,
            UInt128 unsigned => checked((Int128)unsigned),
            BigInteger big => checked((Int128)big),
            string text => Int128.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ when EnumIntegerCodec.TryConvertIntegral(value, out BigInteger integral) => checked((Int128)integral),
            _ => throw new InvalidCastException("Value cannot be converted to a 128-bit integer."),
        };
    }

    /// <summary>
    ///     Converts a caller value to an unsigned 128-bit integer: 128-bit and big integers, other CLR integer
    ///     types, or invariant-culture decimal text.
    /// </summary>
    /// <param name="value">The caller value.</param>
    /// <returns>The same number as <see cref="UInt128"/>.</returns>
    /// <exception cref="OverflowException">The number is negative or too large for <see cref="UInt128"/>.</exception>
    /// <exception cref="FormatException">The text is not an unsigned integer.</exception>
    /// <exception cref="InvalidCastException">The value is not an integer or integer text.</exception>
    public static UInt128 ToUInt128(object value)
    {
        return value switch
        {
            UInt128 exact => exact,
            Int128 signed => checked((UInt128)signed),
            BigInteger big => checked((UInt128)big),
            string text => UInt128.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            _ when EnumIntegerCodec.TryConvertIntegral(value, out BigInteger integral) => checked((UInt128)integral),
            _ => throw new InvalidCastException("Value cannot be converted to an unsigned 128-bit integer."),
        };
    }

    /// <summary>
    ///     Converts a caller value to a half-precision float, rounding to the nearest representable value; a
    ///     magnitude beyond the <see cref="Half"/> range becomes infinity.
    /// </summary>
    /// <param name="value">A floating-point number, another convertible number, or invariant text.</param>
    /// <returns>The nearest <see cref="Half"/> value.</returns>
    /// <exception cref="FormatException">The text is not a number.</exception>
    /// <exception cref="InvalidCastException">The value is not convertible to a number.</exception>
    public static Half ToHalf(object value)
    {
        return value switch
        {
            Half exact => exact,
            float single => (Half)single,
            double real => (Half)real,
            string text => Half.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => (Half)Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
    }
}
