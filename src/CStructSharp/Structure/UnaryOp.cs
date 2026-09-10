namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;

/// <summary>Represents an expression that changes one value with negation or bitwise complement.</summary>
internal class UnaryOp : Expr
{
    /// <summary>Creates a unary expression from its operator and input expression.</summary>
    public UnaryOp(UnaryOperatorType type, Expr expr)
    {
        this.Type = type;
        this.Expr = expr;
    }

    public Expr Expr { get; }

    public UnaryOperatorType Type { get; }

    public override int Value
    {
        get => this.Calc();
    }

    /// <summary>Calculates the value represented by this expression.</summary>
    public override int Calc(Dictionary<string, Expr> variables)
    {
        return global::CStructSharp.ExpressionEvaluator.Default.Evaluate(this, variables);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is UnaryOp u && this.Type == u.Type && this.Expr.Equals(u.Expr);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Type, this.Expr);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"Unary: {this.Value}";
    }
}
