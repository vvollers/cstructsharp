namespace CStructSharp.Tests;

using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks dotted identifier boundaries and the parser's qualified-name metadata.</summary>
[TestClass]
public class QualifiedIdentifierBoundaryTests
{
    /// <summary>Ordinary arithmetic does not request qualified-variable publication.</summary>
    [TestMethod]
    public void UnqualifiedExpression_DoesNotSetTheQualifiedNameFlag()
    {
        _ = LayoutParser.ParseLayout("struct root { uint8 left; uint8 right; uint8 bytes[left+right]; };", null, null, out bool qualified);
        Assert.IsFalse(qualified);
    }

    /// <summary>Whitespace before a dot is trivia, and a final dotted name may end at the end of input.</summary>
    /// <param name="source">A dotted expression with or without preceding whitespace.</param>
    [TestMethod]
    [DataRow("left.right")]
    [DataRow("left .right")]
    public void DottedName_PreservesItsToken(string source)
    {
        var name = Assert.IsInstanceOfType<Identifier>(LayoutParser.ParseExpression(source));
        Assert.AreEqual("left.right", name.Name);
    }

    /// <summary>An operator following a dotted name still separates two expression operands.</summary>
    [TestMethod]
    public void QualifiedOperand_DoesNotConsumeTheFollowingOperator()
    {
        var sum = Assert.IsInstanceOfType<BinaryOp>(LayoutParser.ParseExpression("left.right+other"));
        Assert.AreEqual(7, sum.Calc(new Dictionary<string, Expr> { ["left.right"] = new Literal(3), ["other"] = new Literal(4), }));
    }
}
