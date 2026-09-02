namespace CStructSharp.Structure;

using System.Collections.Generic;

/// <summary>Represents the absence of an optional expression, such as an omitted array length.</summary>
internal class NoneExpr : Expr
{
    private const int VALUE = 0;

    public static readonly NoneExpr Instance = new();

    public override int Value
    {
        get => VALUE;
    }

    /// <summary>Calculates the value represented by this expression.</summary>
    public override int Calc(Dictionary<string, Expr> variables)
    {
        return global::CStructSharp.ExpressionEvaluator.Default.Evaluate(this, variables);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is NoneExpr;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return VALUE.GetHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return "NoneExpr(0)";
    }
}
