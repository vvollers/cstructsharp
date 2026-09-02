namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;

/// <summary>Represents an expression that combines a left and right value with an arithmetic or bitwise operator.</summary>
internal class BinaryOp : Expr
{
    /// <summary>Creates a binary expression from its operator and two input expressions.</summary>
    public BinaryOp(BinaryOperatorType type, Expr left, Expr right)
    {
        this.Type = type;
        this.Left = left;
        this.Right = right;
    }

    public Expr Left { get; }

    public Expr Right { get; }

    public BinaryOperatorType Type { get; }

    public override int Value
    {
        get => this.Calc();
    }

    /// <summary>Calculates both inputs and applies this expression's operator.</summary>
    public override int Calc(Dictionary<string, Expr> variables)
    {
        return global::CStructSharp.ExpressionEvaluator.Default.Evaluate(this, variables);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is BinaryOp b && this.Type == b.Type && this.Left.Equals(b.Left) && this.Right.Equals(b.Right);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Type, this.Left, this.Right);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"BinaryOp: {this.Value}";
    }
}
