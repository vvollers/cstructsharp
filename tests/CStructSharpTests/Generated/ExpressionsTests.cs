namespace CStructSharpTests.Generated;

using System;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;
using CStructSharp.Parsing;

/// <summary>
///     Pins <see cref="Expressions"/> to the runtime evaluator: every operator produces the runtime's value, or
///     fails where the runtime fails, because both call the same <c>ExpressionArithmetic</c>.
/// </summary>
[TestClass]
public class ExpressionsTests
{
    /// <summary>Every <c>Expressions</c> operator returns what the runtime expression evaluator returns for the same operands.</summary>
    [TestMethod]
    public void Operators_MatchTheRuntimeEvaluator()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000));
        foreach ((string text, Func<int> generated) in new (string, Func<int>)[]
                 {
                     ("3 + 4", () => Expressions.Add(3, 4)),
                     ("3 - 4", () => Expressions.Subtract(3, 4)),
                     ("-7 * 6", () => Expressions.Multiply(Expressions.Negate(7), 6)),
                     ("-7 / 2", () => Expressions.Divide(Expressions.Negate(7), 2)),
                     ("-7 % 2", () => Expressions.Modulo(Expressions.Negate(7), 2)),
                     ("~5", () => Expressions.Complement(5)),
                     ("!0", () => Expressions.LogicalNot(0)),
                     ("!7", () => Expressions.LogicalNot(7)),
                     ("6 & 3", () => Expressions.And(6, 3)),
                     ("6 | 3", () => Expressions.Or(6, 3)),
                     ("6 ^ 3", () => Expressions.Xor(6, 3)),
                     ("2 && 0", () => Expressions.LogicalAnd(2, 0)),
                     ("2 || 0", () => Expressions.LogicalOr(2, 0)),
                     ("2 == 2", () => Expressions.Equal(2, 2)),
                     ("2 != 2", () => Expressions.NotEqual(2, 2)),
                     ("1 < 2", () => Expressions.Less(1, 2)),
                     ("2 <= 2", () => Expressions.LessOrEqual(2, 2)),
                     ("3 > 2", () => Expressions.Greater(3, 2)),
                     ("1 >= 2", () => Expressions.GreaterOrEqual(1, 2)),
                     ("1 << 4", () => Expressions.ShiftLeft(1, 4)),
                     ("-16 >> 2", () => Expressions.ShiftRight(Expressions.Negate(16), 2)),
                 })
        {
            int expected = evaluator.Evaluate(LayoutParser.ParseExpression(text));
            Assert.AreEqual(expected, generated(), text);
        }
    }

    /// <summary>Division by zero, overflow, and undefined identifiers fail through <c>Expressions</c> with the runtime evaluator's exceptions.</summary>
    [TestMethod]
    public void Failures_AreTheRuntimeEvaluatorFailures()
    {
        Assert.Throws<OverflowException>(() => Expressions.Add(int.MaxValue, 1));
        Assert.Throws<OverflowException>(() => Expressions.Subtract(int.MinValue, 1));
        Assert.Throws<OverflowException>(() => Expressions.Multiply(65536, 65536));
        Assert.Throws<OverflowException>(() => Expressions.Negate(int.MinValue));
        Assert.Throws<OverflowException>(() => Expressions.Modulo(int.MinValue, -1));
        Assert.Throws<DivideByZeroException>(() => Expressions.Divide(1, 0));
        Assert.Throws<DivideByZeroException>(() => Expressions.Modulo(1, 0));
        Assert.Throws<OverflowException>(() => Expressions.ShiftLeft(1, 31));
        InvalidOperationException masked = Assert.Throws<InvalidOperationException>(() => Expressions.ShiftLeft(1, 32));
        Assert.AreEqual("Expression shift count must be between 0 and 31.", masked.Message);
        Assert.Throws<InvalidOperationException>(() => Expressions.ShiftRight(1, -1));

        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000));
        Assert.Throws<OverflowException>(() => evaluator.Evaluate(LayoutParser.ParseExpression("2147483647 + 1")));
        Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate(LayoutParser.ParseExpression("1 << 32")));
    }

    /// <summary><c>RequireInt32</c> rejects a wide captured value with the runtime evaluator's message.</summary>
    [TestMethod]
    public void RequireInt32_FailsExactlyAsTheRuntimeEvaluatorDoes()
    {
        Assert.AreEqual(5, Expressions.RequireInt32(5L, "count"));
        Assert.AreEqual(int.MaxValue, Expressions.RequireInt32((ulong)int.MaxValue, "count"));
        InvalidOperationException wide = Assert.Throws<InvalidOperationException>(() => Expressions.RequireInt32(2147483648L, "count"));
        Assert.AreEqual("'count' is 2147483648, which is outside the 32-bit range that layout expressions support.", wide.Message);
        Assert.Throws<InvalidOperationException>(() => Expressions.RequireInt32(ulong.MaxValue, "count"));
        Assert.Throws<InvalidOperationException>(() => Expressions.RequireInt32(-2147483649L, "count"));

        // The runtime raises the same exception type and text when an expression selects a captured wide value;
        // ReadCursor.FailExpression turns it into the operation's read failure.
        var layout = new CStruct("struct root { uint32 count; uint8 items[count]; };");
        CStructReadException runtime = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 0, 0, 0, 0x80, 1 }, "root"));
        Assert.IsInstanceOfType<InvalidOperationException>(runtime.InnerException);
        Assert.AreEqual(wide.Message, runtime.InnerException!.Message);
    }
}
