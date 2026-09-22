namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks expression execution through both the signed layout evaluator and the exact enum evaluator.</summary>
[TestClass]
public class ExpressionEvaluatorBoundaryTests
{
    /// <summary>Variable comparisons and logical operators have identical normalized results in both evaluators.</summary>
    /// <param name="operation">The binary comparison or logical operator.</param>
    /// <param name="left">The first variable value.</param>
    /// <param name="right">The second variable value.</param>
    /// <param name="expected">The expected zero-or-one result.</param>
    [TestMethod]
    [DataRow("==", -3, -3, 1)]
    [DataRow("==", -3, 2, 0)]
    [DataRow("!=", -3, -3, 0)]
    [DataRow("!=", -3, 2, 1)]
    [DataRow("<", -3, -3, 0)]
    [DataRow("<", -3, 2, 1)]
    [DataRow("<", 2, -3, 0)]
    [DataRow("<=", -3, -3, 1)]
    [DataRow("<=", -3, 2, 1)]
    [DataRow("<=", 2, -3, 0)]
    [DataRow(">", -3, -3, 0)]
    [DataRow(">", -3, 2, 0)]
    [DataRow(">", 2, -3, 1)]
    [DataRow(">=", -3, -3, 1)]
    [DataRow(">=", -3, 2, 0)]
    [DataRow(">=", 2, -3, 1)]
    [DataRow("&&", 0, 0, 0)]
    [DataRow("&&", 0, -3, 0)]
    [DataRow("&&", -3, 0, 0)]
    [DataRow("&&", -3, 2, 1)]
    [DataRow("||", 0, 0, 0)]
    [DataRow("||", 0, -3, 1)]
    [DataRow("||", -3, 0, 1)]
    [DataRow("||", -3, 2, 1)]
    public void VariableTruthValues_AgreeAcrossEvaluators(string operation, int left, int right, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression($"a {operation} b");
        var variables = new Dictionary<string, Expr> { ["a"] = new Literal(left), ["b"] = new Literal(right), };
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(expected, evaluator.Evaluate(expression, variables));
        Assert.AreEqual(expected, evaluator.CreateSession(variables).Evaluate(expression));
        Assert.AreEqual(new BigInteger(expected), evaluator.EvaluateExact(expression, variables, 64));
    }

    /// <summary>Unselected logical and conditional branches must not resolve an undefined variable.</summary>
    /// <param name="source">An expression with an unreachable variable reference.</param>
    /// <param name="expected">The selected result.</param>
    [TestMethod]
    [DataRow("0 && missing", 0)]
    [DataRow("2 || missing", 1)]
    [DataRow("1 ? 7 : missing", 7)]
    [DataRow("0 ? missing : 9", 9)]
    public void ShortCircuit_SkipsUndefinedBranches(string source, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(expected, evaluator.Evaluate(expression));
        Assert.AreEqual(new BigInteger(expected), evaluator.EvaluateExact(expression, null, 64));
    }

    /// <summary>Exact modulo preserves the dividend's sign and rejects a zero divisor.</summary>
    [TestMethod]
    public void ExactModulo_PreservesRemaindersAndZeroFailure()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(new BigInteger(-2), evaluator.EvaluateExact(CStructDefinitionParser.ParseExpression("-17 % 5"), null, 64));

        // Enum arithmetic is exact, but division by zero still has no mathematical result.
        Assert.Throws<DivideByZeroException>(() => evaluator.EvaluateExact(CStructDefinitionParser.ParseExpression("17 % 0"), null, 64));
    }

    /// <summary>Exact shifts report the caller's enum width and accept its highest valid bit index.</summary>
    [TestMethod]
    public void ExactShift_UsesTheDeclaredWidth()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(new BigInteger(128), evaluator.EvaluateExact(CStructDefinitionParser.ParseExpression("1 << 7"), null, 8));

        // The eighth bit index is outside an eight-bit declaration even though BigInteger can represent it.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => evaluator.EvaluateExact(CStructDefinitionParser.ParseExpression("1 << 8"), null, 8));
        Assert.AreEqual("Enum expression shift count must be between 0 and 7.", failure.Message);
    }
}
