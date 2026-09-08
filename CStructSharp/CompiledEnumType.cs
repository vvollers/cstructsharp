namespace CStructSharp;

using System;
using System.Collections.Immutable;

/// <summary>Represents an enum, its exact integer domain, and declaration-order symbolic values.</summary>
internal sealed class CompiledEnumType : CompiledType
{
    public CompiledEnumType(
        CompiledTypeSymbol symbol,
        CompiledTypeReference underlying,
        EnumIntegerCodec integer,
        ImmutableArray<CompiledEnumMember> members)
        : base(symbol)
    {
        this.Underlying = underlying;
        this.Integer = integer;
        this.Members = members;
        this.MembersByName = members.ToImmutableDictionary(member => member.Name, StringComparer.Ordinal);
    }

    public EnumIntegerCodec Integer { get; }

    public ImmutableArray<CompiledEnumMember> Members { get; }

    public ImmutableDictionary<string, CompiledEnumMember> MembersByName { get; }

    public CompiledTypeReference Underlying { get; }

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
