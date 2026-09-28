namespace CStructSharp.Compilation;

using System;
using System.Collections.Immutable;
using CStructSharp.Codecs;

/// <summary>Represents an enum, its exact integer domain, and declaration-order symbolic values.</summary>
internal sealed class CompiledEnumType : CompiledType
{
    /// <summary>Creates the compiled enum from its validated storage and members.</summary>
    /// <param name="symbol">The enum's type symbol.</param>
    /// <param name="underlying">The resolved integer storage type the enum occupies.</param>
    /// <param name="integer">The exact signed or unsigned domain of that storage.</param>
    /// <param name="members">The members in declaration order, with values as storage bits.</param>
    /// <param name="isFlag">Whether the declaration is a <c>flag</c> bitmask.</param>
    public CompiledEnumType(
        CompiledTypeSymbol symbol,
        CompiledTypeReference underlying,
        EnumIntegerCodec integer,
        ImmutableArray<CompiledEnumMember> members,
        bool isFlag = false)
        : base(symbol)
    {
        this.Underlying = underlying;
        this.Integer = integer;
        this.Members = members;
        this.MembersByName = members.ToImmutableDictionary(member => member.Name, StringComparer.Ordinal);
        this.IsFlag = isFlag;
    }

    /// <summary>Whether the declaration is a <c>flag</c> (bitmask) rather than a plain enum.</summary>
    public bool IsFlag { get; }

    /// <summary>The declared enum or flag name.</summary>
    public string Name => this.Symbol.Name;

    /// <summary>Gets the exact integer domain (width and signedness) that converts storage bits to values.</summary>
    public EnumIntegerCodec Integer { get; }

    /// <summary>Gets the members in declaration order; several names may share one value.</summary>
    public ImmutableArray<CompiledEnumMember> Members { get; }

    /// <summary>Gets the members by exact (case-sensitive) name.</summary>
    public ImmutableDictionary<string, CompiledEnumMember> MembersByName { get; }

    /// <summary>Gets the integer storage type the enum is read and written as.</summary>
    public CompiledTypeReference Underlying { get; }

    /// <summary>
    ///     Decomposes a flag value: every nonzero member whose bits are all set, in declaration order, and the bits no
    ///     member accounts for. Called only when a consumer asks for the names, never on the read path itself.
    /// </summary>
    /// <param name="rawBits">The stored value as storage bits in the low bits of the enum's width.</param>
    /// <returns>The names of the fully set members and the bits left unaccounted for (0 when all are named).</returns>
    public (ImmutableArray<string> Names, ulong Remainder) Decompose(ulong rawBits)
    {
        ImmutableArray<string>.Builder names = ImmutableArray.CreateBuilder<string>();
        ulong covered = 0;
        foreach (CompiledEnumMember member in this.Members)
        {
            if (member.RawBits != 0 && (rawBits & member.RawBits) == member.RawBits)
            {
                names.Add(member.Name);
                covered |= member.RawBits;
            }
        }

        return (names.ToImmutable(), rawBits & ~covered);
    }

    /// <summary>Finds the first member, in declaration order, whose value equals the stored bits exactly.</summary>
    /// <param name="rawBits">The stored value as storage bits in the low bits of the enum's width.</param>
    /// <returns>The member name, or null when no member has that value.</returns>
    public string? FindName(ulong rawBits)
    {
        foreach (CompiledEnumMember member in this.Members)
        {
            if (member.RawBits == rawBits)
            {
                return member.Name;
            }
        }

        return null;
    }
}
