namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks focused expression-compiler diagnostics and exact syntax-work limits.</summary>
[TestClass]
public class ExpressionCompilerBoundaryTests
{
    /// <summary>A missing expression is identified at the evaluator boundary, not as an internal cache-key failure.</summary>
    [TestMethod]
    public void NullExpression_IdentifiesTheArgument()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 10));

        // The evaluator's parameter name should survive the compiler's internal cache implementation.
        ArgumentNullException failure = Assert.Throws<ArgumentNullException>(() => evaluator.Compile(null!));
        Assert.AreEqual("expression", failure.ParamName);
    }

    /// <summary>Compilation accepts exactly three syntax nodes and rejects the same tree when only two are permitted.</summary>
    [TestMethod]
    public void SyntaxWork_ReportsTheConfiguredLimit()
    {
        Expr expression = CStructDefinitionParser.ParseExpression("1 + 2");
        new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 3)).Compile(expression);
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 2));

        // The two literal operands and their addition each count as one syntax node.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.Compile(expression));
        Assert.AreEqual("Maximum expression evaluation work exceeded.", failure.Message);
    }

    /// <summary>An invalid internal syntax operator reports its operator family and value before execution.</summary>
    /// <param name="unary">Whether to construct a unary rather than binary node.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void UnknownSyntaxOperator_IdentifiesItsFamily(bool unary)
    {
        Expr expression = unary
                              ? new UnaryOp((UnaryOperatorType)999, new Literal(1))
                              : new BinaryOp((BinaryOperatorType)999, new Literal(1), new Literal(2));
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 10));

        // Invalid internal enum values must not silently select an unrelated arithmetic operation.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => evaluator.Compile(expression));
        Assert.AreEqual(unary ? "Unknown unary operator: 999" : "Unknown binary operator: 999", failure.Message);
    }
}
