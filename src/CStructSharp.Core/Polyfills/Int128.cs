#if NETSTANDARD2_0
namespace System;

using System.Globalization;
using System.Numerics;

/// <summary>
///     A minimal <c>System.Int128</c> for the netstandard2.0 build of the Core sources (the source generator), which
///     folds layout expressions in the same signed 128-bit domain as the runtime. It stores the value as a
///     <see cref="BigInteger"/> kept inside the 128-bit range: the generator only folds constants, so clarity matters
///     more than speed. Unchecked arithmetic wraps modulo 2^128 and the <c>checked</c> operators throw
///     <see cref="OverflowException"/>, exactly as the runtime type does.
/// </summary>
internal readonly struct Int128 : IEquatable<Int128>, IComparable<Int128>, IFormattable
{
    private static readonly BigInteger Modulus = BigInteger.One << 128;
    private static readonly BigInteger Minimum = -(BigInteger.One << 127);
    private static readonly BigInteger Maximum = (BigInteger.One << 127) - 1;

    private readonly BigInteger value;

    /// <summary>Creates a value from its upper and lower 64-bit halves, as the runtime constructor does.</summary>
    /// <param name="upper">The upper 64 bits, including the sign bit.</param>
    /// <param name="lower">The lower 64 bits.</param>
    public Int128(ulong upper, ulong lower)
    {
        this.value = Wrap((new BigInteger(upper) << 64) | new BigInteger(lower));
    }

    /// <summary>Stores an already range-checked value.</summary>
    private Int128(BigInteger value, bool unused)
    {
        _ = unused;
        this.value = value;
    }

    /// <summary>Gets the smallest value, -2^127.</summary>
    public static Int128 MinValue => new(Minimum, false);

    /// <summary>Gets the largest value, 2^127 - 1.</summary>
    public static Int128 MaxValue => new(Maximum, false);

    /// <summary>Gets zero.</summary>
    public static Int128 Zero => default;

    /// <summary>Gets one.</summary>
    public static Int128 One => new(BigInteger.One, false);

    /// <summary>Gets minus one.</summary>
    public static Int128 NegativeOne => new(BigInteger.MinusOne, false);

    /// <summary>Widens a signed byte.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(sbyte value) => new(value, false);

    /// <summary>Widens a byte.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(byte value) => new(value, false);

    /// <summary>Widens a 16-bit integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(short value) => new(value, false);

    /// <summary>Widens an unsigned 16-bit integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(ushort value) => new(value, false);

    /// <summary>Widens a character's code.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(char value) => new(value, false);

    /// <summary>Widens a 32-bit integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(int value) => new(value, false);

    /// <summary>Widens an unsigned 32-bit integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(uint value) => new(value, false);

    /// <summary>Widens a 64-bit integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(long value) => new(value, false);

    /// <summary>Widens an unsigned 64-bit integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Int128(ulong value) => new(value, false);

    /// <summary>Widens to an arbitrary-precision integer.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator BigInteger(Int128 value) => value.value;

    /// <summary>Narrows an arbitrary-precision integer, throwing when it does not fit, as the runtime does.</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="OverflowException">The value is outside the 128-bit range.</exception>
    public static explicit operator Int128(BigInteger value)
        => value < Minimum || value > Maximum ? throw new OverflowException() : new Int128(value, false);

    /// <summary>Truncates to the low 32 bits, as an unchecked conversion does.</summary>
    /// <param name="value">The value.</param>
    public static explicit operator int(Int128 value) => unchecked((int)(uint)(value.value & uint.MaxValue));

    /// <summary>Truncates to the low 64 bits, as an unchecked conversion does.</summary>
    /// <param name="value">The value.</param>
    public static explicit operator long(Int128 value) => unchecked((long)(ulong)(value.value & ulong.MaxValue));

    /// <summary>Adds, wrapping modulo 2^128.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The wrapped sum.</returns>
    public static Int128 operator +(Int128 left, Int128 right) => new(Wrap(left.value + right.value), false);

    /// <summary>Adds, throwing when the sum leaves the range.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The sum is outside the 128-bit range.</exception>
    public static Int128 operator checked +(Int128 left, Int128 right) => Checked(left.value + right.value);

    /// <summary>Subtracts, wrapping modulo 2^128.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The wrapped difference.</returns>
    public static Int128 operator -(Int128 left, Int128 right) => new(Wrap(left.value - right.value), false);

    /// <summary>Subtracts, throwing when the difference leaves the range.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The difference is outside the 128-bit range.</exception>
    public static Int128 operator checked -(Int128 left, Int128 right) => Checked(left.value - right.value);

    /// <summary>Multiplies, wrapping modulo 2^128.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The wrapped product.</returns>
    public static Int128 operator *(Int128 left, Int128 right) => new(Wrap(left.value * right.value), false);

    /// <summary>Multiplies, throwing when the product leaves the range.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product.</returns>
    /// <exception cref="OverflowException">The product is outside the 128-bit range.</exception>
    public static Int128 operator checked *(Int128 left, Int128 right) => Checked(left.value * right.value);

    /// <summary>Divides, truncating toward zero.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    /// <exception cref="OverflowException">The quotient of <see cref="MinValue"/> and -1 is outside the range.</exception>
    public static Int128 operator /(Int128 left, Int128 right) => Checked(BigInteger.Divide(left.value, right.value));

    /// <summary>Computes the remainder, whose sign follows the dividend.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The remainder.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static Int128 operator %(Int128 left, Int128 right) => new(BigInteger.Remainder(left.value, right.value), false);

    /// <summary>Negates, wrapping <see cref="MinValue"/> to itself.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The wrapped negation.</returns>
    public static Int128 operator -(Int128 value) => new(Wrap(-value.value), false);

    /// <summary>Negates, throwing for <see cref="MinValue"/>.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negation.</returns>
    /// <exception cref="OverflowException"><paramref name="value"/> is <see cref="MinValue"/>.</exception>
    public static Int128 operator checked -(Int128 value) => Checked(-value.value);

    /// <summary>Inverts every bit.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The complement.</returns>
    public static Int128 operator ~(Int128 value) => new(-value.value - 1, false);

    /// <summary>Computes the bitwise AND of two's-complement values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in both.</returns>
    public static Int128 operator &(Int128 left, Int128 right) => new(left.value & right.value, false);

    /// <summary>Computes the bitwise OR of two's-complement values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in either.</returns>
    public static Int128 operator |(Int128 left, Int128 right) => new(left.value | right.value, false);

    /// <summary>Computes the bitwise exclusive OR of two's-complement values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in exactly one.</returns>
    public static Int128 operator ^(Int128 left, Int128 right) => new(left.value ^ right.value, false);

    /// <summary>Shifts left by the count masked to 0-127, wrapping modulo 2^128.</summary>
    /// <param name="value">The value.</param>
    /// <param name="count">The shift count; only its low seven bits are used, as the runtime does.</param>
    /// <returns>The shifted value.</returns>
    public static Int128 operator <<(Int128 value, int count) => new(Wrap(value.value << (count & 127)), false);

    /// <summary>Shifts right arithmetically by the count masked to 0-127.</summary>
    /// <param name="value">The value; its sign is copied into the vacated bits.</param>
    /// <param name="count">The shift count; only its low seven bits are used, as the runtime does.</param>
    /// <returns>The shifted value.</returns>
    public static Int128 operator >>(Int128 value, int count) => new(value.value >> (count & 127), false);

    /// <summary>Compares for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether the values are equal.</returns>
    public static bool operator ==(Int128 left, Int128 right) => left.value == right.value;

    /// <summary>Compares for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether the values differ.</returns>
    public static bool operator !=(Int128 left, Int128 right) => left.value != right.value;

    /// <summary>Tests whether the left operand is smaller.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether <paramref name="left"/> is less than <paramref name="right"/>.</returns>
    public static bool operator <(Int128 left, Int128 right) => left.value < right.value;

    /// <summary>Tests whether the left operand is not larger.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether <paramref name="left"/> is at most <paramref name="right"/>.</returns>
    public static bool operator <=(Int128 left, Int128 right) => left.value <= right.value;

    /// <summary>Tests whether the left operand is larger.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether <paramref name="left"/> is greater than <paramref name="right"/>.</returns>
    public static bool operator >(Int128 left, Int128 right) => left.value > right.value;

    /// <summary>Tests whether the left operand is not smaller.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>Whether <paramref name="left"/> is at least <paramref name="right"/>.</returns>
    public static bool operator >=(Int128 left, Int128 right) => left.value >= right.value;

    /// <summary>Compares two values for equality.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>Whether the values are equal.</returns>
    public bool Equals(Int128 other) => this.value == other.value;

    /// <summary>Compares with a boxed value for equality.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>Whether <paramref name="obj"/> is an equal <see cref="Int128"/>.</returns>
    public override bool Equals(object? obj) => obj is Int128 other && this.Equals(other);

    /// <summary>Returns a hash code consistent with <see cref="Equals(Int128)"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => this.value.GetHashCode();

    /// <summary>Orders two values.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero, or a positive number as this value is smaller, equal, or larger.</returns>
    public int CompareTo(Int128 other) => this.value.CompareTo(other.value);

    /// <summary>Formats the value in decimal with the invariant culture (the generator never formats for a user).</summary>
    /// <returns>The decimal text.</returns>
    public override string ToString() => this.value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats the value in decimal with the given culture.</summary>
    /// <param name="provider">The culture, or <see langword="null"/> for the current one.</param>
    /// <returns>The decimal text.</returns>
    public string ToString(IFormatProvider? provider) => this.value.ToString(provider);

    /// <summary>Formats the value with a standard numeric format.</summary>
    /// <param name="format">The format string, or <see langword="null"/> for decimal.</param>
    /// <param name="formatProvider">The culture, or <see langword="null"/> for the current one.</param>
    /// <returns>The formatted text.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider) => this.value.ToString(format, formatProvider);

    /// <summary>Reduces an arbitrary integer to the 128-bit two's-complement value with the same low 128 bits.</summary>
    private static BigInteger Wrap(BigInteger value)
    {
        BigInteger reduced = BigInteger.Remainder(value, Modulus);
        if (reduced.Sign < 0)
        {
            reduced += Modulus;
        }

        return reduced > Maximum ? reduced - Modulus : reduced;
    }

    /// <summary>Returns an exact result, throwing when it is outside the 128-bit range.</summary>
    private static Int128 Checked(BigInteger value)
        => value < Minimum || value > Maximum ? throw new OverflowException() : new Int128(value, false);
}
#endif
