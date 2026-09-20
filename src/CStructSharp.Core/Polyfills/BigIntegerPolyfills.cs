#if NETSTANDARD2_0
namespace System.Numerics;

/// <summary>The .NET 5+ <c>BigInteger.GetBitLength</c> for the netstandard2.0 build of the Core sources.</summary>
internal static class BigIntegerPolyfills
{
    /// <summary>
    ///     The number of bits needed to represent the magnitude of a non-negative value (0 for zero), which is what
    ///     the runtime's instance method returns for such values; the Core sources only ask for flag members, which
    ///     are never negative.
    /// </summary>
    public static long GetBitLength(this BigInteger value)
    {
        BigInteger magnitude = BigInteger.Abs(value);
        long bits = 0;
        while (!magnitude.IsZero)
        {
            magnitude >>= 1;
            bits++;
        }

        return bits;
    }
}
#endif
