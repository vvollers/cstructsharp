namespace CStructSharp.Compilation;

using System;
using System.Collections.Immutable;
using CStructSharp.Codecs;

/// <summary>Represents an enum, its exact integer domain, and declaration-order symbolic values.</summary>
internal sealed class CompiledEnumType : CompiledType
{
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

    public EnumIntegerCodec Integer { get; }

    public ImmutableArray<CompiledEnumMember> Members { get; }

    public ImmutableDictionary<string, CompiledEnumMember> MembersByName { get; }

    public CompiledTypeReference Underlying { get; }

    /// <summary>
    ///     Decomposes a flag value: every nonzero member whose bits are all set, in declaration order, and the bits no
    ///     member accounts for. Called only when a consumer asks for the names, never on the read path itself.
    /// </summary>
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
