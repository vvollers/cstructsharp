namespace CStructSharp.Structure;

using System;

/// <summary>Represents a <c>#define</c> name and expression used by later layout declarations.</summary>
internal class Defines : CStructElement
{
    /// <summary>Creates a named definition whose expression is evaluated before reading or writing.</summary>
    public Defines(Identifier name, Expr value)
    {
        this.Name = name;
        this.Value = value;
    }

    public override Identifier Name { get; }

    public Expr Value { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Defines d && this.Name.Equals(d.Name) && this.Value.Equals(d.Value);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Value);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"Define: {this.Name} = {this.Value}";
    }
}
