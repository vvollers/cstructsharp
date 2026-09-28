namespace CStructSharp.Tests.Generated;

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
        Int128 big = (Int128)ulong.MaxValue;
        foreach ((string text, Func<Int128> generated) in new (string, Func<Int128>)[]
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
                     ("0xFFFFFFFFFFFFFFFF * 2", () => Expressions.Multiply(big, 2)),
                     ("0xFFFFFFFFFFFFFFFF + 1", () => Expressions.Add(big, 1)),
                     ("-1 << 127", () => Expressions.ShiftLeft(Expressions.Negate(1), 127)),
                     ("(1 << 126) >> 126", () => Expressions.ShiftRight(Expressions.ShiftLeft(1, 126), 126)),
                     ("0xFFFFFFFFFFFFFFFF != 0", () => Expressions.NotEqual(big, 0)),
                 })
        {
            Int128 expected = evaluator.Evaluate(LayoutParser.ParseExpression(text));
            Assert.AreEqual(expected, generated(), text);
        }
    }

    /// <summary>Division by zero, overflow, and undefined identifiers fail through <c>Expressions</c> with the runtime evaluator's exceptions.</summary>
    [TestMethod]
    public void Failures_AreTheRuntimeEvaluatorFailures()
    {
        Assert.Throws<OverflowException>(() => Expressions.Add(Int128.MaxValue, 1));
        Assert.Throws<OverflowException>(() => Expressions.Subtract(Int128.MinValue, 1));
        Assert.Throws<OverflowException>(() => Expressions.Multiply((Int128)1 << 64, (Int128)1 << 63));
        Assert.Throws<OverflowException>(() => Expressions.Negate(Int128.MinValue));
        Assert.Throws<OverflowException>(() => Expressions.Divide(Int128.MinValue, -1));
        Assert.Throws<OverflowException>(() => Expressions.Modulo(Int128.MinValue, -1));
        Assert.Throws<DivideByZeroException>(() => Expressions.Divide(1, 0));
        Assert.Throws<DivideByZeroException>(() => Expressions.Modulo(1, 0));
        Assert.Throws<OverflowException>(() => Expressions.ShiftLeft(1, 127));
        InvalidOperationException masked = Assert.Throws<InvalidOperationException>(() => Expressions.ShiftLeft(1, 128));
        Assert.AreEqual("Expression shift count must be between 0 and 127.", masked.Message);
        Assert.Throws<InvalidOperationException>(() => Expressions.ShiftRight(1, -1));
        Assert.AreEqual(
            "'WIDE' is 340282366920938463463374607431768211455, which is outside the 128-bit range that layout expressions support.",
            Assert.Throws<InvalidOperationException>(() => Expressions.OutOfRangeConstant("WIDE", "340282366920938463463374607431768211455")).Message);
        Assert.AreEqual(
            "The literal 340282366920938463463374607431768211455 is outside the 128-bit range that layout expressions support.",
            Assert.Throws<InvalidOperationException>(() => Expressions.OutOfRangeLiteral("340282366920938463463374607431768211455")).Message);
        Assert.AreEqual("Undefined expression identifier: n", Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => Expressions.Undefined("n")).Message);

        var variables = new System.Collections.Generic.Dictionary<string, int> { ["N"] = 7, };
        Assert.IsTrue(Expressions.TryVariable(variables, "N", out int supplied));
        Assert.AreEqual(7, supplied);
        Assert.IsFalse(Expressions.TryVariable(variables, "M", out int missing));
        Assert.AreEqual(0, missing);
        Assert.IsFalse(Expressions.TryVariable(null, "N", out _));

        // The runtime evaluator fails the same way for the same results.
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000));
        Assert.Throws<OverflowException>(() => evaluator.Evaluate(LayoutParser.ParseExpression("170141183460469231731687303715884105727 + 1")));
        Assert.Throws<OverflowException>(() => evaluator.Evaluate(LayoutParser.ParseExpression("1 << 127")));
        Assert.AreEqual(
            masked.Message,
            Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate(LayoutParser.ParseExpression("1 << 128"))).Message);
        Assert.AreEqual(
            "The literal 340282366920938463463374607431768211455 is outside the 128-bit range that layout expressions support.",
            Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate(LayoutParser.ParseExpression("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF + 0"))).Message);
    }

    /// <summary><c>FromUInt128</c> widens every signed-range value exactly and rejects a larger one with the runtime evaluator's message.</summary>
    [TestMethod]
    public void FromUInt128_FailsExactlyAsTheRuntimeEvaluatorDoes()
    {
        Assert.AreEqual((Int128)5, Expressions.FromUInt128(5, "count"));
        Assert.AreEqual(Int128.MaxValue, Expressions.FromUInt128((UInt128)Int128.MaxValue, "count"), "the range is inclusive");
        InvalidOperationException wide = Assert.Throws<InvalidOperationException>(() => Expressions.FromUInt128((UInt128)Int128.MaxValue + 1, "count"));
        Assert.AreEqual("'count' is 170141183460469231731687303715884105728, which is outside the 128-bit range that layout expressions support.", wide.Message);
        Assert.Throws<InvalidOperationException>(() => Expressions.FromUInt128(UInt128.MaxValue, "count"));

        // The runtime raises the same exception type and text when an expression selects a captured wide value;
        // ReadCursor.FailExpression turns it into the operation's read failure.
        var layout = new CStruct("struct root { uint128 count; uint8 items[count]; };");
        byte[] bytes = new byte[17];
        bytes[15] = 0x80;
        CStructReadException runtime = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "root"));
        Assert.IsInstanceOfType<InvalidOperationException>(runtime.InnerException);
        Assert.AreEqual(wide.Message, runtime.InnerException!.Message);
    }

    /// <summary>Generated variable lookups distinguish missing required names, supplied zero and constant fallbacks.</summary>
    [TestMethod]
    public void Variables_RespectRequiredNamesAndCallerOverrides()
    {
        var variables = new Dictionary<string, int> { ["zero"] = 0, ["count"] = 7, };
        Assert.AreEqual((Int128)7, Expressions.Variable(variables, "count"));
        Assert.AreEqual((Int128)0, Expressions.Variable(variables, "zero"));
        Assert.AreEqual((Int128)7, Expressions.Variable(variables, "count", 19));
        Assert.AreEqual((Int128)0, Expressions.Variable(variables, "zero", 19));
        Assert.AreEqual((Int128)19, Expressions.Variable(variables, "missing", 19));
        Assert.AreEqual((Int128)19, Expressions.Variable(null, "missing", 19));
        Assert.AreEqual((Int128)ulong.MaxValue, Expressions.Variable(null, "wide", ulong.MaxValue), "a constant keeps its exact 64-bit value");
        foreach (IReadOnlyDictionary<string, int>? input in new IReadOnlyDictionary<string, int>?[] { null, variables, })
        {
            // A required name has no constant fallback; preserve the identifier in its diagnostic.
            KeyNotFoundException failure = Assert.Throws<KeyNotFoundException>(() => Expressions.Variable(input, "missing"));
            Assert.AreEqual("Undefined expression identifier: missing", failure.Message);
        }
    }
}
