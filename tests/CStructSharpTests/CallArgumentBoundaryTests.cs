namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks the distinct syntax of ordinary call arguments and builtin type/field arguments.</summary>
[TestClass]
public class CallArgumentBoundaryTests
{
    /// <summary>The syntax parser retains arithmetic in later ordinary arguments, even though unknown calls cannot be evaluated.</summary>
    [TestMethod]
    public void OrdinaryCall_ParsesLaterExpressionArguments()
    {
        var call = Assert.IsInstanceOfType<Call>(LayoutParser.ParseExpression("unsupported(1, 2 + 3)"));
        Assert.HasCount(2, call.Arguments);
        Assert.IsInstanceOfType<BinaryOp>(call.Arguments[1]);
        Assert.AreEqual(5, call.Arguments[1].Calc());
    }

    /// <summary>The field-name argument of offsetof cannot contain an arithmetic expression.</summary>
    [TestMethod]
    public void Offsetof_RejectsArithmeticInsideItsFieldArgument()
    {
        // Reject invalid builtin syntax before evaluation could replace it with a different type error.
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseExpression("offsetof(root, value + 1)"));
    }
}
