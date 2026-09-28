namespace CStructSharp.Syntax;

using System;

/// <summary>Represents one named value inside an enum declaration.</summary>
internal sealed class EnumValue : IEquatable<EnumValue>
{
    /// <summary>Creates an enum value with an explicit expression.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="value">The expression written after <c>=</c>, evaluated when the enum is compiled.</param>
    public EnumValue(Identifier name, Expr value)
    {
        this.Name = name;
        this.Value = value;
    }

    /// <summary>Creates an enum value whose number will be assigned from its position.</summary>
    /// <param name="name">The member name.</param>
    public EnumValue(Identifier name)
    {
        this.Name = name;
        this.Value = NoneExpr.Instance;
    }

    /// <summary>Gets the member name.</summary>
    public Identifier Name { get; }

    /// <summary>
    ///     Gets the explicit value expression, or <see cref="NoneExpr.Instance"/> when the value follows from the
    ///     previous member.
    /// </summary>
    public Expr Value { get; } = NoneExpr.Instance;

    /// <summary>Checks whether another enum member has the same name and value expression.</summary>
    /// <param name="other">The member to compare with, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when both the name and the value expression are equal.</returns>
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
    /// <returns>The member name and value expression.</returns>
    public override string ToString()
    {
        return $"EnumValue({this.Name},{this.Value})";
    }
}
