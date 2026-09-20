namespace CStructSharp;

using System;

/// <summary>Names the layout member a property or field of a <c>[CStructMapped]</c> class maps to, when the C# name differs.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class CStructMemberAttribute : Attribute
{
    /// <summary>Maps the member to <paramref name="name"/>.</summary>
    /// <param name="name">The layout member name, spelled as the layout declares it.</param>
    public CStructMemberAttribute(string name)
    {
        this.Name = name;
    }

    /// <summary>Gets the layout member name.</summary>
    public string Name { get; }
}
