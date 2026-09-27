namespace CStructSharp.Tests;

using System.Collections;
using System.Numerics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>
///     Checks expression execution through both the signed layout evaluator and the exact enum evaluator, and the
///     ownership of the evaluator's rented value stack.
/// </summary>
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
        Expr expression = LayoutParser.ParseExpression($"a {operation} b");
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
        Expr expression = LayoutParser.ParseExpression(source);
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(expected, evaluator.Evaluate(expression));
        Assert.AreEqual(new BigInteger(expected), evaluator.EvaluateExact(expression, null, 64));
    }

    /// <summary>Exact modulo preserves the dividend's sign and rejects a zero divisor.</summary>
    [TestMethod]
    public void ExactModulo_PreservesRemaindersAndZeroFailure()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(new BigInteger(-2), evaluator.EvaluateExact(LayoutParser.ParseExpression("-17 % 5"), null, 64));

        // Enum arithmetic is exact, but division by zero still has no mathematical result.
        Assert.Throws<DivideByZeroException>(() => evaluator.EvaluateExact(LayoutParser.ParseExpression("17 % 0"), null, 64));
    }

    /// <summary>Exact shifts report the caller's enum width and accept its highest valid bit index.</summary>
    [TestMethod]
    public void ExactShift_UsesTheDeclaredWidth()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        Assert.AreEqual(new BigInteger(128), evaluator.EvaluateExact(LayoutParser.ParseExpression("1 << 7"), null, 8));

        // The eighth bit index is outside an eight-bit declaration even though BigInteger can represent it.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => evaluator.EvaluateExact(LayoutParser.ParseExpression("1 << 8"), null, 8));
        Assert.AreEqual("Enum expression shift count must be between 0 and 7.", failure.Message);
    }

    /// <summary>The outer expression stack is returned even when a selected dependency fails in its own stack.</summary>
    /// <param name="fail">Whether the selected dependency divides by zero.</param>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedDependency_ReturnsTheOuterStack(bool fail)
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(20, 100));
        Expr root = LayoutParser.ParseExpression("1 ? value : 0");
        Expr dependency = LayoutParser.ParseExpression(fail ? "1 / 0" : "7");
        evaluator.Compile(root);
        evaluator.Compile(dependency);
        using var returns = new PoolReturnListener();
        var variables = new ObservedVariables(dependency, returns);
        ExpressionEvaluator.ExpressionEvaluationSession session = evaluator.CreateSession(variables);
        if (fail)
        {
            // The dependency fails after both outer and inner value stacks have been rented.
            Assert.Throws<DivideByZeroException>(() => session.Evaluate(root));
        }
        else
        {
            Assert.AreEqual(7, session.Evaluate(root));
        }

        Assert.IsTrue(variables.Observed);
        Assert.IsTrue(returns.Returned, "The exact outer value-stack rental must be returned before evaluation exits.");
    }

    /// <summary>Observes the outer stack at its first conditional lookup, before a dependency rents another stack.</summary>
    private sealed class ObservedVariables : IReadOnlyDictionary<string, Expr>
    {
        private readonly Dictionary<string, Expr> entries;
        private readonly PoolReturnListener returns;

        /// <summary>Stores one immutable dependency and the listener for its enclosing operation.</summary>
        /// <param name="dependency">The expression returned for value.</param>
        /// <param name="returns">Listener that records the latest rental and its exact return.</param>
        public ObservedVariables(Expr dependency, PoolReturnListener returns)
        {
            this.entries = new Dictionary<string, Expr> { ["value"] = dependency, };
            this.returns = returns;
        }

        /// <summary>Gets the single dependency count.</summary>
        public int Count => this.entries.Count;

        /// <summary>Gets the dependency names.</summary>
        public IEnumerable<string> Keys => this.entries.Keys;

        /// <summary>Gets the dependency expressions.</summary>
        public IEnumerable<Expr> Values => this.entries.Values;

        /// <summary>Gets whether the outer rental was observed at the first selected lookup.</summary>
        public bool Observed { get; private set; }

        /// <summary>Gets the expression bound to a dependency name.</summary>
        /// <param name="key">Dependency name.</param>
        /// <returns>The bound expression; throws for an unknown name.</returns>
        public Expr this[string key] => this.entries[key];

        /// <summary>Checks whether a dependency exists without triggering the lookup observation.</summary>
        /// <param name="key">Dependency name.</param>
        /// <returns>Whether the name is bound.</returns>
        public bool ContainsKey(string key) => this.entries.ContainsKey(key);

        /// <summary>Watches the latest outer stack once, then returns the requested dependency.</summary>
        /// <param name="key">Dependency name.</param>
        /// <param name="value">The bound expression on success.</param>
        /// <returns>Whether the dependency exists.</returns>
        public bool TryGetValue(string key, out Expr value)
        {
            if (!this.Observed)
            {
                Assert.AreNotEqual(0, this.returns.LastRental);
                Assert.AreEqual(16, this.returns.LastRentalLength);
                this.returns.Watch(this.returns.LastRental);
                this.Observed = true;
            }

            return this.entries.TryGetValue(key, out value!);
        }

        /// <summary>Enumerates the immutable dependency bindings.</summary>
        /// <returns>An enumerator over the stored binding.</returns>
        public IEnumerator<KeyValuePair<string, Expr>> GetEnumerator() => this.entries.GetEnumerator();

        /// <summary>Enumerates the immutable dependency bindings through the non-generic interface.</summary>
        /// <returns>An enumerator over the stored binding.</returns>
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
