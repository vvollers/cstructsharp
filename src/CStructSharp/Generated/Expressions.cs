namespace CStructSharp.Generated;

using System;
using System.Globalization;
using CStructSharp.Expressions;

/// <summary>
///     The layout expression operators as generated code evaluates them: the signed-Int32 semantics of the runtime's
///     <c>ExpressionEvaluator</c> (checked <c>+ - *</c> and negation, unmasked shift counts, comparisons and logic
///     that yield 0 or 1), in one place shared with the runtime.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can call it.
/// </remarks>
public static class Expressions
{
    /// <summary>Checked addition.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Add(int left, int right) => ExpressionArithmetic.Add(left, right);

    /// <summary>Checked subtraction.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Subtract(int left, int right) => ExpressionArithmetic.Subtract(left, right);

    /// <summary>Checked multiplication.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Multiply(int left, int right) => ExpressionArithmetic.Multiply(left, right);

    /// <summary>Integer division; a zero divisor raises <see cref="DivideByZeroException"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Divide(int left, int right) => ExpressionArithmetic.Divide(left, right);

    /// <summary>Integer remainder; a zero divisor raises <see cref="DivideByZeroException"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Modulo(int left, int right) => ExpressionArithmetic.Modulo(left, right);

    /// <summary>Checked negation.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The operator result.</returns>
    public static int Negate(int value) => ExpressionArithmetic.Negate(value);

    /// <summary>Bitwise complement.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The operator result.</returns>
    public static int Complement(int value) => ExpressionArithmetic.Complement(value);

    /// <summary>1 when the value is 0; otherwise 0.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The operator result.</returns>
    public static int LogicalNot(int value) => ExpressionArithmetic.LogicalNot(value);

    /// <summary>Bitwise and.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int And(int left, int right) => ExpressionArithmetic.And(left, right);

    /// <summary>Bitwise or.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Or(int left, int right) => ExpressionArithmetic.Or(left, right);

    /// <summary>Bitwise exclusive or.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Xor(int left, int right) => ExpressionArithmetic.Xor(left, right);

    /// <summary>1 when both operands are non-zero; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int LogicalAnd(int left, int right) => ExpressionArithmetic.LogicalAnd(left, right);

    /// <summary>1 when either operand is non-zero; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int LogicalOr(int left, int right) => ExpressionArithmetic.LogicalOr(left, right);

    /// <summary>1 when equal; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Equal(int left, int right) => ExpressionArithmetic.Equal(left, right);

    /// <summary>1 when different; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int NotEqual(int left, int right) => ExpressionArithmetic.NotEqual(left, right);

    /// <summary>1 when <paramref name="left"/> is less; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Less(int left, int right) => ExpressionArithmetic.Less(left, right);

    /// <summary>1 when <paramref name="left"/> is less or equal; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int LessOrEqual(int left, int right) => ExpressionArithmetic.LessOrEqual(left, right);

    /// <summary>1 when <paramref name="left"/> is greater; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int Greater(int left, int right) => ExpressionArithmetic.Greater(left, right);

    /// <summary>1 when <paramref name="left"/> is greater or equal; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The operator result.</returns>
    public static int GreaterOrEqual(int left, int right) => ExpressionArithmetic.GreaterOrEqual(left, right);

    /// <summary>Left shift by a bit index (0-31, never masked); the result must stay in the signed 32-bit range.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The operator result.</returns>
    public static int ShiftLeft(int value, int count) => ExpressionArithmetic.ShiftLeft(value, count);

    /// <summary>Arithmetic right shift by a bit index (0-31, never masked).</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The operator result.</returns>
    public static int ShiftRight(int value, int count) => ExpressionArithmetic.ShiftRight(value, count);

    /// <summary>
    ///     The value of a captured member as an expression operand. Layout expressions are 32-bit: a member that holds
    ///     a wider value (a <c>uint32</c> at or above 2^31, a <c>uint64</c>, ...) is kept exact until an expression
    ///     selects it, and then fails exactly as the runtime's evaluator does - with an
    ///     <see cref="InvalidOperationException"/> that <c>ReadCursor.FailExpression</c> turns into the operation's
    ///     <c>Cannot evaluate {context}: ...</c> failure.
    /// </summary>
    /// <param name="value">The captured member value.</param>
    /// <param name="name">The member name, for the message.</param>
    /// <returns>The value as an <see cref="int"/>.</returns>
    /// <exception cref="InvalidOperationException">The value is outside the signed 32-bit range.</exception>
    public static int RequireInt32(long value, string name)
    {
        if (value is < int.MinValue or > int.MaxValue)
        {
            throw OutOfRange(value, name);
        }

        return (int)value;
    }

    /// <summary>The unsigned overload of <see cref="RequireInt32(long, string)"/>.</summary>
    /// <param name="value">The captured member value.</param>
    /// <param name="name">The member name, for the message.</param>
    /// <returns>The value as an <see cref="int"/>.</returns>
    /// <exception cref="InvalidOperationException">The value is outside the signed 32-bit range.</exception>
    public static int RequireInt32(ulong value, string name)
    {
        if (value > int.MaxValue)
        {
            throw OutOfRange(value, name);
        }

        return (int)value;
    }

    /// <summary>
    ///     A caller-supplied variable (<c>ReadOptions</c>'s <c>variables</c> argument) as an expression operand,
    ///     failing as the runtime evaluator does when the name was not supplied.
    /// </summary>
    /// <param name="variables">The caller's variables, or <see langword="null"/>.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">The variable was not supplied.</exception>
    public static int Variable(System.Collections.Generic.IReadOnlyDictionary<string, int>? variables, string name)
    {
        if (variables is not null && variables.TryGetValue(name, out int value))
        {
            return value;
        }

        throw new System.Collections.Generic.KeyNotFoundException("Undefined expression identifier: " + name);
    }

    private static InvalidOperationException OutOfRange(IFormattable value, string name)
        => new(WideValueVariable.DescribeOutOfRange(name, value));
}
