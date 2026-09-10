namespace CStructSharp.Structure;

using System.Collections.Generic;
using System.Numerics;

/// <summary>Represents a number written directly in a layout expression.</summary>
internal class Literal : Expr
{
    private readonly BigInteger int32Projection;

    /// <summary>Creates a fixed numeric expression.</summary>
    public Literal(int value)
        : this(new BigInteger(value))
    {
    }

    /// <summary>Creates an exact integer literal; layout-expression evaluation remains checked to Int32.</summary>
    public Literal(BigInteger value)
        : this(value, value)
    {
    }

    /// <summary>Creates a parsed literal with separate exact and traditional Int32 expression interpretations.</summary>
    internal Literal(BigInteger exactValue, BigInteger int32Projection)
    {
        this.ExactValue = exactValue;
        this.int32Projection = int32Projection;
    }

    /// <summary>Gets the exact mathematical integer represented by this literal.</summary>
    public BigInteger ExactValue { get; }

    public override int Value => checked((int)this.int32Projection);

    /// <summary>Gets the value consumed by ordinary checked Int32 layout expressions.</summary>
    internal BigInteger Int32Projection => this.int32Projection;

    /// <summary>Calculates the value represented by this expression.</summary>
    public override int Calc(Dictionary<string, Expr> variables)
    {
        return global::CStructSharp.ExpressionEvaluator.Default.Evaluate(this, variables);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is Literal literal && this.ExactValue == literal.ExactValue;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return this.ExactValue.GetHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"Literal: {this.ExactValue}";
    }
}
