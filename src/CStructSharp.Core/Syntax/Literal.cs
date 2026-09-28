namespace CStructSharp.Syntax;

using System;
using System.Globalization;
using System.Numerics;

/// <summary>
///     Represents a number written directly in a layout expression, or a value (a field's, a constant's) that an
///     operation stores as one. A literal is its exact mathematical value: <c>0xFFFFFFFF</c> is 4294967295. Values
///     inside the signed 128-bit expression domain are held as an <see cref="Int128"/> so evaluation reads them
///     without conversion; a larger value (an unsigned 128-bit enum member, say) keeps its exact value for enum and
///     constant evaluation and fails when an ordinary expression uses it.
/// </summary>
internal class Literal : Expr
{
    private static readonly BigInteger DomainMinimum = (BigInteger)Int128.MinValue;
    private static readonly BigInteger DomainMaximum = (BigInteger)Int128.MaxValue;

    private readonly Int128 value;
    private readonly BigInteger? outsideDomain;

    /// <summary>Creates a literal inside the expression domain.</summary>
    /// <param name="value">The value.</param>
    public Literal(Int128 value)
    {
        this.value = value;
    }

    /// <summary>Creates a literal from an exact integer of any size.</summary>
    /// <param name="value">
    ///     The exact integer; one outside the signed 128-bit range is kept exactly but cannot be used by an ordinary
    ///     expression.
    /// </param>
    public Literal(BigInteger value)
    {
        if (value >= DomainMinimum && value <= DomainMaximum)
        {
            this.value = (Int128)value;
        }
        else
        {
            this.outsideDomain = value;
        }
    }

    /// <summary>Gets the exact mathematical integer represented by this literal.</summary>
    public BigInteger ExactValue => this.outsideDomain ?? (BigInteger)this.value;

    /// <summary>Gets a value indicating whether the literal lies inside the signed 128-bit expression domain.</summary>
    public bool IsInDomain => !this.outsideDomain.HasValue;

    /// <summary>Gets the literal's value in the expression domain.</summary>
    /// <exception cref="InvalidOperationException">The literal lies outside the signed 128-bit range.</exception>
    public Int128 Value => this.outsideDomain.HasValue ? throw new InvalidOperationException(this.DescribeOutsideDomain()) : this.value;

    /// <summary>The text an expression fails with when it uses a literal outside the domain.</summary>
    /// <param name="value">The literal's exact value in invariant decimal digits.</param>
    /// <returns>A message naming the literal's value.</returns>
    public static string DescribeOutsideDomain(string value)
        => "The literal " + value + " is outside the 128-bit range that layout expressions support.";

    /// <summary>The text an expression fails with when it uses this literal although it is outside the domain.</summary>
    /// <returns>A message naming the literal's exact value.</returns>
    public string DescribeOutsideDomain() => DescribeOutsideDomain(this.ExactValue.ToString(CultureInfo.InvariantCulture));

    /// <summary>Gets the literal as an <see cref="int"/> when it fits one.</summary>
    /// <param name="result">The value on success; otherwise 0.</param>
    /// <returns><see langword="true"/> when the literal lies within the signed 32-bit range.</returns>
    public bool TryGetInt32(out int result)
    {
        if (!this.outsideDomain.HasValue && this.value >= int.MinValue && this.value <= int.MaxValue)
        {
            result = (int)this.value;
            return true;
        }

        result = 0;
        return false;
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="other"/> is a literal with the same exact value.</returns>
    public override bool Equals(Expr? other)
    {
        return other is Literal literal && this.outsideDomain == literal.outsideDomain && this.value == literal.value;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>The hash of the exact value.</returns>
    public override int GetHashCode()
    {
        return this.outsideDomain?.GetHashCode() ?? this.value.GetHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The text <c>Literal: </c> followed by the exact value.</returns>
    public override string ToString()
    {
        return $"Literal: {this.ExactValue}";
    }
}
