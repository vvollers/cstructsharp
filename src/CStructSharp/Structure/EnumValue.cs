namespace CStructSharp.Structure;

using System;

/// <summary>Represents one named value inside an enum declaration.</summary>
internal sealed class EnumValue : IEquatable<EnumValue>
{
    /// <summary>Creates an enum value with an explicit expression.</summary>
    public EnumValue(Identifier name, Expr value)
    {
        this.Name = name;
        this.Value = value;
    }

    /// <summary>Creates an enum value whose number will be assigned from its position.</summary>
    public EnumValue(Identifier name)
    {
        this.Name = name;
        this.Value = NoneExpr.Instance;
    }

    public Identifier Name { get; }

    public Expr Value { get; } = NoneExpr.Instance;

    /// <summary>Checks whether another enum member has the same name and value expression.</summary>
    public bool Equals(EnumValue? other)
    {
        return other is not null && this.Name.Equals(other.Name) && this.Value.Equals(other.Value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as EnumValue);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Value);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"EnumValue({this.Name},{this.Value})";
    }
}
