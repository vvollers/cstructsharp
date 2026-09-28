namespace CStructSharp.Introspection;

using System.Numerics;

/// <summary>One member of an enum or flag.</summary>
public sealed class LayoutEnumMemberInfo
{
    /// <summary>Creates the description of one enum member.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member's exact mathematical value, sign included.</param>
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
