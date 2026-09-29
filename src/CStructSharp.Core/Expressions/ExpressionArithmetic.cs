namespace CStructSharp.Expressions;

using System;

/// <summary>
///     The operator semantics every layout expression is evaluated with, in the domain of signed 128-bit integers
///     (<see cref="Int128"/>): checked <c>+ - *</c> and unary <c>-</c>, division and modulo that truncate toward zero
///     (a zero divisor raises <see cref="DivideByZeroException"/>), shifts whose count must be a bit index (0-127,
///     never masked) and whose left shift may not leave the 128-bit range, and comparisons and logical operators that
///     produce 0 or 1. The runtime evaluator and the generated code both call these, so the rules live in one place.
/// </summary>
internal static class ExpressionArithmetic
{
    /// <summary>The number of bits in the expression domain; a shift count must be below it.</summary>
    public const int DomainBits = 128;

    /// <summary>The text a shift count outside 0 through 127 fails with.</summary>
    public const string ShiftCountMessage = "Expression shift count must be between 0 and 127.";

    /// <summary>Adds two values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The sum leaves the signed 128-bit range.</exception>
    public static Int128 Add(Int128 left, Int128 right) => checked(left + right);

    /// <summary>Subtracts the right operand from the left.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The difference leaves the signed 128-bit range.</exception>
    public static Int128 Subtract(Int128 left, Int128 right) => checked(left - right);

    /// <summary>Multiplies two values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product.</returns>
    /// <exception cref="OverflowException">The product leaves the signed 128-bit range.</exception>
    public static Int128 Multiply(Int128 left, Int128 right) => checked(left * right);

    /// <summary>Divides, truncating toward zero.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    /// <exception cref="OverflowException">
    ///     <paramref name="left"/> is <see cref="Int128.MinValue"/> and <paramref name="right"/> is -1, whose quotient
    ///     2^127 is outside the range.
    /// </exception>
    public static Int128 Divide(Int128 left, Int128 right)
    {
        RejectUnrepresentableQuotient(left, right);
        return left / right;
    }

    /// <summary>Computes the remainder of a division that truncates toward zero; its sign follows the dividend.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The remainder.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    /// <exception cref="OverflowException">
    ///     <paramref name="left"/> is <see cref="Int128.MinValue"/> and <paramref name="right"/> is -1: the remainder
    ///     belongs to a quotient outside the range, so it fails as the division does.
    /// </exception>
    public static Int128 Modulo(Int128 left, Int128 right)
    {
        RejectUnrepresentableQuotient(left, right);
        return left % right;
    }

    /// <summary>Negates a value.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated value.</returns>
    /// <exception cref="OverflowException"><paramref name="value"/> is <see cref="Int128.MinValue"/>.</exception>
    public static Int128 Negate(Int128 value) => checked(-value);

    /// <summary>Computes the bitwise complement (<c>~</c>) of the two's-complement value.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value with every bit inverted, which is <c>-value - 1</c>.</returns>
    public static Int128 Complement(Int128 value) => ~value;

    /// <summary>Computes C's <c>!</c>, treating any nonzero operand as true.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>1 when the operand is 0; otherwise 0.</returns>
    public static Int128 LogicalNot(Int128 value) => value == Int128.Zero ? Int128.One : Int128.Zero;

    /// <summary>Computes the bitwise AND.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in both operands.</returns>
    public static Int128 And(Int128 left, Int128 right) => left & right;

    /// <summary>Computes the bitwise OR.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in either operand.</returns>
    public static Int128 Or(Int128 left, Int128 right) => left | right;

    /// <summary>Computes the bitwise exclusive OR.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in exactly one operand.</returns>
    public static Int128 Xor(Int128 left, Int128 right) => left ^ right;

    /// <summary>Computes C's <c>&amp;&amp;</c>, treating any nonzero operand as true.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when both operands are nonzero; otherwise 0.</returns>
    public static Int128 LogicalAnd(Int128 left, Int128 right) => Truth(left != Int128.Zero && right != Int128.Zero);

