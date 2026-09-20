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
    public static int Add(int left, int right) => checked(left + right);

    public static int Subtract(int left, int right) => checked(left - right);

    public static int Multiply(int left, int right) => checked(left * right);

    public static int Divide(int left, int right) => left / right;

    public static int Modulo(int left, int right) => checked(left % right);

    public static int Negate(int value) => checked(-value);

    public static int Complement(int value) => ~value;

    public static int LogicalNot(int value) => value == 0 ? 1 : 0;

    public static int And(int left, int right) => left & right;

    public static int Or(int left, int right) => left | right;

    public static int Xor(int left, int right) => left ^ right;

    public static int LogicalAnd(int left, int right) => left != 0 && right != 0 ? 1 : 0;

    public static int LogicalOr(int left, int right) => left != 0 || right != 0 ? 1 : 0;

    public static int Equal(int left, int right) => left == right ? 1 : 0;

    public static int NotEqual(int left, int right) => left != right ? 1 : 0;

    public static int Less(int left, int right) => left < right ? 1 : 0;

    public static int LessOrEqual(int left, int right) => left <= right ? 1 : 0;

    public static int Greater(int left, int right) => left > right ? 1 : 0;

    public static int GreaterOrEqual(int left, int right) => left >= right ? 1 : 0;

    /// <summary>Rejects C#'s masked shift counts and any signed-Int32 left-shift overflow.</summary>
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
