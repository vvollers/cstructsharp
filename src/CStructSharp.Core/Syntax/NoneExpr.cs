namespace CStructSharp.Syntax;

using System.Collections.Generic;

/// <summary>Represents the absence of an optional expression, such as an omitted array length.</summary>
internal class NoneExpr : Expr
{
    private const int VALUE = 0;

    /// <summary>The shared instance used wherever an optional expression was omitted.</summary>
    public static readonly NoneExpr Instance = new();

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> is also a <see cref="NoneExpr"/>.</returns>
    public override bool Equals(Expr? other)
    {
        return other is NoneExpr;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>The same constant for every instance.</returns>
    public override int GetHashCode()
    {
        return VALUE.GetHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The text <c>NoneExpr(0)</c>.</returns>
    public override string ToString()
    {
        return "NoneExpr(0)";
    }
}
