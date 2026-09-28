namespace CStructSharp.Expressions;

using System;
using System.Globalization;
using System.Numerics;

/// <summary>
///     Converts decoded or caller-supplied scalars into the signed 128-bit domain of the layout expression language
///     without raising exceptions for the ordinary out-of-range case. Every integer type converts exactly when it fits
///     (all of <c>sbyte</c> through <c>ulong</c>, <c>char</c>, <c>bool</c> as 0/1, <see cref="Int128"/>, and
///     <see cref="UInt128"/> and <see cref="BigInteger"/> up to <see cref="Int128.MaxValue"/>). A floating-point or
///     decimal value is rounded half-to-even first, as <see cref="Convert"/> rounds, and NaN or infinity is rejected;
///     text is parsed as an invariant-culture integer; an <see cref="Enum"/> member converts through its underlying
///     value; any other <see cref="IConvertible"/> converts through <see cref="Convert.ToInt64(object, IFormatProvider)"/>
///     or, when that overflows, <see cref="Convert.ToUInt64(object, IFormatProvider)"/>, with its overflow, cast and
///     format failures reported as a value that cannot be captured.
/// </summary>
internal static class ExpressionValueCapture
{
    // 2^127, exactly representable as a double: a rounded double in [-2^127, 2^127) is a value inside Int128.
    private static readonly double DomainLimit = Math.ScaleB(1.0, 127);
    private static readonly BigInteger DomainMinimum = (BigInteger)Int128.MinValue;
    private static readonly BigInteger DomainMaximum = (BigInteger)Int128.MaxValue;

    /// <summary>
    ///     Attempts the conversion; <see langword="false"/> means the value cannot become a layout variable, which the
    ///     callers report as an out-of-range or non-integer layout variable.
    /// </summary>
    /// <param name="value">The captured or caller-supplied scalar; <see langword="null"/> converts to 0.</param>
    /// <param name="result">The converted value on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when the value has an integer meaning inside the signed 128-bit range.</returns>
    public static bool TryConvert(object? value, out Int128 result)
    {
        switch (value)
        {
        case int i:
            result = i;
            return true;
        case byte b:
            result = b;
            return true;
        case sbyte sb:
            result = sb;
            return true;
        case short s:
            result = s;
            return true;
        case ushort us:
            result = us;
            return true;
        case bool flag:
            result = flag ? Int128.One : Int128.Zero;
            return true;
        case char c:
            result = c;
            return true;
        case uint u:
            result = u;
            return true;
        case long l:
            result = l;
            return true;
        case ulong ul:
            result = ul;
            return true;
        case Int128 wide:
            result = wide;
            return true;
        case UInt128 unsignedWide:
            return TryFromUInt128(unsignedWide, out result);
        case BigInteger big:
            return TryFromBigInteger(big, out result);
        case float f:
            return TryFromDouble(f, out result);
        case double d:
            return TryFromDouble(d, out result);
        case decimal m:
            result = (Int128)Math.Round(m, MidpointRounding.ToEven);
            return true;
        case string text:
            return Int128.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        case null:
            // Convert.ToInt32((object?)null) returns 0 rather than throwing; keep that decision.
            result = Int128.Zero;
            return true;
        default:
            return TrySlowPath(value, out result);
        }
    }

    /// <summary>Converts an unsigned 128-bit value that fits the signed range.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The same value on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is at most <see cref="Int128.MaxValue"/>.</returns>
    public static bool TryFromUInt128(UInt128 value, out Int128 result)
    {
        if (value > (UInt128)Int128.MaxValue)
        {
            result = Int128.Zero;
            return false;
        }

        result = (Int128)value;
        return true;
    }

    /// <summary>Converts an arbitrary-precision integer (an enum member's number) that fits the signed 128-bit range.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The same value on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> lies within the signed 128-bit range.</returns>
    public static bool TryFromBigInteger(BigInteger value, out Int128 result)
    {
        if (value < DomainMinimum || value > DomainMaximum)
        {
            result = Int128.Zero;
            return false;
        }

        result = (Int128)value;
        return true;
    }

    /// <summary>Rounds a double half-to-even and converts it when the result lies in the 128-bit range; NaN and infinity fail.</summary>
    private static bool TryFromDouble(double value, out Int128 result)
    {
        double rounded = Math.Round(value, MidpointRounding.ToEven);

        // Both comparisons are false for NaN, so NaN is rejected with the infinities.
        if (rounded >= -DomainLimit && rounded < DomainLimit)
        {
            result = (Int128)rounded;
            return true;
        }

        result = Int128.Zero;
        return false;
    }

    /// <summary>Converts an enum member or any other convertible value with the invariant culture, returning false instead of throwing.</summary>
    private static bool TrySlowPath(object value, out Int128 result)
    {
        if (value is not IConvertible)
        {
            result = Int128.Zero;
            return false;
        }

        try
        {
            // An enum member converts through its underlying value; an unsigned 64-bit one needs the unsigned path.
            result = value is Enum member && member.GetTypeCode() == TypeCode.UInt64
                         ? Convert.ToUInt64(value, CultureInfo.InvariantCulture)
                         : ToInt64OrUInt64(value);
            return true;
        }
        catch (Exception exception) when (exception is OverflowException or InvalidCastException or FormatException)
        {
            result = Int128.Zero;
            return false;
        }
    }

    /// <summary>Converts through <see cref="Convert.ToInt64(object, IFormatProvider)"/>, retrying unsigned when the value is above its range.</summary>
    private static Int128 ToInt64OrUInt64(object value)
    {
        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
    }
}
