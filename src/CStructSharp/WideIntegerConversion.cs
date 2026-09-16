namespace CStructSharp;

using System;
using System.Globalization;
using System.Numerics;

/// <summary>Caller-value conversions for the codecs whose CLR results are wider than <see cref="long"/> or narrower than <see cref="float"/>.</summary>
internal static class WideIntegerConversion
{
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
