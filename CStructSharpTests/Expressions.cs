namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for expressions so changes to this behavior are caught.</summary>
[TestClass]
public class Expressions
{
    /// <summary>
    ///     These inputs are formulas, not binary records.
    /// </summary>
    /// <remarks>
    ///     For example, 10+20*30 must be 610 because multiplication takes priority. Division uses integer arithmetic,
    ///     and operators of equal priority are applied from left to right. Getting these rules wrong would also give
    ///     wrong array lengths in a layout.
    /// </remarks>
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
    ///     Grouping changes which calculation happens first: (10+20)*30 must give 900.
    /// </summary>
    /// <remarks>
    ///     Nested groups and negative factors must work too. Integer division discards the fractional part, so
    ///     expressions such as 5/10 become zero even inside larger calculations.
    /// </remarks>
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
    ///     The ampersand keeps only bits that are set in both operands.
    /// </summary>
    /// <remarks>
    ///     For example, 555&amp;3 produces 3 by retaining the lowest two bits. Both a single operation and a chain must
    ///     match C# integer results; this is useful for testing packed flag expressions.
    /// </remarks>
    [TestMethod]
    public void TestExpressionAnd()
    {
        Assert.AreEqual(555 & 3, CStructDefinitionParser.Expr.ParseOrThrow("555&3").Calc());
        Assert.AreEqual(10 & 500 & 3 & 2, CStructDefinitionParser.Expr.ParseOrThrow("10&500&3&2").Calc());
    }

    /// <summary>
    ///     555/3 must produce 185.
    /// </summary>
    /// <remarks>
    ///     The chain 10/500/3/2 becomes zero at the first division and stays zero because these are integers. This
    ///     checks that the expression evaluator neither uses floating-point division nor groups the chain from the
    ///     right.
    /// </remarks>
    [TestMethod]
    public void TestExpressionDiv()
    {
        Assert.AreEqual(555 / 3, CStructDefinitionParser.Expr.ParseOrThrow("555/3").Calc());
        Assert.AreEqual(10 / 500 / 3 / 2, CStructDefinitionParser.Expr.ParseOrThrow("10/500/3/2").Calc());
    }

    /// <summary>
    ///     100-1 must give 99, and 10-500-1-3 must give -494.
    /// </summary>
    /// <remarks>
    ///     Each subtraction uses the previous result. This matters when a layout calculates remaining space by
    ///     subtracting header sizes from a total length.
    /// </remarks>
    [TestMethod]
    public void TestExpressionMinus()
    {
        Assert.AreEqual(100 - 1, CStructDefinitionParser.Expr.ParseOrThrow("100-1").Calc());
        Assert.AreEqual(10 - 500 - 1 - 3, CStructDefinitionParser.Expr.ParseOrThrow("10-500-1-3").Calc());
    }

    /// <summary>
    ///     The vertical bar keeps a bit if either operand has it set.
    /// </summary>
    /// <remarks>
    ///     Both the pair and the longer chain must match C# bitwise OR. This is the operation used to combine
    ///     independent flags; it is different from adding their numbers.
    /// </remarks>
    [TestMethod]
    public void TestExpressionOr()
    {
        Assert.AreEqual(555 | 3, CStructDefinitionParser.Expr.ParseOrThrow("555|3").Calc());
        Assert.AreEqual(10 | 500 | 3 | 2, CStructDefinitionParser.Expr.ParseOrThrow("10|500|3|2").Calc());
    }

    /// <summary>
    ///     The evaluator must turn 1+1 into 2 and 10+500+1+3 into 514.
    /// </summary>
    /// <remarks>
    ///     These small checks establish how length and offset formulas add several integer terms before those formulas
    ///     are used in a struct declaration.
    /// </remarks>
    [TestMethod]
    public void TestExpressionPlus()
    {
        Assert.AreEqual(2, CStructDefinitionParser.Expr.ParseOrThrow("1+1").Calc());
        Assert.AreEqual(10 + 500 + 1 + 3, CStructDefinitionParser.Expr.ParseOrThrow("10+500+1+3").Calc());
    }

    /// <summary>
    ///     Moving bits left by two positions turns 555 into 2220.
    /// </summary>
    /// <remarks>
    ///     The longer chain applies each shift in order and is compared with C#. Layout constants often use shifts to
    ///     place flag bits at a specific position.
    /// </remarks>
    [TestMethod]
    public void TestExpressionShiftLeft()
    {
        Assert.AreEqual(555 << 2, CStructDefinitionParser.Expr.ParseOrThrow("555<<2").Calc());
        Assert.AreEqual(1000 << 3 << 4, CStructDefinitionParser.Expr.ParseOrThrow("1000<<3<<4").Calc());
    }

    /// <summary>
    ///     Moving the bits of 555 right by two positions gives 138; discarded low bits are lost.
    /// </summary>
    /// <remarks>
    ///     Chained shifts must agree with C#. This operation helps separate parts of an integer that contains several
    ///     packed values.
    /// </remarks>
    [TestMethod]
    public void TestExpressionShiftRight()
    {
        Assert.AreEqual(555 >> 2, CStructDefinitionParser.Expr.ParseOrThrow("555>>2").Calc());
        Assert.AreEqual(1000 >> 3 >> 4, CStructDefinitionParser.Expr.ParseOrThrow("1000>>3>>4").Calc());
    }

    /// <summary>
    ///     555*3 must be 1665, and 10*500*3*2 must be 30000.
    /// </summary>
    /// <remarks>
    ///     The test evaluates multiplication directly, without a struct. These calculations are the basis of formulas
    ///     such as element count multiplied by element size.
    /// </remarks>
    [TestMethod]
    public void TestExpressionTimes()
    {
        Assert.AreEqual(555 * 3, CStructDefinitionParser.Expr.ParseOrThrow("555*3").Calc());
        Assert.AreEqual(10 * 500 * 3 * 2, CStructDefinitionParser.Expr.ParseOrThrow("10*500*3*2").Calc());
    }

    /// <summary>
    ///     The expression contains the name a instead of a literal number.
    /// </summary>
    /// <remarks>
    ///     Passing a dictionary with a = 10 must make 1+1+a+3+4 equal 19. Spaces do not change the result; the
    ///     dictionary supplies the value used when the expression is evaluated.
    /// </remarks>
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
