namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks expression depth through every branch position and cumulative work after failed evaluations.</summary>
[TestClass]
public class ExpressionBranchBudgetTests
{
    /// <summary>Each binary or conditional child adds one level, including branches that need not execute.</summary>
    /// <param name="source">An expression whose deepest child occupies exactly three levels.</param>
    [TestMethod]
    [DataRow("(1 + 2) + 3")]
    [DataRow("1 + (2 + 3)")]
    [DataRow("(1 + 2) ? 3 : 4")]
    [DataRow("1 ? (2 + 3) : 4")]
    [DataRow("0 ? 1 : (2 + 3)")]
    public void CompilationDepth_CountsEveryChildPosition(string source)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 100)).Compile(expression);
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(2, 100));

        // Tree validation counts all syntax, independently of which runtime branch is selected.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.Compile(expression));
        Assert.AreEqual("Maximum expression evaluation depth exceeded.", failure.Message);
    }

    /// <summary>Exact arithmetic includes a selected identifier's expression depth in every conditional position.</summary>
    /// <param name="source">A conditional whose selected dependency needs four levels.</param>
    /// <param name="expected">The result with a sufficient depth budget.</param>
    [TestMethod]
    [DataRow("value ? 2 : 3", 2)]
    [DataRow("1 ? value : 3", -1)]
    [DataRow("0 ? 2 : value", -1)]
    public void ExactConditionalDepth_IncludesSelectedDependencies(string source, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr> { ["value"] = CStructDefinitionParser.ParseExpression("-1"), };
        var sufficient = new ExpressionEvaluator(new ExpressionEvaluationLimits(4, 100));
        Assert.AreEqual(new BigInteger(expected), sufficient.EvaluateExact(expression, variables, 64));
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 100));

        // The root syntax fits; the referenced unary expression adds the fourth level during execution.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.EvaluateExact(expression, variables, 64));
        Assert.AreEqual("Maximum expression evaluation depth exceeded.", failure.Message);
    }

    /// <summary>A selected conditional dependency must fit the cumulative depth even when it has no further names.</summary>
    /// <param name="source">A conditional selecting a unary dependency through either result arm.</param>
    [TestMethod]
    [DataRow("1 ? value : 0")]
    [DataRow("0 ? 0 : value")]
    public void SessionConditionalDepth_IncludesTheSelectedProgram(string source)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr> { ["value"] = CStructDefinitionParser.ParseExpression("-1"), };
        var sufficient = new ExpressionEvaluator(new ExpressionEvaluationLimits(4, 100));
        Assert.AreEqual(-1, sufficient.Evaluate(expression, variables));
        Assert.AreEqual(-1, sufficient.CreateSession(variables).Evaluate(expression));
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 100));

        // The selected program has no names of its own; its unary tree still contributes two levels.
        CStructLayoutException direct = Assert.Throws<CStructLayoutException>(() => limited.Evaluate(expression, variables));
        CStructLayoutException session = Assert.Throws<CStructLayoutException>(() => limited.CreateSession(variables).Evaluate(expression));
        Assert.AreEqual("Maximum expression evaluation depth exceeded.", direct.Message);
        Assert.AreEqual(direct.Message, session.Message);
    }

    /// <summary>Repeated failed dependencies consume execution work even when their validation has been cached.</summary>
    [TestMethod]
    public void FailedSessionEvaluations_StillConsumeWork()
    {
        var variables = new Dictionary<string, Expr> { ["bad"] = CStructDefinitionParser.ParseExpression("1 / 0"), };
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 8));
        ExpressionEvaluator.ExpressionEvaluationSession session = evaluator.CreateSession(variables);
        Expr root = new Identifier("bad");

        // Each failed attempt executes the identifier and three dependency instructions; no result is cached.
        Assert.Throws<DivideByZeroException>(() => session.Evaluate(root));
        Assert.Throws<DivideByZeroException>(() => session.Evaluate(root));
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => session.Evaluate(root));
        Assert.AreEqual("Maximum expression evaluation work exceeded.", failure.Message);
    }
}
