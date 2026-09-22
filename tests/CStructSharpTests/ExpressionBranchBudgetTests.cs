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
    /// <summary>Exact arithmetic includes the dependency depth of either binary operand.</summary>
    /// <param name="source">An addition with a unary dependency on its left or right.</param>
    [TestMethod]
    [DataRow("1 + value")]
    [DataRow("value + 1")]
    public void ExactBinaryDependencyDepth_CountsEitherOperand(string source)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr> { ["value"] = CStructDefinitionParser.ParseExpression("-1"), };
        var sufficient = new ExpressionEvaluator(new ExpressionEvaluationLimits(4, 100));
        Assert.AreEqual(BigInteger.Zero, sufficient.EvaluateExact(expression, variables, 64));
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 100));

        // addition -> identifier -> negation -> literal requires four levels whichever operand holds the name.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.EvaluateExact(expression, variables, 64));
        Assert.AreEqual("Maximum expression evaluation depth exceeded.", failure.Message);
    }

    /// <summary>Named dependencies consume validation work as well as the root's complete program.</summary>
    /// <param name="source">The root expression selecting a named dependency.</param>
    /// <param name="dependency">The dependency expression, including any conditional branches.</param>
    /// <param name="work">The total root and dependency instructions that validation must count.</param>
    /// <param name="expected">The value returned with sufficient validation work.</param>
    [TestMethod]
    [DataRow("1 ? value : 0", "1 + 2", 9, 3)]
    [DataRow("0 ? 0 : value", "1 + 2", 9, 3)]
    [DataRow("value", "1 ? 2 : 3", 7, 2)]
    [DataRow("value", "0 ? 2 : 3", 7, 3)]
    public void SelectedDependencyValidation_ChargesTheWholeProgram(string source, string dependency, int work, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr> { ["value"] = CStructDefinitionParser.ParseExpression(dependency), };
        var sufficient = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, work));
        Assert.AreEqual(expected, sufficient.CreateSession(variables).Evaluate(expression));
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, work - 1));

        // Validation counts every instruction in both programs, including an unselected conditional arm.
        // The executed path is shorter, so execution accounting cannot replace validation accounting.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.CreateSession(variables).Evaluate(expression));
        Assert.AreEqual("Maximum expression evaluation work exceeded.", failure.Message);
    }

    /// <summary>A cached dependency value does not erase its depth along a later selected conditional path.</summary>
    /// <param name="source">A conditional selecting the same nested dependency through either arm.</param>
    [TestMethod]
    [DataRow("1 ? a : 0")]
    [DataRow("0 ? 0 : a")]
    public void SelectedCachedDependency_PreservesItsFullDepth(string source)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr>
        {
            ["a"] = CStructDefinitionParser.ParseExpression("b + 1"),
            ["b"] = CStructDefinitionParser.ParseExpression("-1"),
        };
        ExpressionEvaluator.ExpressionEvaluationSession sufficient = new ExpressionEvaluator(new ExpressionEvaluationLimits(6, 100)).CreateSession(variables);
        Assert.AreEqual(-1, sufficient.Evaluate(new Identifier("b")));
        Assert.AreEqual(0, sufficient.Evaluate(expression));
        ExpressionEvaluator.ExpressionEvaluationSession limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(5, 100)).CreateSession(variables);
        Assert.AreEqual(-1, limited.Evaluate(new Identifier("b")));

        // conditional -> a -> addition -> b -> negation -> literal still has six levels after b is cached.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.Evaluate(expression));
        Assert.AreEqual("Maximum expression evaluation depth exceeded.", failure.Message);
    }

    /// <summary>Each binary or conditional child adds one level, including branches that need not execute.</summary>
    /// <param name="source">An expression whose deepest child occupies exactly three levels.</param>
    [TestMethod]
    [DataRow("(1 + 2) + 3")]
    [DataRow("1 + (2 + 3)")]
    [DataRow("(1 + 2) && 3")]
    [DataRow("0 && (2 + 3)")]
    [DataRow("(1 + 2) || 3")]
    [DataRow("1 || (2 + 3)")]
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

    /// <summary>Returning from either conditional arm preserves the surrounding arithmetic instructions.</summary>
    /// <param name="source">A conditional nested inside a unary or binary expression.</param>
    /// <param name="expected">The result after executing both the selected arm and its surrounding arithmetic.</param>
    [TestMethod]
    [DataRow("1 + (1 ? 2 : 3)", 3)]
    [DataRow("(1 ? 2 : 3) + 4", 6)]
    [DataRow("-(1 ? 2 : 3)", -2)]
    [DataRow("1 + (0 ? 2 : 3)", 4)]
    public void NestedConditionalJoin_PreservesFollowingInstructions(string source, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 100));

        Assert.AreEqual(expected, evaluator.Evaluate(expression));
        Assert.AreEqual(expected, evaluator.CreateSession().Evaluate(expression));
        Assert.AreEqual(new BigInteger(expected), evaluator.EvaluateExact(expression, null, 64));
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
