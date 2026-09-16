namespace CStructSharp;

using System;
using System.Collections.Immutable;
using System.Numerics;

/// <summary>
///     The value read from a <c>flag</c> declaration: an <see cref="EnumValueResult"/> whose bits are also
///     decomposed into the member names that are set. <see cref="EnumValueResult.Name"/> is the single member whose
///     value equals the whole stored value, when one exists; <see cref="Names"/> lists every set member.
/// </summary>
public sealed class FlagValueResult : EnumValueResult
{
    private readonly Func<ulong, (ImmutableArray<string> Names, ulong Remainder)> decompose;
    private ImmutableArray<string> names;
    private ulong remainder;
    private bool decomposed;

    internal FlagValueResult(
        string enumName,
        string? name,
        BigInteger value,
        ulong rawBits,
        string storageType,
        int bitWidth,
        bool isSigned,
        Func<ulong, (ImmutableArray<string> Names, ulong Remainder)> decompose)
        : base(enumName, name, value, rawBits, storageType, bitWidth, isSigned)
    {
        this.decompose = decompose;
    }

    /// <summary>Gets the members whose bits are all set in the value, in declaration order. Computed on first use.</summary>
    public ImmutableArray<string> Names
    {
        get
        {
            this.EnsureDecomposed();
            return this.names;
        }
    }

    /// <summary>Gets the bits of the value that no member accounts for (zero when every bit is named).</summary>
    public ulong Remainder
    {
        get
        {
            this.EnsureDecomposed();
            return this.remainder;
        }
    }

    /// <summary>Returns whether the named member's bits are all set in the value.</summary>
    /// <param name="member">The declared member name to test.</param>
    /// <returns><see langword="true"/> when every bit of the member is set; otherwise, <see langword="false"/>.</returns>
    public bool Has(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return this.Names.Contains(member, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        this.EnsureDecomposed();
        if (this.names.IsEmpty)
        {
            return base.ToString();
        }

        string joined = string.Join("|", this.names);
        return this.remainder == 0
                   ? joined
                   : joined + "|0x" + this.remainder.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void EnsureDecomposed()
    {
        if (!this.decomposed)
        {
            // A benign race computes the same immutable result twice.
            (this.names, this.remainder) = this.decompose(this.RawBits);
            this.decomposed = true;
        }
    }
}
