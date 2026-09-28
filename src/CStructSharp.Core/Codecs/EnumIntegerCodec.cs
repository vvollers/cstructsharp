namespace CStructSharp.Codecs;

using System;
using System.Globalization;
using System.Numerics;

/// <summary>Describes one exact integer storage domain without introducing a public numeric union.</summary>
internal sealed class EnumIntegerCodec
{
    private EnumIntegerCodec(string storageType, int bitWidth, bool isSigned)
    {
        this.StorageType = storageType;
        this.BitWidth = bitWidth;
        this.IsSigned = isSigned;
        this.Minimum = isSigned ? -(BigInteger.One << (bitWidth - 1)) : BigInteger.Zero;
        this.Maximum = isSigned
                           ? (BigInteger.One << (bitWidth - 1)) - BigInteger.One
                           : (BigInteger.One << bitWidth) - BigInteger.One;
    }

    /// <summary>Gets the storage width in bits: 8, 16, 32, or 64.</summary>
    public int BitWidth { get; }

    /// <summary>Gets a value indicating whether the storage is two's-complement signed.</summary>
    public bool IsSigned { get; }

    /// <summary>Gets the largest value the storage can hold.</summary>
    public BigInteger Maximum { get; }

    /// <summary>Gets the smallest value the storage can hold; zero for unsigned storage.</summary>
    public BigInteger Minimum { get; }

    /// <summary>Gets the storage size in bytes.</summary>
    public int SizeInBytes => this.BitWidth / 8;

    /// <summary>Gets the canonical storage spelling, such as <c>uint8</c> for <c>byte</c>.</summary>
    public string StorageType { get; }

    /// <summary>Creates a canonical descriptor for an accepted direct spelling.</summary>
    /// <param name="spelling">The storage type spelling, such as <c>uint16</c> or <c>byte</c>.</param>
    /// <param name="codec">Receives the descriptor, or <see langword="null"/> for an unknown spelling.</param>
    /// <returns><see langword="true"/> when <paramref name="spelling"/> names a supported storage type.</returns>
    public static bool TryCreate(string spelling, out EnumIntegerCodec? codec)
    {
        codec = spelling switch
        {
            "byte" or "uint8" => new EnumIntegerCodec("uint8", 8, false),
            "int8" => new EnumIntegerCodec("int8", 8, true),
            "uint16" => new EnumIntegerCodec("uint16", 16, false),
            "int16" => new EnumIntegerCodec("int16", 16, true),
            "uint32" => new EnumIntegerCodec("uint32", 32, false),
            "int32" => new EnumIntegerCodec("int32", 32, true),
            "uint64" => new EnumIntegerCodec("uint64", 64, false),
            "int64" => new EnumIntegerCodec("int64", 64, true),
            _ => null,
        };
        return codec is not null;
    }

    /// <summary>Accepts only mathematical integral CLR inputs; floating/fractional conversion is never implicit.</summary>
    /// <param name="value">The value to convert; any CLR integer type or <see cref="BigInteger"/> is accepted.</param>
    /// <param name="result">Receives the exact value, or zero when the conversion fails.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is an integral CLR value.</returns>
    public static bool TryConvertIntegral(object? value, out BigInteger result)
    {
        switch (value)
        {
        case BigInteger number:
            result = number;
            return true;
        case sbyte number:
            result = number;
            return true;
        case byte number:
            result = number;
            return true;
        case short number:
            result = number;
            return true;
        case ushort number:
            result = number;
            return true;
        case int number:
            result = number;
            return true;
        case uint number:
            result = number;
            return true;
        case long number:
            result = number;
            return true;
        case ulong number:
            result = number;
            return true;
        default:
            result = BigInteger.Zero;
            return false;
        }
    }

    /// <summary>Converts a primitive reader result into its exact mathematical value.</summary>
    /// <param name="value">The boxed integer the primitive reader produced.</param>
    /// <returns>The value as a <see cref="BigInteger"/>.</returns>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="value"/> is not an integer or lies outside this domain.
    /// </exception>
    public BigInteger FromStorageValue(object value)
    {
        if (!TryConvertIntegral(value, out BigInteger result) || !this.Contains(result))
        {
            throw new InvalidOperationException(
                $"Enum storage reader for {this.StorageType} returned an incompatible value.");
        }

        return result;
    }

    /// <summary>Converts an in-domain mathematical value to its declared-width storage bits.</summary>
    /// <param name="value">The mathematical value to encode.</param>
    /// <returns>The two's-complement bits in the low <see cref="BitWidth"/> bits; the higher bits are zero.</returns>
    /// <exception cref="OverflowException"><paramref name="value"/> is outside the domain.</exception>
    public ulong ToRawBits(BigInteger value)
    {
        this.EnsureInRange(value);
        BigInteger raw = value < BigInteger.Zero
                             ? (BigInteger.One << this.BitWidth) + value
                             : value;
        return (ulong)raw;
    }

    /// <summary>Interprets declared-width storage bits through this descriptor's signedness.</summary>
    /// <param name="rawBits">The storage bits in the low <see cref="BitWidth"/> bits.</param>
    /// <returns>The mathematical value, sign-extended when the domain is signed.</returns>
    public BigInteger FromRawBits(ulong rawBits)
    {
        BigInteger raw = rawBits;
        if (!this.IsSigned)
        {
            return raw;
        }

        ulong signBit = 1UL << (this.BitWidth - 1);
        return (rawBits & signBit) == 0
                   ? raw
                   : raw - (BigInteger.One << this.BitWidth);
    }

    /// <summary>Converts an exact validated value to the primitive writer's natural CLR type.</summary>
    /// <param name="value">The mathematical value to convert.</param>
    /// <returns>The value boxed as the CLR integer type that matches <see cref="StorageType"/>.</returns>
    /// <exception cref="OverflowException"><paramref name="value"/> is outside the domain.</exception>
    public object ToStorageValue(BigInteger value)
    {
        this.EnsureInRange(value);
        return this.StorageType switch
        {
            "int8" => (sbyte)value,
            "uint8" => (byte)value,
            "int16" => (short)value,
            "uint16" => (ushort)value,
            "int32" => (int)value,
            "uint32" => (uint)value,
            "int64" => (long)value,
            "uint64" => (ulong)value,
            _ => throw new InvalidOperationException(
                "Unknown enum integer storage type: " + this.StorageType),
        };
    }

    /// <summary>Tests whether a mathematical value lies within this storage domain.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="value"/> is between <see cref="Minimum"/> and
    ///     <see cref="Maximum"/> inclusive.
    /// </returns>
    public bool Contains(BigInteger value)
    {
        return value >= this.Minimum && value <= this.Maximum;
    }

    /// <summary>Rejects a mathematical value that lies outside this storage domain.</summary>
    /// <param name="value">The value to check.</param>
    /// <exception cref="OverflowException"><paramref name="value"/> is outside the domain.</exception>
    public void EnsureInRange(BigInteger value)
    {
        if (!this.Contains(value))
        {
            throw new OverflowException(
                FormattableString.Invariant($"Value {value} is outside the {this.StorageType} enum domain."));
        }
    }
}
