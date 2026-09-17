namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;

/// <summary>C's <c>condition ? whenTrue : whenFalse</c>; only the selected arm is evaluated.</summary>
internal class ConditionalExpr : Expr
{
    public ConditionalExpr(Expr condition, Expr whenTrue, Expr whenFalse)
    {
        this.Condition = condition;
        this.WhenTrue = whenTrue;
        this.WhenFalse = whenFalse;
    }

    public Expr Condition { get; }

    public Expr WhenTrue { get; }

    public Expr WhenFalse { get; }

    public override int Value
    {
        get => this.Calc();
    }

    public override int Calc(Dictionary<string, Expr> variables)
    {
        return global::CStructSharp.Expressions.ExpressionEvaluator.Default.Evaluate(this, variables);
    }

    public override bool Equals(Expr? other)
    {
        return other is ConditionalExpr c && this.Condition.Equals(c.Condition) && this.WhenTrue.Equals(c.WhenTrue) && this.WhenFalse.Equals(c.WhenFalse);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Condition, this.WhenTrue, this.WhenFalse);
    }

    public override string ToString()
    {
        return $"Conditional: {this.Value}";
    }
}
