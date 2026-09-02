namespace CStructSharp;

using System;
using System.Globalization;
using System.Numerics;

/// <summary>
///     Represents an enum payload parsed from binary data.
/// </summary>
/// <remarks>
///     The exact numeric value and storage bits remain available even when no declared member matches. Writers
///     validate the declaration name, width, signedness, raw bits, and optional member name before reusing this model.
/// </remarks>
public sealed class EnumValueResult
{
    /// <summary>Creates one self-describing value from its validated compiled enum descriptor.</summary>
    internal EnumValueResult(
        string enumName,
        string? name,
        BigInteger value,
        ulong rawBits,
        string storageType,
        int bitWidth,
        bool isSigned)
    {
        if (string.IsNullOrWhiteSpace(enumName))
        {
            throw new ArgumentException("An enum name is required.", nameof(enumName));
        }

        this.Enum = enumName;
        this.Name = name;
        this.Value = value;
        this.RawBits = rawBits;
        this.StorageType = storageType;
        this.BitWidth = bitWidth;
        this.IsSigned = isSigned;
    }

    /// <summary>Gets the enum declaration name.</summary>
    public string Enum { get; }

    /// <summary>Gets the first matching declared member name, or <see langword="null"/> for an unknown payload.</summary>
    public string? Name { get; }

    /// <summary>Gets the exact mathematical payload without narrowing its declared integer domain.</summary>
    public BigInteger Value { get; }

    /// <summary>Gets the payload's unsigned storage bits, masked to <see cref="BitWidth"/>.</summary>
    public ulong RawBits { get; }

    /// <summary>Gets the canonical backing codec name, such as <c>int16</c> or <c>uint64</c>.</summary>
    public string StorageType { get; }

    /// <summary>Gets the declared backing width: 8, 16, 32, or 64 bits.</summary>
    public int BitWidth { get; }

    /// <summary>Gets whether the declared backing domain interprets its high bit as a sign bit.</summary>
    public bool IsSigned { get; }

    /// <summary>Returns the declared enum name when known, otherwise the numeric value from the stream.</summary>
    /// <returns>The matching member name or an invariant decimal representation of <see cref="Value"/>.</returns>
    public override string ToString()
    {
        return this.Name ?? this.Value.ToString(CultureInfo.InvariantCulture);
    }
}
