namespace CStructSharp.Tests;

using System.Numerics;
using System.Reflection;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks work and dependency-depth accounting across simple, session and exact expression execution.</summary>
[TestClass]
public class ExpressionSessionBoundaryTests
{
    /// <summary>Validation work cannot wrap its counter and silently admit more instructions.</summary>
    [TestMethod]
    public void ValidationCounterOverflow_DoesNotWrap()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, int.MaxValue));
        ExpressionEvaluator.ExpressionEvaluationSession session = evaluator.CreateSession();
        FieldInfo counter = typeof(ExpressionEvaluator.ExpressionEvaluationSession).GetField("validatedNodes", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Seed a valid near-limit state instead of executing billions of earlier validation steps.
        counter.SetValue(session, int.MaxValue - 1);
        Expr expression = CStructDefinitionParser.ParseExpression("1 + 2");

        // Three more instructions overflow the checked counter; the failed assignment must leave it unchanged.
        Assert.Throws<OverflowException>(() => session.Evaluate(expression));
        Assert.AreEqual(int.MaxValue - 1, counter.GetValue(session));
    }

    /// <summary>A missing name reached through a selected branch retains the same diagnostic in every evaluator.</summary>
    /// <param name="source">A conditional or logical expression that selects the missing dependency.</param>
    [TestMethod]
    [DataRow("1 ? missing : 0")]
    [DataRow("0 ? 0 : missing")]
    [DataRow("1 && missing")]
    [DataRow("0 || missing")]
    public void SelectedMissingDependency_IdentifiesTheName(string source)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr>();
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(20, 100));

        for (int engine = 0; engine < 3; engine++)
        {
            // Selected references are resolved during execution, after unconditional dependency validation.
            KeyNotFoundException failure = Assert.Throws<KeyNotFoundException>(() => Evaluate(evaluator, expression, variables, engine));
            Assert.AreEqual("Undefined expression identifier: missing", failure.Message);
        }
    }

    /// <summary>A cycle through selected branches is diagnosed as a cycle before it exhausts a general depth or work limit.</summary>
    /// <param name="dependency">A selected branch that refers back to its own variable.</param>
    [TestMethod]
    [DataRow("1 ? a : 0")]
    [DataRow("0 ? 0 : a")]
    [DataRow("1 && a")]
    [DataRow("0 || a")]
    public void SelectedDependencyCycle_IdentifiesTheRepeatedName(string dependency)
    {
        var variables = new Dictionary<string, Expr> { ["a"] = CStructDefinitionParser.ParseExpression(dependency), };
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(20, 100));

        for (int engine = 0; engine < 3; engine++)
        {
            // Static validation skips conditional edges; the selected runtime edge must still detect re-entry.
            CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => Evaluate(evaluator, new Identifier("a"), variables, engine));
            Assert.AreEqual("Circular expression dependency detected at: a", failure.Message);
        }
    }

    /// <summary>Nested unary expressions bypass the one-operator fast path without speculative dependency lookups.</summary>
    /// <param name="source">An expression containing two unary operators around one dependency.</param>
    /// <param name="expected">The result after both unary operations.</param>
    [TestMethod]
    [DataRow("-(-value)", 2)]
    [DataRow("~(~value)", 2)]
    public void NestedUnary_DoesNotRepeatDependencyLookups(string source, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 100));
        var sessionVariables = new CountedVariables { ["value"] = new Literal(2), };
        var directVariables = new CountedVariables { ["value"] = new Literal(2), };

        Assert.AreEqual(expected, evaluator.CreateSession(sessionVariables).Evaluate(expression));
        Assert.AreEqual(expected, evaluator.Evaluate(expression, directVariables));
        Assert.IsTrue(sessionVariables.Lookups > 0);
        Assert.AreEqual(sessionVariables.Lookups, directVariables.Lookups, "Rejecting the simple path must not add a dependency lookup before session evaluation.");
    }

    /// <summary>The allocation-free path still counts a literal variable's dependency level and explains depth failures.</summary>
    /// <param name="source">A scalar, unary or binary expression eligible for the simple execution path.</param>
    /// <param name="depth">The exact permitted depth, including the referenced literal.</param>
    /// <param name="expected">The result when the full dependency path fits.</param>
    [TestMethod]
    [DataRow("value", 2, 2)]
    [DataRow("-value", 3, -2)]
    [DataRow("value + 1", 3, 3)]
    public void SimpleDependencyDepth_ExplainsItsLimit(string source, int depth, int expected)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        var variables = new Dictionary<string, Expr> { ["value"] = new Literal(2), };
        Assert.AreEqual(expected, new ExpressionEvaluator(new ExpressionEvaluationLimits(depth, 100)).Evaluate(expression, variables));
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(depth - 1, 100));

        // The root syntax fits; resolving its literal variable adds the final dependency level.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.Evaluate(expression, variables));
        Assert.AreEqual("Maximum expression evaluation depth exceeded.", failure.Message);
    }

    /// <summary>A referenced expression contributes its full depth, not just the root identifier's depth.</summary>
    /// <param name="engine">Zero for the normal entry point, one for a session, two for exact arithmetic.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void DependencyDepth_AcceptsExactBoundaryAndRejectsOneLess(int engine)
    {
        var variables = new Dictionary<string, Expr> { ["a"] = CStructDefinitionParser.ParseExpression("b + 1"), ["b"] = new Literal(2), };
        Expr root = new Identifier("a");
        Assert.AreEqual(new BigInteger(3), Evaluate(new ExpressionEvaluator(new ExpressionEvaluationLimits(4, 100)), root, variables, engine));

        // root a -> addition -> identifier b -> literal 2 is a four-level dependency path.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => Evaluate(new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 100)), root, variables, engine));
        StringAssert.StartsWith(failure.Message, "Maximum expression evaluation depth exceeded.");
    }

    /// <summary>Repeated identifiers share one dependency value, while distinct identifiers each consume work.</summary>
    /// <param name="engine">Zero for the normal entry point, one for a session, two for exact arithmetic.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void DependencyWork_CountsEachDistinctLiteralOnce(int engine)
    {
        var variables = new Dictionary<string, Expr> { ["a"] = new Literal(2), ["b"] = new Literal(3), };
        Expr repeated = CStructDefinitionParser.ParseExpression("a + a");
        Expr distinct = CStructDefinitionParser.ParseExpression("a + b");
        Assert.AreEqual(new BigInteger(4), Evaluate(new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 4)), repeated, variables, engine));
        Assert.AreEqual(new BigInteger(5), Evaluate(new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 5)), distinct, variables, engine));

        // Three root nodes plus two distinct referenced literals require five units of work.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => Evaluate(new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 4)), distinct, variables, engine));
        StringAssert.StartsWith(failure.Message, "Maximum expression evaluation work exceeded.");
    }

    /// <summary>One session shares its finite work counter across separate root evaluations.</summary>
    [TestMethod]
    public void SessionWork_IsCumulativeAcrossRoots()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 2));
        ExpressionEvaluator.ExpressionEvaluationSession session = evaluator.CreateSession();
        Assert.AreEqual(1, session.Evaluate(new Literal(1)));
        Assert.AreEqual(2, session.Evaluate(new Literal(2)));

        // A new root is not permission to reset a caller-owned evaluation session's budget.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => session.Evaluate(new Literal(3)));
        StringAssert.StartsWith(failure.Message, "Maximum expression evaluation work exceeded.");
    }

    /// <summary>Call discovery walks unary, binary and all conditional positions, even branches not executed.</summary>
    /// <param name="source">The expression containing a layout call.</param>
    [TestMethod]
    [DataRow("-sizeof(root)")]
    [DataRow("sizeof(root) + 1")]
    [DataRow("1 + sizeof(root)")]
    [DataRow("sizeof(root) ? 1 : 2")]
    [DataRow("0 ? sizeof(root) : 2")]
    [DataRow("1 ? 2 : sizeof(root)")]
    public void CallDiscovery_VisitsEveryChildPosition(string source)
    {
        Expr expression = CStructDefinitionParser.ParseExpression(source);
        Assert.IsTrue(ExpressionEvaluator.ContainsCall(expression));
        Assert.IsFalse(ExpressionEvaluator.ContainsCall(CStructDefinitionParser.ParseExpression("1 ? -2 : 3 + 4")));
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(20, 100));

        // Calls must be folded by layout compilation before ordinary expression compilation can execute them.
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => evaluator.Compile(expression));
        Assert.AreEqual("Expression calls are parsed but are not supported.", failure.Message);
    }

    /// <summary>Undefined and circular references preserve their offending name in every evaluator.</summary>
    /// <param name="engine">Zero for the normal entry point, one for a session, two for exact arithmetic.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void InvalidDependencies_IdentifyMissingAndCircularNames(int engine)
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(20, 100));
        var variables = new Dictionary<string, Expr> { ["a"] = new Identifier("b"), ["b"] = new Identifier("a"), };

        // A missing name is a lookup failure, whereas a cycle is an invalid dependency graph.
        Assert.AreEqual("Undefined expression identifier: missing", Assert.Throws<KeyNotFoundException>(() => Evaluate(evaluator, new Identifier("missing"), variables, engine)).Message);
        StringAssert.StartsWith(Assert.Throws<CStructLayoutException>(() => Evaluate(evaluator, new Identifier("a"), variables, engine)).Message, "Circular expression dependency detected at: a");
    }

    /// <summary>Evaluates the same syntax through one selected execution entry point without changing its limits.</summary>
    /// <param name="evaluator">The evaluator with the tested finite limits.</param>
    /// <param name="root">The expression to execute.</param>
    /// <param name="variables">The immutable name view for this evaluation.</param>
    /// <param name="engine">Zero for normal, one for explicit session, two for exact arithmetic.</param>
    /// <returns>The integer result; propagates the selected evaluator's documented failure.</returns>
    private static BigInteger Evaluate(ExpressionEvaluator evaluator, Expr root, IReadOnlyDictionary<string, Expr> variables, int engine)
        => engine switch
        {
            0 => evaluator.Evaluate(root, variables),
            1 => evaluator.CreateSession(variables).Evaluate(root),
            _ => evaluator.EvaluateExact(root, variables, 64),
        };

    /// <summary>Counts lookups through the evaluator's read-only view without changing the stored expressions.</summary>
    private sealed class CountedVariables : Dictionary<string, Expr>, IReadOnlyDictionary<string, Expr>
    {
        /// <summary>Gets how often evaluation requested a named dependency.</summary>
        public int Lookups { get; private set; }

        /// <summary>Counts one lookup and returns the dictionary's unchanged result.</summary>
        /// <param name="key">The requested dependency name.</param>
        /// <param name="value">The stored expression on success.</param>
        /// <returns>Whether the name exists.</returns>
        bool IReadOnlyDictionary<string, Expr>.TryGetValue(string key, out Expr value)
        {
            this.Lookups++;
            return this.TryGetValue(key, out value!);
        }
    }
}
