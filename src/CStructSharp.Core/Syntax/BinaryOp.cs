namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;

/// <summary>Represents an expression that combines a left and right value with an arithmetic or bitwise operator.</summary>
internal class BinaryOp : Expr
{
    /// <summary>Creates a binary expression from its operator and two input expressions.</summary>
    /// <param name="type">The operator that combines the operands.</param>
    /// <param name="left">The expression left of the operator.</param>
    /// <param name="right">The expression right of the operator.</param>
    public BinaryOp(BinaryOperatorType type, Expr left, Expr right)
    {
        this.Type = type;
        this.Left = left;
        this.Right = right;
    }

    /// <summary>Gets the operand written left of the operator.</summary>
    public Expr Left { get; }

    /// <summary>Gets the operand written right of the operator.</summary>
    public Expr Right { get; }

    /// <summary>Gets the operator that combines <see cref="Left"/> and <see cref="Right"/>.</summary>
    public BinaryOperatorType Type { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is a binary expression with the same operator and equal
    ///     operands.
    /// </returns>
    public override bool Equals(Expr? other)
    {
        return other is BinaryOp b && this.Type == b.Type && this.Left.Equals(b.Left) && this.Right.Equals(b.Right);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash combining the operator and both operands.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Type, this.Left, this.Right);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The operands and operator name in parentheses, prefixed with <c>BinaryOp: </c>.</returns>
    public override string ToString()
    {
        return $"BinaryOp: ({this.Left} {this.Type} {this.Right})";
    }
}
