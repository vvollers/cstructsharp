namespace CStructSharp.Tests;

using System.Collections;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks exact ownership of the expression machine's rented value stack on success and failure.</summary>
[TestClass]
[DoNotParallelize]
public class ExpressionPoolOwnershipTests
{
    /// <summary>The outer expression stack is returned even when a selected dependency fails in its own stack.</summary>
    /// <param name="fail">Whether the selected dependency divides by zero.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedDependency_ReturnsTheOuterStack(bool fail)
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(20, 100));
        Expr root = CStructDefinitionParser.ParseExpression("1 ? value : 0");
        Expr dependency = CStructDefinitionParser.ParseExpression(fail ? "1 / 0" : "7");
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
