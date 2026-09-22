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
        // A positive high bit cannot be represented as a signed Int32 result.
        OverflowException failure = Assert.Throws<OverflowException>(() => ExpressionArithmetic.ShiftLeft(1, 31));
        Assert.AreEqual("Expression left shift exceeded the signed 32-bit range.", failure.Message);
    }
}
