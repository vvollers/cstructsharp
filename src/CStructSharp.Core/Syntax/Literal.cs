namespace CStructSharp.Syntax;

using System.Collections.Generic;
using System.Numerics;

/// <summary>Represents a number written directly in a layout expression.</summary>
internal class Literal : Expr
{
    private readonly BigInteger int32Projection;

    /// <summary>Creates a fixed numeric expression.</summary>
    /// <param name="value">The number; its exact value and its Int32 value are the same.</param>
    public Literal(int value)
        : this(new BigInteger(value))
    {
    }

    /// <summary>Creates an exact integer literal; layout-expression evaluation remains checked to Int32.</summary>
    /// <param name="value">The exact integer, used unchanged for both exact and Int32 evaluation.</param>
    public Literal(BigInteger value)
        : this(value, value)
    {
    }

    /// <summary>Creates a parsed literal with separate exact and traditional Int32 expression interpretations.</summary>
    /// <param name="exactValue">
    ///     The mathematical integer the literal spells, used by width-aware enum evaluation.
    /// </param>
    /// <param name="int32Projection">
    ///     The value ordinary Int32 expressions use: for a <c>0x</c>/<c>0b</c>/<c>0o</c> literal up to 32 bits, the
    ///     two's-complement reinterpretation of its bits (<c>0xFFFFFFFF</c> is -1); otherwise
    ///     <paramref name="exactValue"/>.
    /// </param>
    internal Literal(BigInteger exactValue, BigInteger int32Projection)
    {
        this.ExactValue = exactValue;
        this.int32Projection = int32Projection;
    }

    /// <summary>Gets the exact mathematical integer represented by this literal.</summary>
    public BigInteger ExactValue { get; }

    /// <summary>Gets the literal as an Int32, throwing <see cref="OverflowException"/> when it lies outside that range.</summary>
    public int Value => checked((int)this.int32Projection);

    /// <summary>Gets the value consumed by ordinary checked Int32 layout expressions.</summary>
    internal BigInteger Int32Projection => this.int32Projection;

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> is a literal with the same exact value.</returns>
    public override bool Equals(Expr? other)
    {
        return other is Literal literal && this.ExactValue == literal.ExactValue;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>The hash of the exact value.</returns>
    public override int GetHashCode()
    {
        return this.ExactValue.GetHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The text <c>Literal: </c> followed by the exact value.</returns>
    public override string ToString()
    {
        return $"Literal: {this.ExactValue}";
    }
}
