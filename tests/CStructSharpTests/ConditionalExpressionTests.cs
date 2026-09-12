namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Checks branch predicates in the checked and exact integer evaluators.</summary>
[TestClass]
public class ConditionalExpressionTests
{
    /// <summary>Lazy dependencies cannot bypass depth/work limits, including after an expression was cached.</summary>
    [TestMethod]
    public void ConditionalDependencies_EnforceBudgetsOnlyWhenReached()
    {
        var variables = new Dictionary<string, Expr>();
        for (int index = 0; index < 30; index++)
        {
            variables["v" + index] = index == 29 ? new Literal(1) : new Identifier("v" + (index + 1));
        }

        Expr skipped = CStructDefinitionParser.Expr.ParseOrThrow("1 || v0");
        Expr active = CStructDefinitionParser.Expr.ParseOrThrow("0 || v0");
        foreach (ExpressionEvaluationLimits limits in new[]
        {
            new ExpressionEvaluationLimits(16, 1024),
            new ExpressionEvaluationLimits(128, 20),
        })
        {
            var evaluator = new ExpressionEvaluator(limits);
            Assert.AreEqual(1, evaluator.Evaluate(skipped, variables));
            Assert.AreEqual(System.Numerics.BigInteger.One, evaluator.EvaluateExact(skipped, variables, 64));
            for (int repeat = 0; repeat < 2; repeat++)
            {
                Assert.Throws<CStructLayoutException>(() => evaluator.Evaluate(active, variables));
                Assert.Throws<CStructLayoutException>(() => evaluator.EvaluateExact(active, variables, 64));
            }
        }

        var nested = new Dictionary<string, Expr>
        {
            ["a"] = CStructDefinitionParser.Expr.ParseOrThrow("0 || b"),
            ["b"] = CStructDefinitionParser.Expr.ParseOrThrow("1 && a"),
        };
        Expr cycle = CStructDefinitionParser.Expr.ParseOrThrow("0 || a");
        Assert.Throws<CStructLayoutException>(() => ExpressionEvaluator.Default.Evaluate(cycle, nested));
        Assert.Throws<CStructLayoutException>(() => ExpressionEvaluator.Default.EvaluateExact(cycle, nested, 64));
    }

    /// <summary>Logical operations normalize results and do not evaluate inactive operands.</summary>
    [TestMethod]
    public void LogicalExpressions_ShortCircuitAndRespectPrecedence()
    {
        foreach ((string source, int expected) in new[]
                 {
                     ("0 && (1 / 0)", 0), ("1 || missing", 1),
                     ("0 && missing || 3 >= 2 && !(5 == 4)", 1),
                     ("2 < 3 == 1", 1), ("3 <= 2", 0), ("4 != 4", 0),
                     ("1 | 2 && 4 > 3", 1), ("!5", 0), ("!!9", 1),
                 })
        {
            Expr expression = CStructDefinitionParser.Expr.Before(Pidgin.Parser<char>.End).ParseOrThrow(source);
            Assert.AreEqual(expected, expression.Calc(), source);
            Assert.AreEqual(new System.Numerics.BigInteger(expected), ExpressionEvaluator.Default.EvaluateExact(expression, null, 64), source);
        }
    }

    /// <summary>Active operands still report missing variables, arithmetic errors and dependency cycles.</summary>
    [TestMethod]
    public void ActiveOperands_PreserveFailures()
    {
        Assert.Throws<KeyNotFoundException>(() => CStructDefinitionParser.Expr.ParseOrThrow("1 && missing").Calc());
        Assert.Throws<DivideByZeroException>(() => CStructDefinitionParser.Expr.ParseOrThrow("0 || (1 / 0)").Calc());
        var variables = new Dictionary<string, Expr> { ["cycle"] = new Identifier("cycle") };
        Assert.AreEqual(1, ExpressionEvaluator.Default.Evaluate(CStructDefinitionParser.Expr.ParseOrThrow("1 || cycle"), variables));
        Assert.Throws<CStructLayoutException>(() => ExpressionEvaluator.Default.Evaluate(CStructDefinitionParser.Expr.ParseOrThrow("0 || cycle"), variables));
    }
}
