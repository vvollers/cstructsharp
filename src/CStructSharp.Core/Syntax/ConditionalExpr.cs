namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;

/// <summary>C's <c>condition ? whenTrue : whenFalse</c>; only the selected arm is evaluated.</summary>
internal class ConditionalExpr : Expr
{
    /// <summary>Creates a conditional expression from its three operands.</summary>
    public ConditionalExpr(Expr condition, Expr whenTrue, Expr whenFalse)
    {
        this.Condition = condition;
        this.WhenTrue = whenTrue;
        this.WhenFalse = whenFalse;
    }

    /// <summary>Gets the operand whose non-zero value selects <see cref="WhenTrue"/>.</summary>
    public Expr Condition { get; }

    /// <summary>Gets the arm evaluated when the condition is non-zero.</summary>
    public Expr WhenTrue { get; }

    /// <summary>Gets the arm evaluated when the condition is zero.</summary>
    public Expr WhenFalse { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is ConditionalExpr c && this.Condition.Equals(c.Condition) && this.WhenTrue.Equals(c.WhenTrue) && this.WhenFalse.Equals(c.WhenFalse);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Condition, this.WhenTrue, this.WhenFalse);
    }

    /// <summary>Returns a short readable description of the expression tree, without evaluating it.</summary>
    public override string ToString()
    {
        return $"Conditional: ({this.Condition} ? {this.WhenTrue} : {this.WhenFalse})";
    }
}
