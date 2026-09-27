namespace CStructSharp.Tests;

using CStructSharp.Expressions;

/// <summary>Checks signed remainder results and the two exceptional divisor cases shared by expression evaluators.</summary>
[TestClass]
public class ModuloBoundaryTests
{
    /// <summary>The remainder retains the dividend's sign and accepts representable endpoint operations.</summary>
    /// <param name="left">Signed dividend.</param>
    /// <param name="right">Nonzero divisor.</param>
    /// <param name="expected">The exact signed remainder.</param>
    [TestMethod]
    [DataRow(int.MinValue, 1, 0)]
    [DataRow(int.MaxValue, -1, 0)]
    [DataRow(-17, 5, -2)]
    [DataRow(17, -5, 2)]
    public void Remainder_RetainsSignedIntegerSemantics(int left, int right, int expected)
    {
        Assert.AreEqual(expected, ExpressionArithmetic.Modulo(left, right));
    }

    /// <summary>The CLR rejects minimum-integer division by minus one even when evaluating a remainder.</summary>
    [TestMethod]
    public void MinimumIntegerRemainder_RejectsTheOverflowingDivision()
    {
        Assert.Throws<OverflowException>(() => ExpressionArithmetic.Modulo(int.MinValue, -1));
    }

    /// <summary>A zero divisor fails rather than returning a plausible numeric remainder.</summary>
    [TestMethod]
    public void ZeroDivisor_RejectsTheRemainder()
    {
        Assert.Throws<DivideByZeroException>(() => ExpressionArithmetic.Modulo(1, 0));
    }
}
