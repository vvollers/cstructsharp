namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="LayoutExpressionEvaluator"/> directly, independent of a compiled <see cref="CStruct"/>
///     layout. Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class LayoutExpressionEvaluatorTests
{
    private static LayoutExpressionEvaluator CreateEvaluator(int maximumDepth = 64, int maximumNodes = 10_000)
    {
        return new LayoutExpressionEvaluator(
            new ExpressionEvaluator(new ExpressionEvaluationLimits(maximumDepth, maximumNodes)));
    }

    /// <summary>A literal expression evaluates to its own value without touching any variable.</summary>
    [TestMethod]
    public void Evaluate_Literal_ReturnsItsValue()
    {
        LayoutExpressionEvaluator evaluator = CreateEvaluator();

        int result = evaluator.Evaluate(new Literal(7), new Dictionary<string, Expr>(), "test");

        Assert.AreEqual(7, result);
    }

    /// <summary>An identifier resolves through the supplied variable dictionary.</summary>
    [TestMethod]
    public void Evaluate_KnownIdentifier_ResolvesFromVariables()
    {
        LayoutExpressionEvaluator evaluator = CreateEvaluator();
        var variables = new Dictionary<string, Expr> { ["count"] = new Literal(3), };

        int result = evaluator.Evaluate(new Identifier("count"), variables, "test");

        Assert.AreEqual(3, result);
    }

    /// <summary>An unresolved identifier is a deterministic expression-domain failure, wrapped with the caller's context.</summary>
    [TestMethod]
    public void Evaluate_UnknownIdentifier_ThrowsWithContext()
    {
        LayoutExpressionEvaluator evaluator = CreateEvaluator();

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => evaluator.Evaluate(new Identifier("missing"), new Dictionary<string, Expr>(), "array length for x"));

        StringAssert.Contains(exception.Message, "array length for x");
    }

    /// <summary>A CStructLayoutException raised by the underlying evaluator propagates unchanged, without double-wrapping.</summary>
    [TestMethod]
    public void Evaluate_UnderlyingLayoutException_PropagatesUnwrapped()
    {
        LayoutExpressionEvaluator evaluator = CreateEvaluator(maximumDepth: 1);
        Expr deep = new UnaryOp(UnaryOperatorType.Neg, new UnaryOp(UnaryOperatorType.Neg, new Literal(1)));

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => evaluator.Evaluate(deep, new Dictionary<string, Expr>(), "test"));

        StringAssert.Contains(exception.Message, "depth");
    }

    /// <summary>Every documented deterministic failure category is recognized as an expression failure.</summary>
    [TestMethod]
    public void IsExpressionFailure_RecognizedExceptionTypes_ReturnsTrue()
    {
        Assert.IsTrue(LayoutExpressionEvaluator.IsExpressionFailure(new InvalidOperationException()));
        Assert.IsTrue(LayoutExpressionEvaluator.IsExpressionFailure(new OverflowException()));
        Assert.IsTrue(LayoutExpressionEvaluator.IsExpressionFailure(new KeyNotFoundException()));
        Assert.IsTrue(LayoutExpressionEvaluator.IsExpressionFailure(new NotSupportedException()));
    }

    /// <summary>An exception outside the documented expression-domain categories is not swallowed.</summary>
    [TestMethod]
    public void IsExpressionFailure_UnrelatedExceptionType_ReturnsFalse()
    {
        Assert.IsFalse(LayoutExpressionEvaluator.IsExpressionFailure(new ArgumentException()));
    }
}