    /// <summary>Computes C's <c>||</c>, treating any nonzero operand as true.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when either operand is nonzero; otherwise 0.</returns>
    public static Int128 LogicalOr(Int128 left, Int128 right) => Truth(left != Int128.Zero || right != Int128.Zero);

    /// <summary>Compares for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the operands are equal; otherwise 0.</returns>
    public static Int128 Equal(Int128 left, Int128 right) => Truth(left == right);

    /// <summary>Compares for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the operands differ; otherwise 0.</returns>
    public static Int128 NotEqual(Int128 left, Int128 right) => Truth(left != right);

    /// <summary>Tests whether the left operand is less than the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static Int128 Less(Int128 left, Int128 right) => Truth(left < right);

    /// <summary>Tests whether the left operand is less than or equal to the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static Int128 LessOrEqual(Int128 left, Int128 right) => Truth(left <= right);

    /// <summary>Tests whether the left operand is greater than the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static Int128 Greater(Int128 left, Int128 right) => Truth(left > right);

    /// <summary>Tests whether the left operand is greater than or equal to the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static Int128 GreaterOrEqual(Int128 left, Int128 right) => Truth(left >= right);

    /// <summary>Shifts left after rejecting a masked shift count and any result outside the signed 128-bit range.</summary>
    /// <param name="value">The value to shift.</param>
    /// <param name="count">The number of bit positions to shift by, 0 through 127.</param>
    /// <returns>The shifted value, which equals <c>value * 2^count</c>.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> is outside 0 through 127.</exception>
    /// <exception cref="OverflowException">The result leaves the signed 128-bit range.</exception>
    public static Int128 ShiftLeft(Int128 value, Int128 count)
    {
        int bits = ValidateShiftCount(count);
        Int128 result = value << bits;

        // The shift kept every significant bit (the sign included) exactly when shifting back restores the value.
        if (result >> bits != value)
        {
            throw new OverflowException("Expression left shift exceeded the signed 128-bit range.");
        }

        return result;
    }

    /// <summary>Performs an arithmetic signed right shift after validating the unmasked count.</summary>
    /// <param name="value">The value to shift; its sign bit is copied into the vacated bits.</param>
    /// <param name="count">The number of bit positions to shift by, 0 through 127.</param>
    /// <returns>The shifted value, which is <c>value / 2^count</c> rounded toward negative infinity.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> is outside 0 through 127.</exception>
    public static Int128 ShiftRight(Int128 value, Int128 count) => value >> ValidateShiftCount(count);

    /// <summary>The domain's truth value: 1 or 0.</summary>
    private static Int128 Truth(bool condition) => condition ? Int128.One : Int128.Zero;

    /// <summary>Defines valid shift counts as the complete 128-bit bit-index domain.</summary>
    private static int ValidateShiftCount(Int128 count)
    {
        if (count < Int128.Zero || count >= DomainBits)
        {
            throw new InvalidOperationException(ShiftCountMessage);
        }

        return (int)count;
    }

    /// <summary>
    ///     Rejects a zero divisor with <see cref="DivideByZeroException"/>, and the one quotient that has no
    ///     representation, 2^127 (<see cref="Int128.MinValue"/> / -1).
    /// </summary>
    /// <remarks>
    ///     The zero check is explicit because the .NET 8 runtime's 128-bit division throws an index or argument exception
    ///     instead of <see cref="DivideByZeroException"/> for a dividend of 64 bits or more.
    /// </remarks>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    /// <exception cref="OverflowException"><paramref name="left"/> is <see cref="Int128.MinValue"/> and <paramref name="right"/> is -1.</exception>
    private static void RejectUnrepresentableQuotient(Int128 left, Int128 right)
    {
        if (right == Int128.Zero)
        {
            throw new DivideByZeroException();
        }

        if (right == Int128.NegativeOne && left == Int128.MinValue)
        {
            throw new OverflowException();
        }
    }
}
