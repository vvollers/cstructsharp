namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for expressions so changes to this behavior are caught.</summary>
[TestClass]
public class Expressions
{
    /// <summary>
    ///     Binary format definitions frequently use C-like constant expressions to compute lengths and offsets. This test
    ///     validates precedence and associativity across mixed arithmetic operators without extra parentheses.
    /// </summary>
    [TestMethod]
    public void CombinedExpressions()
    {
        Assert.AreEqual(10 + (20 * 30), CStructDefinitionParser.Expr.ParseOrThrow("10+20*30").Calc());
        Assert.AreEqual(10 - (20 * 30), CStructDefinitionParser.Expr.ParseOrThrow("10-20*30").Calc());
        Assert.AreEqual((10 * 20) - 30, CStructDefinitionParser.Expr.ParseOrThrow("10*20-30").Calc());
        Assert.AreEqual((50 / 10) + (20 * 30), CStructDefinitionParser.Expr.ParseOrThrow("50/10+20*30").Calc());
        Assert.AreEqual((50 / 10) - (20 * 30), CStructDefinitionParser.Expr.ParseOrThrow("50/10-20*30").Calc());
        Assert.AreEqual((50 / 10 * 20) - 30, CStructDefinitionParser.Expr.ParseOrThrow("50/10*20-30").Calc());
        Assert.AreEqual((50 * 10) + (20 / 30), CStructDefinitionParser.Expr.ParseOrThrow("50*10+20/30").Calc());
        Assert.AreEqual((50 * 10) - (20 / 30), CStructDefinitionParser.Expr.ParseOrThrow("50*10-20/30").Calc());
        Assert.AreEqual((50 * 10 * 20) - 30, CStructDefinitionParser.Expr.ParseOrThrow("50*10*20-30").Calc());
        Assert.AreEqual(10 + (20 * 30 / 3), CStructDefinitionParser.Expr.ParseOrThrow("10+20*30/3").Calc());
        Assert.AreEqual(10 - (20 * 30 / 3), CStructDefinitionParser.Expr.ParseOrThrow("10-20*30/3").Calc());
        Assert.AreEqual((10 * 20) - (30 / 3), CStructDefinitionParser.Expr.ParseOrThrow("10*20-30/3").Calc());
        Assert.AreEqual((1 * 2) + 3, CStructDefinitionParser.Expr.ParseOrThrow("1*2+3").Calc());
        Assert.AreEqual(1 + (2 * 3) + 4, CStructDefinitionParser.Expr.ParseOrThrow("1+2*3+4").Calc());
        Assert.AreEqual(1 + (2 * 3 * 4 / 2) - 2, CStructDefinitionParser.Expr.ParseOrThrow("1+2*3*4/2-2").Calc());
        Assert.AreEqual(11 - 4 + 93, CStructDefinitionParser.Expr.ParseOrThrow("11-4+93").Calc());
        Assert.AreEqual(11 - 4 + 98 - 5, CStructDefinitionParser.Expr.ParseOrThrow("11-4+98-5").Calc());
        Assert.AreEqual(11 * 4 / 98 * 5, CStructDefinitionParser.Expr.ParseOrThrow("11*4/98*5").Calc());
        Assert.AreEqual(12 / 4 * 100 / 5, CStructDefinitionParser.Expr.ParseOrThrow("12/4*100/5").Calc());
        Assert.AreEqual(1 + (5 * 2) - 4 + 98 - 5, CStructDefinitionParser.Expr.ParseOrThrow("1+5*2-4+98-5").Calc());
        Assert.AreEqual(
                        1 + (5 * 2) - 4 + 98 - (5 / 2),
                        CStructDefinitionParser.Expr.ParseOrThrow(" 1 + 5 * 2 - 4+    98- 5/ 2").Calc());
    }

    /// <summary>
    ///     Parentheses are the primary mechanism in C expressions for forcing evaluation order. This test checks grouped
    ///     subexpressions, nested groups, and unary signs produce deterministic results.
    /// </summary>
    [TestMethod]
    public void ParenthesizedExpressions()
    {
        Assert.AreEqual((10 + 20) * 30, CStructDefinitionParser.Expr.ParseOrThrow("(10+20)*30").Calc());
        Assert.AreEqual(50 / (10 + 20) * 30, CStructDefinitionParser.Expr.ParseOrThrow("50/(10+20)*30").Calc());
        Assert.AreEqual(
                        ((1 + 5) * 2) - (4 + 98 - 5),
                        CStructDefinitionParser.Expr.ParseOrThrow("(1+5)*2-(4+98-5)").Calc());
        Assert.AreEqual(
                        11 * (4 / 12) * (5 / 10) * 6,
                        CStructDefinitionParser.Expr.ParseOrThrow("11*(4/2*(5/10)*6)").Calc());
        Assert.AreEqual((5 - (4 + 98 - 5)) * -3, CStructDefinitionParser.Expr.ParseOrThrow("(5-(4+98-5))*-3").Calc());
    }

