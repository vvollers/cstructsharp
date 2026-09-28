namespace CStructSharp.Expressions;

using System;
using System.Globalization;

/// <summary>
///     Converts parsed or caller-supplied scalars into the Int32 domain of the layout expression language without
///     raising exceptions for the ordinary out-of-range case. Semantics match <see cref="Convert.ToInt32(object)"/>
///     exactly for every primitive the codecs produce (including half-to-even rounding of floating-point values
///     and the NaN/infinity rejection); types outside that set fall back to <see cref="Convert.ToInt32(object)"/>
///     with its overflow, cast and format failures reported as a value that cannot be captured, so custom
///     <see cref="IConvertible"/> implementations and <see cref="Enum"/> members convert as they define.
/// </summary>
internal static class Int32Capture
{
    /// <summary>
    ///     Attempts the conversion; <see langword="false"/> means the value cannot become an Int32 layout variable,
    ///     which the callers report as an out-of-range or non-integer layout variable.
    /// </summary>
    /// <param name="value">The captured or caller-supplied scalar; <see langword="null"/> converts to 0.</param>
    /// <param name="result">The converted value on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when the value converts to an Int32 without overflow.</returns>
    public static bool TryConvert(object? value, out int result)
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
            result = flag ? 1 : 0;
            return true;
        case char c:
            result = c;
            return true;
        case uint u:
            return TryFromInt64(u, out result);
        case long l:
            return TryFromInt64(l, out result);
        case ulong ul:
            if (ul > int.MaxValue)
            {
                result = 0;
                return false;
            }

            result = (int)ul;
            return true;
        case float f:
            return TryFromDouble(f, out result);
        case double d:
            return TryFromDouble(d, out result);
        case decimal m:
            return TryFromDecimal(m, out result);
        case null:
            // Convert.ToInt32((object?)null) returns 0 rather than throwing; keep that decision identical.
            result = 0;
            return true;
        default:
            return TrySlowPath(value, out result);
        }
    }

    /// <summary>Narrows a 64-bit integer to Int32, returning false instead of throwing when it does not fit.</summary>
    /// <param name="value">The value to narrow.</param>
    /// <param name="result">The same value as an Int32 on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> lies within the Int32 range.</returns>
    public static bool TryFromInt64(long value, out int result)
    {
        if (value is < int.MinValue or > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)value;
        return true;
    }

    /// <summary>Converts a double to Int32 with Convert.ToInt32's half-to-even rounding, without throwing on overflow or NaN.</summary>
    private static bool TryFromDouble(double value, out int result)
    {
        // Convert.ToInt32(double) accepts exactly the open interval (-2147483648.5, 2147483647.5) and rejects NaN
        // (both comparisons are false for NaN). Within that interval the framework conversion cannot throw, so it
        // is reused to keep the half-to-even rounding bit-identical.
        if (value >= 0 ? value < 2147483647.5 : value >= -2147483648.5)
        {
            result = Convert.ToInt32(value);
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryFromDecimal(decimal value, out int result)
    {
        decimal rounded = Math.Round(value, MidpointRounding.ToEven);
        if (rounded < int.MinValue || rounded > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = decimal.ToInt32(rounded);
        return true;
    }

    /// <summary>Converts any other convertible value (including text) with the invariant culture, returning false instead of throwing.</summary>
    private static bool TrySlowPath(object value, out int result)
    {
        if (value is not IConvertible)
        {
            result = 0;
            return false;
        }

        try
        {
            result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is OverflowException or InvalidCastException or FormatException)
        {
            result = 0;
            return false;
        }
    }
}
