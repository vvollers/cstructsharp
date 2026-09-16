namespace CStructSharp;

using System.Collections.Generic;
using System.Numerics;

/// <summary>One member of an enum or flag.</summary>
public sealed class LayoutEnumMemberInfo
{
    internal LayoutEnumMemberInfo(string name, BigInteger value)
    {
        this.Name = name;
        this.Value = value;
    }

    /// <summary>Gets the member name.</summary>
    public string Name { get; }

    /// <summary>Gets the member's exact value.</summary>
    public BigInteger Value { get; }
}