    /// <summary>
    ///     Bitwise AND is used in C layouts to mask packed flags and isolate bit ranges. This test validates chained mask
    ///     evaluation.
    /// </summary>
    [TestMethod]
    public void TestExpressionAnd()
    {
        Assert.AreEqual(555 & 3, CStructDefinitionParser.Expr.ParseOrThrow("555&3").Calc());
        Assert.AreEqual(10 & 500 & 3 & 2, CStructDefinitionParser.Expr.ParseOrThrow("10&500&3&2").Calc());
    }

    /// <summary>
    ///     Integer division appears in C metadata math for stride, chunk, and size conversions. This test verifies
    ///     left-associative division behavior.
    /// </summary>
    [TestMethod]
    public void TestExpressionDiv()
    {
        Assert.AreEqual(555 / 3, CStructDefinitionParser.Expr.ParseOrThrow("555/3").Calc());
        Assert.AreEqual(10 / 500 / 3 / 2, CStructDefinitionParser.Expr.ParseOrThrow("10/500/3/2").Calc());
    }

    /// <summary>
    ///     Subtraction chains are common in offset-delta calculations in C declarations. This test confirms sequential
    ///     left-to-right subtraction semantics.
    /// </summary>
    [TestMethod]
    public void TestExpressionMinus()
    {
        Assert.AreEqual(100 - 1, CStructDefinitionParser.Expr.ParseOrThrow("100-1").Calc());
        Assert.AreEqual(10 - 500 - 1 - 3, CStructDefinitionParser.Expr.ParseOrThrow("10-500-1-3").Calc());
    }

    /// <summary>
    ///     Bitwise OR is used to compose flag words from independent bits. This test verifies chained OR evaluation for packed
    ///     constants.
    /// </summary>
    [TestMethod]
    public void TestExpressionOr()
    {
        Assert.AreEqual(555 | 3, CStructDefinitionParser.Expr.ParseOrThrow("555|3").Calc());
        Assert.AreEqual(10 | 500 | 3 | 2, CStructDefinitionParser.Expr.ParseOrThrow("10|500|3|2").Calc());
    }

    /// <summary>
    ///     Addition chains model cumulative sizes and offsets in C struct expressions. This test verifies deterministic
    ///     summation across multiple operands.
    /// </summary>
    [TestMethod]
    public void TestExpressionPlus()
    {
        Assert.AreEqual(2, CStructDefinitionParser.Expr.ParseOrThrow("1+1").Calc());
        Assert.AreEqual(10 + 500 + 1 + 3, CStructDefinitionParser.Expr.ParseOrThrow("10+500+1+3").Calc());
    }

    /// <summary>
    ///     Left shifts are a standard C technique for building masks and positioning flag bits. This test validates chained
    ///     shift semantics.
    /// </summary>
    [TestMethod]
    public void TestExpressionShiftLeft()
    {
        Assert.AreEqual(555 << 2, CStructDefinitionParser.Expr.ParseOrThrow("555<<2").Calc());
        Assert.AreEqual(1000 << 3 << 4, CStructDefinitionParser.Expr.ParseOrThrow("1000<<3<<4").Calc());
    }

    /// <summary>
    ///     Right shifts are used to extract high-order bit fields from packed integers. This test verifies chained right-shift
    ///     behavior.
    /// </summary>
    [TestMethod]
    public void TestExpressionShiftRight()
    {
        Assert.AreEqual(555 >> 2, CStructDefinitionParser.Expr.ParseOrThrow("555>>2").Calc());
        Assert.AreEqual(1000 >> 3 >> 4, CStructDefinitionParser.Expr.ParseOrThrow("1000>>3>>4").Calc());
    }

    /// <summary>
    ///     Multiplication is used in C layout formulas for element-stride and block-size calculations. This test validates
    ///     multi-operand multiplication order.
    /// </summary>
    [TestMethod]
    public void TestExpressionTimes()
    {
        Assert.AreEqual(555 * 3, CStructDefinitionParser.Expr.ParseOrThrow("555*3").Calc());
        Assert.AreEqual(10 * 500 * 3 * 2, CStructDefinitionParser.Expr.ParseOrThrow("10*500*3*2").Calc());
    }

    /// <summary>
    ///     C-style expressions in definitions often reference symbolic identifiers from defines. This test verifies variable
    ///     substitution works during expression evaluation.
    /// </summary>
    [TestMethod]
    public void TestExpressionWithVariable()
    {
        Expr? someVar = CStructDefinitionParser.Expr.ParseOrThrow("1 + 1+    a   + 3 + 4");
        var vars = new Dictionary<string, Expr>();
        vars["a"] = new Literal(10);
        int result = someVar.Calc(vars);
        Assert.AreEqual(19, result);
    }
}
