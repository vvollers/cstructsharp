namespace CStructSharp.Expressions;

using System;

/// <summary>
///     The signed-Int32 operator semantics every layout expression is evaluated with: checked <c>+ - *</c> and
///     unary <c>-</c>, C# division and modulo (a zero divisor raises <see cref="DivideByZeroException"/>), shifts
///     whose count must be a bit index (0-31, never masked) and whose left shift may not leave the 32-bit range,
///     and comparisons and logical operators that produce 0 or 1. The runtime evaluator and the generated code both
///     call these, so the rules live in one place.
/// </summary>
internal static class ExpressionArithmetic
{
    /// <summary>Adds two values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The sum leaves the signed 32-bit range.</exception>
    public static int Add(int left, int right) => checked(left + right);

    /// <summary>Subtracts the right operand from the left.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The difference leaves the signed 32-bit range.</exception>
    public static int Subtract(int left, int right) => checked(left - right);

    /// <summary>Multiplies two values.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product.</returns>
    /// <exception cref="OverflowException">The product leaves the signed 32-bit range.</exception>
    public static int Multiply(int left, int right) => checked(left * right);

    /// <summary>Divides with C# semantics, truncating toward zero.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    public static int Divide(int left, int right) => left / right;

    /// <summary>Computes the remainder with C# semantics; its sign follows the left operand.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The remainder.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    public static int Modulo(int left, int right) => checked(left % right);

    /// <summary>Negates a value.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated value.</returns>
    /// <exception cref="OverflowException"><paramref name="value"/> is <see cref="int.MinValue"/>.</exception>
    public static int Negate(int value) => checked(-value);

    /// <summary>Computes the bitwise complement (<c>~</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value with every bit inverted.</returns>
    public static int Complement(int value) => ~value;

    /// <summary>Computes C's <c>!</c>, treating any nonzero operand as true.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>1 when the operand is 0; otherwise 0.</returns>
    public static int LogicalNot(int value) => value == 0 ? 1 : 0;

    /// <summary>Computes the bitwise AND.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in both operands.</returns>
    public static int And(int left, int right) => left & right;

    /// <summary>Computes the bitwise OR.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in either operand.</returns>
    public static int Or(int left, int right) => left | right;

    /// <summary>Computes the bitwise exclusive OR.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in exactly one operand.</returns>
    public static int Xor(int left, int right) => left ^ right;

    /// <summary>Computes C's <c>&amp;&amp;</c>, treating any nonzero operand as true.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when both operands are nonzero; otherwise 0.</returns>
    public static int LogicalAnd(int left, int right) => left != 0 && right != 0 ? 1 : 0;

    /// <summary>Computes C's <c>||</c>, treating any nonzero operand as true.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when either operand is nonzero; otherwise 0.</returns>
    public static int LogicalOr(int left, int right) => left != 0 || right != 0 ? 1 : 0;

    /// <summary>Compares for equality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the operands are equal; otherwise 0.</returns>
    public static int Equal(int left, int right) => left == right ? 1 : 0;

    /// <summary>Compares for inequality.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the operands differ; otherwise 0.</returns>
    public static int NotEqual(int left, int right) => left != right ? 1 : 0;

    /// <summary>Tests whether the left operand is less than the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static int Less(int left, int right) => left < right ? 1 : 0;

    /// <summary>Tests whether the left operand is less than or equal to the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static int LessOrEqual(int left, int right) => left <= right ? 1 : 0;

    /// <summary>Tests whether the left operand is greater than the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static int Greater(int left, int right) => left > right ? 1 : 0;

    /// <summary>Tests whether the left operand is greater than or equal to the right.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>1 when the comparison holds; otherwise 0.</returns>
    public static int GreaterOrEqual(int left, int right) => left >= right ? 1 : 0;

    /// <summary>Rejects C#'s masked shift counts and any signed-Int32 left-shift overflow.</summary>
    /// <param name="value">The value to shift.</param>
    /// <param name="count">The number of bit positions to shift by, 0 through 31.</param>
    /// <returns>The shifted value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> is outside 0 through 31.</exception>
    /// <exception cref="OverflowException">The result leaves the signed 32-bit range.</exception>
    public static int ShiftLeft(int value, int count)
    {
        ValidateShiftCount(count);
        long result = (long)value << count;
        if (result is < int.MinValue or > int.MaxValue)
        {
            throw new OverflowException("Expression left shift exceeded the signed 32-bit range.");
        }

        return (int)result;
    }

    /// <summary>Performs an arithmetic signed right shift after validating the unmasked count.</summary>
    /// <param name="value">The value to shift; its sign bit is copied into the vacated bits.</param>
    /// <param name="count">The number of bit positions to shift by, 0 through 31.</param>
    /// <returns>The shifted value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> is outside 0 through 31.</exception>
    public static int ShiftRight(int value, int count)
    {
        ValidateShiftCount(count);
        return value >> count;
    }

    /// <summary>Defines valid shift counts as the complete signed-Int32 bit-index domain.</summary>
    private static void ValidateShiftCount(int count)
    {
        if (count is < 0 or >= sizeof(int) * 8)
        {
            throw new InvalidOperationException("Expression shift count must be between 0 and 31.");
        }
    }
}
