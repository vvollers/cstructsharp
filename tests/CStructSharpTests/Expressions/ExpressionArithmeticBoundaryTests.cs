namespace CStructSharp.Tests;

using CStructSharp.Expressions;

/// <summary>Checks that shared expression operators return integer truth values at equal, ordered and zero boundaries.</summary>
[TestClass]
public class ExpressionArithmeticBoundaryTests
{
    /// <summary>All six comparisons follow the signed ordering and return exactly zero or one.</summary>
    /// <param name="left">The first signed operand.</param>
    /// <param name="right">The second signed operand.</param>
    [TestMethod]
    [DataRow(-3, -3)]
    [DataRow(-3, 2)]
    [DataRow(2, -3)]
    [DataRow(0, 0)]
    [DataRow(int.MinValue, int.MaxValue)]
    [DataRow(int.MaxValue, int.MinValue)]
    public void Comparisons_PreserveSignedOrdering(int left, int right)
    {
        int ordering = left.CompareTo(right);
        Assert.AreEqual(ordering == 0 ? 1 : 0, ExpressionArithmetic.Equal(left, right));
        Assert.AreEqual(ordering != 0 ? 1 : 0, ExpressionArithmetic.NotEqual(left, right));
        Assert.AreEqual(ordering < 0 ? 1 : 0, ExpressionArithmetic.Less(left, right));
        Assert.AreEqual(ordering <= 0 ? 1 : 0, ExpressionArithmetic.LessOrEqual(left, right));
        Assert.AreEqual(ordering > 0 ? 1 : 0, ExpressionArithmetic.Greater(left, right));
        Assert.AreEqual(ordering >= 0 ? 1 : 0, ExpressionArithmetic.GreaterOrEqual(left, right));
    }

    /// <summary>Logical operators treat every nonzero integer as true without returning the original operand.</summary>
    /// <param name="left">The first integer truth value.</param>
    /// <param name="right">The second integer truth value.</param>
    /// <param name="andResult">Expected logical conjunction.</param>
    /// <param name="orResult">Expected logical disjunction.</param>
    [TestMethod]
    [DataRow(0, 0, 0, 0)]
    [DataRow(0, -3, 0, 1)]
    [DataRow(-3, 0, 0, 1)]
    [DataRow(-3, 2, 1, 1)]
    public void LogicalOperators_NormalizeTruthValues(int left, int right, int andResult, int orResult)
    {
        Assert.AreEqual(andResult, ExpressionArithmetic.LogicalAnd(left, right));
        Assert.AreEqual(orResult, ExpressionArithmetic.LogicalOr(left, right));
    }

    /// <summary>Left-shift overflow explains the signed-width constraint instead of exposing a blank arithmetic failure.</summary>
    [TestMethod]
    public void LeftShiftOverflow_ExplainsItsRange()
    {
        // A positive high bit cannot be represented as a signed Int128 result; the same bit is fine for -1.
        OverflowException failure = Assert.Throws<OverflowException>(() => ExpressionArithmetic.ShiftLeft(1, 127));
        Assert.AreEqual("Expression left shift exceeded the signed 128-bit range.", failure.Message);
        Assert.AreEqual(Int128.MinValue, ExpressionArithmetic.ShiftLeft(-1, 127));
        Assert.AreEqual((Int128)1 << 126, ExpressionArithmetic.ShiftLeft(1, 126));
        Assert.Throws<OverflowException>(() => ExpressionArithmetic.ShiftLeft((Int128)3 << 125, 1), "a bit shifted into the sign flips it");
        Assert.AreEqual((Int128)1 << 31, ExpressionArithmetic.ShiftLeft(1, 31), "the old 32-bit boundary is an ordinary value");
    }

    /// <summary>
    ///     A zero divisor fails with <see cref="DivideByZeroException"/> for every dividend, including dividends of 64 bits
    ///     or more, for which the .NET 8 runtime's own 128-bit division throws an index or argument exception instead.
    /// </summary>
    /// <param name="exponent">The dividend is 2 raised to this power plus 5, negated when <paramref name="negative"/> is set.</param>
    /// <param name="negative">Whether the dividend is negative.</param>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(33, false)]
    [DataRow(64, false)]
    [DataRow(70, true)]
    [DataRow(100, false)]
    [DataRow(126, true)]
    public void ZeroDivisor_FailsAsDivisionByZeroForEveryDividend(int exponent, bool negative)
    {
        Int128 dividend = negative ? -(Int128.One << exponent) - 5 : (Int128.One << exponent) + 5;
        Assert.ThrowsExactly<DivideByZeroException>(() => ExpressionArithmetic.Divide(dividend, Int128.Zero));
        Assert.ThrowsExactly<DivideByZeroException>(() => ExpressionArithmetic.Modulo(dividend, Int128.Zero));
    }
}
