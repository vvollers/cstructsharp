namespace CStructSharp.Generated;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     The layout expression operators as generated code evaluates them: the signed 128-bit semantics of the runtime's
///     <c>ExpressionEvaluator</c> (checked <c>+ - *</c> and negation, unmasked shift counts from 0 to 127, comparisons
///     and logic that yield 0 or 1), in one place shared with the runtime. Every integer member up to 64 bits, a
///     pointer's address, a <c>bool</c> and a character widen into <see cref="Int128"/> exactly; an unsigned 128-bit
///     member goes through <see cref="FromUInt128"/>.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can call it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class Expressions
{
    /// <summary>Checked addition.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The sum is outside the signed 128-bit range.</exception>
    public static Int128 Add(Int128 left, Int128 right) => ExpressionArithmetic.Add(left, right);

    /// <summary>Checked subtraction.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The difference is outside the signed 128-bit range.</exception>
    public static Int128 Subtract(Int128 left, Int128 right) => ExpressionArithmetic.Subtract(left, right);

    /// <summary>Checked multiplication.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The product.</returns>
    /// <exception cref="OverflowException">The product is outside the signed 128-bit range.</exception>
    public static Int128 Multiply(Int128 left, Int128 right) => ExpressionArithmetic.Multiply(left, right);

    /// <summary>Integer division, truncating toward zero.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    /// <exception cref="OverflowException">The quotient of <see cref="Int128.MinValue"/> and -1 is outside the range.</exception>
    public static Int128 Divide(Int128 left, Int128 right) => ExpressionArithmetic.Divide(left, right);

    /// <summary>Integer remainder, whose sign follows the dividend.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The remainder.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is 0.</exception>
    /// <exception cref="OverflowException"><paramref name="left"/> is <see cref="Int128.MinValue"/> and <paramref name="right"/> is -1.</exception>
    public static Int128 Modulo(Int128 left, Int128 right) => ExpressionArithmetic.Modulo(left, right);

    /// <summary>Checked negation.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The negated value.</returns>
    /// <exception cref="OverflowException"><paramref name="value"/> is <see cref="Int128.MinValue"/>.</exception>
    public static Int128 Negate(Int128 value) => ExpressionArithmetic.Negate(value);

    /// <summary>Bitwise complement.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The value with every bit inverted.</returns>
    public static Int128 Complement(Int128 value) => ExpressionArithmetic.Complement(value);

    /// <summary>1 when the value is 0; otherwise 0.</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 LogicalNot(Int128 value) => ExpressionArithmetic.LogicalNot(value);

    /// <summary>Bitwise and.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in both operands.</returns>
    public static Int128 And(Int128 left, Int128 right) => ExpressionArithmetic.And(left, right);

    /// <summary>Bitwise or.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in either operand.</returns>
    public static Int128 Or(Int128 left, Int128 right) => ExpressionArithmetic.Or(left, right);

    /// <summary>Bitwise exclusive or.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The bits set in exactly one operand.</returns>
    public static Int128 Xor(Int128 left, Int128 right) => ExpressionArithmetic.Xor(left, right);

    /// <summary>1 when both operands are non-zero; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 LogicalAnd(Int128 left, Int128 right) => ExpressionArithmetic.LogicalAnd(left, right);

    /// <summary>1 when either operand is non-zero; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 LogicalOr(Int128 left, Int128 right) => ExpressionArithmetic.LogicalOr(left, right);

    /// <summary>1 when equal; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 Equal(Int128 left, Int128 right) => ExpressionArithmetic.Equal(left, right);

    /// <summary>1 when different; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 NotEqual(Int128 left, Int128 right) => ExpressionArithmetic.NotEqual(left, right);

    /// <summary>1 when <paramref name="left"/> is less; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 Less(Int128 left, Int128 right) => ExpressionArithmetic.Less(left, right);

    /// <summary>1 when <paramref name="left"/> is less or equal; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 LessOrEqual(Int128 left, Int128 right) => ExpressionArithmetic.LessOrEqual(left, right);

    /// <summary>1 when <paramref name="left"/> is greater; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 Greater(Int128 left, Int128 right) => ExpressionArithmetic.Greater(left, right);

    /// <summary>1 when <paramref name="left"/> is greater or equal; otherwise 0.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The truth value.</returns>
    public static Int128 GreaterOrEqual(Int128 left, Int128 right) => ExpressionArithmetic.GreaterOrEqual(left, right);

    /// <summary>Left shift by a bit index (0-127, never masked); the result must stay in the signed 128-bit range.</summary>
    /// <param name="value">The value to shift.</param>
    /// <param name="count">The number of bit positions.</param>
    /// <returns>The shifted value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> is outside 0 through 127.</exception>
    /// <exception cref="OverflowException">The result is outside the signed 128-bit range.</exception>
    public static Int128 ShiftLeft(Int128 value, Int128 count) => ExpressionArithmetic.ShiftLeft(value, count);

    /// <summary>Arithmetic right shift by a bit index (0-127, never masked).</summary>
    /// <param name="value">The value to shift; its sign is copied into the vacated bits.</param>
    /// <param name="count">The number of bit positions.</param>
    /// <returns>The shifted value.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> is outside 0 through 127.</exception>
    public static Int128 ShiftRight(Int128 value, Int128 count) => ExpressionArithmetic.ShiftRight(value, count);

    /// <summary>
    ///     A captured unsigned 128-bit member as an expression operand. Every value up to <see cref="Int128.MaxValue"/>
    ///     converts exactly; a larger one fails exactly as the runtime's evaluator does - with an
    ///     <see cref="InvalidOperationException"/> that <c>ReadCursor.FailExpression</c> turns into the operation's
    ///     <c>Cannot evaluate {context}: ...</c> failure.
    /// </summary>
    /// <param name="value">The captured member value.</param>
    /// <param name="name">The member name, for the message.</param>
    /// <returns>The value in the expression domain.</returns>
    /// <exception cref="InvalidOperationException">The value is above <see cref="Int128.MaxValue"/>.</exception>
    public static Int128 FromUInt128(UInt128 value, string name)
    {
        if (!ExpressionValueCapture.TryFromUInt128(value, out Int128 result))
        {
            throw new InvalidOperationException(WideValueVariable.DescribeOutOfRange(name, value));
        }

        return result;
    }

    /// <summary>
    ///     A member that is not an integer (text, an array, a struct, a floating-point value, ...) used as an expression
    ///     operand: the name is shared with an integer member, and this member's value is the one in effect, so the
    ///     expression fails as the runtime evaluator does.
    /// </summary>
    /// <param name="name">The member name.</param>
    /// <param name="reason">What the member holds, as the phrase after "is" (for example <c>text</c>).</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static Int128 NotAnInteger(string name, string reason) => throw new InvalidOperationException(NotANumberVariable.Describe(name, reason));

    /// <summary>
    ///     A caller-supplied variable (<c>ReadOptions</c>'s <c>variables</c> argument) as an expression operand,
    ///     failing as the runtime evaluator does when the name was not supplied.
    /// </summary>
    /// <param name="variables">The caller's variables, or <see langword="null"/>.</param>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, widened into the expression domain.</returns>
    /// <exception cref="KeyNotFoundException">The variable was not supplied.</exception>
    public static Int128 Variable(IReadOnlyDictionary<string, int>? variables, string name)
    {
        if (variables is not null && variables.TryGetValue(name, out int value))
        {
            return value;
        }

        throw new KeyNotFoundException("Undefined expression identifier: " + name);
    }

    /// <summary>
    ///     A layout constant (a define, an enum member) as an expression operand: the caller's variable of the same
    ///     name wins, as it does at runtime, where supplied variables override the layout's own definitions.
    /// </summary>
    /// <param name="variables">The caller's variables, or <see langword="null"/>.</param>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The layout's value of the constant.</param>
    /// <returns>The caller's value when supplied; otherwise <paramref name="value"/>.</returns>
    public static Int128 Variable(IReadOnlyDictionary<string, int>? variables, string name, Int128 value)
        => variables is not null && variables.TryGetValue(name, out int supplied) ? supplied : value;

    /// <summary>Looks a caller variable up, for a define whose own expression is evaluated only when the caller did not override it.</summary>
    /// <param name="variables">The caller's variables, or <see langword="null"/>.</param>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The caller's value when supplied.</param>
    /// <returns>Whether the caller supplied the variable.</returns>
    public static bool TryVariable(IReadOnlyDictionary<string, int>? variables, string name, out int value)
    {
        if (variables is not null && variables.TryGetValue(name, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>
    ///     The failure for a name that an expression selects before the composite read it: a conditional composite's
    ///     own members hide any outer or caller value of the same name from the start of the composite, and a member in
    ///     an arm that was not selected has no value.
    /// </summary>
    /// <param name="name">The member name.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="KeyNotFoundException">Always.</exception>
    public static Int128 Undefined(string name)
        => throw new KeyNotFoundException("Undefined expression identifier: " + name);

    /// <summary>
    ///     The failure for a layout constant outside the signed 128-bit range (an unsigned 128-bit enum member such as
    ///     <c>0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF</c>): the runtime keeps its exact value and fails, naming it, when an
    ///     expression selects it.
    /// </summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="value">The constant's exact value in invariant decimal digits.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static Int128 OutOfRangeConstant(string name, string value)
        => throw new InvalidOperationException(WideValueVariable.DescribeOutOfRange(name, value));

    /// <summary>The failure for a literal outside the signed 128-bit range, raised when an expression evaluates it.</summary>
    /// <param name="value">The literal's exact value in invariant decimal digits.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public static Int128 OutOfRangeLiteral(string value)
        => throw new InvalidOperationException(Literal.DescribeOutsideDomain(value));
}
