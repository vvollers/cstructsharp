namespace CStructSharp.Syntax;

using System;

/// <summary>Represents a <c>#define</c> name and expression used by later layout declarations.</summary>
internal class Defines : CStructElement
{
    /// <summary>Creates a named definition whose expression is evaluated before reading or writing.</summary>
    /// <param name="name">The name later expressions use to refer to the definition.</param>
    /// <param name="value">The unevaluated expression the name stands for.</param>
    public Defines(Identifier name, Expr value)
    {
        this.Name = name;
        this.Value = value;
    }

    /// <summary>Gets the name later expressions use to refer to the definition.</summary>
    public override Identifier Name { get; }

    /// <summary>Gets the unevaluated expression the name stands for.</summary>
    public Expr Value { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The item to compare with, or <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is a definition with the same name and expression.
    /// </returns>
    public override bool Equals(CStructElement? other)
    {
        return other is Defines d && this.Name.Equals(d.Name) && this.Value.Equals(d.Value);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash code combining the name and the expression.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Value);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>Text of the form <c>Define: name = expression</c>.</returns>
    public override string ToString()
    {
        return $"Define: {this.Name} = {this.Value}";
    }
}
