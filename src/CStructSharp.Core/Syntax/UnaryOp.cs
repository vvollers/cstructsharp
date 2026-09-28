namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;

/// <summary>Represents an expression that changes one value with negation or bitwise complement.</summary>
internal class UnaryOp : Expr
{
    /// <summary>Creates a unary expression from its operator and input expression.</summary>
    /// <param name="type">The operator: logical not, negation, or bitwise complement.</param>
    /// <param name="expr">The operand the operator applies to.</param>
    public UnaryOp(UnaryOperatorType type, Expr expr)
    {
        this.Type = type;
        this.Expr = expr;
    }

    /// <summary>Gets the operand the operator applies to.</summary>
    public Expr Expr { get; }

    /// <summary>Gets the operator applied to the operand.</summary>
    public UnaryOperatorType Type { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with this one.</param>
    /// <returns>True when both are unary expressions with the same operator and equal operands.</returns>
    public override bool Equals(Expr? other)
    {
        return other is UnaryOp u && this.Type == u.Type && this.Expr.Equals(u.Expr);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash combining the operator and the operand.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Type, this.Expr);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The operator name and operand as text.</returns>
    public override string ToString()
    {
        return $"Unary: {this.Type}({this.Expr})";
    }
}
