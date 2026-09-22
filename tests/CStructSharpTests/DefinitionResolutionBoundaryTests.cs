namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using Definition = CStructSharp.Syntax.Defines;

/// <summary>Checks deferred definition capture, exact enum expressions and resolution diagnostics.</summary>
[TestClass]
public class DefinitionResolutionBoundaryTests
{
    /// <summary>Without overrides, a resolved layout needs only its isolated dictionary copy, not another evaluation session.</summary>
    /// <param name="emptyOverrides">Whether the caller supplies an empty map instead of null.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnchangedDefinitions_AllocateOnlyTheOperationCopy(bool emptyOverrides)
    {
        var definitions = new Definition[32];
        for (int index = 0; index < definitions.Length; index++)
        {
            definitions[index] = new Definition(new Identifier("VALUE" + index), new Literal(index));
        }

        var resolver = new LayoutVariableResolver(definitions, ExpressionEvaluator.Default);
        var baseline = (IDictionary<string, Expr>)resolver.CreateStatic();
        IReadOnlyDictionary<string, Expr>? supplied = emptyOverrides ? new Dictionary<string, Expr>() : null;

        // Both delegates allocate the required isolated dictionary; only repeated resolution adds avoidable work.
        Func<Dictionary<string, Expr>> copy = () => new LayoutVariables(baseline);
        Func<Dictionary<string, Expr>> resolve = () => resolver.Create(supplied);
        _ = MeasureCopies(copy);
        _ = MeasureCopies(resolve);
        long copyBytes = MeasureCopies(copy);
        long resolutionBytes = MeasureCopies(resolve);
        Assert.IsTrue(resolutionBytes <= copyBytes + (64 * 128), "An unchanged baseline must not rebuild expression sessions or dependency collections for each copy.");
    }

    /// <summary>An override of an existing definition may defer its field dependency and must enable full field capture.</summary>
    [TestMethod]
    public void DeferredDefinitionOverride_CapturesFieldValues()
    {
        var resolver = new LayoutVariableResolver(
            [new Definition(new Identifier("COUNT"), new Literal(1)),],
            ExpressionEvaluator.Default);
        var expression = new Identifier("laterField");
        Dictionary<string, Expr> resolved = resolver.Create(new Dictionary<string, Expr> { ["COUNT"] = expression, });
        Assert.IsInstanceOfType<LayoutVariables>(resolved);
        Assert.IsTrue(((LayoutVariables)resolved).CaptureAll);
        Assert.AreSame(expression, resolved["COUNT"]);
        Assert.AreEqual(1, resolver.CreateStatic()["COUNT"].Value);
    }

    /// <summary>A wide exact-enum expression retains its original interpretation without capturing unrelated fields.</summary>
    [TestMethod]
    public void ExactEnumWithoutDependencies_DoesNotCaptureFields()
    {
        var expression = new BinaryOp(BinaryOperatorType.Add, new Literal(int.MaxValue), new Literal(1));
        var resolver = new LayoutVariableResolver(
            [new Definition(new Identifier("WIDE"), expression),],
            ExpressionEvaluator.Default,
            ["WIDE",]);
        Assert.AreSame(expression, resolver.CreateStatic()["WIDE"]);
        Dictionary<string, Expr> resolved = resolver.Create(new Dictionary<string, Expr> { ["WIDE"] = expression, });
        Assert.AreSame(expression, resolved["WIDE"]);
        Assert.IsFalse(((LayoutVariables)resolved).CaptureAll);
    }

    /// <summary>Compile-time arithmetic failures keep the layout-resolution prefix and original cause.</summary>
    [TestMethod]
    public void StaticArithmeticFailure_IdentifiesLayoutResolution()
    {
        var expression = new BinaryOp(BinaryOperatorType.Div, new Literal(1), new Literal(0));

        // The failing expression is wholly static, so construction diagnoses it before any operation starts.
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new LayoutVariableResolver(
            [new Definition(new Identifier("BAD"), expression),],
            ExpressionEvaluator.Default));
        StringAssert.StartsWith(failure.Message, "Layout expression could not be resolved: ");
        Assert.IsInstanceOfType<DivideByZeroException>(failure.InnerException);
    }

    /// <summary>An invalid caller expression has the same useful resolution context as a static definition failure.</summary>
    [TestMethod]
    public void SuppliedArithmeticFailure_IdentifiesLayoutResolution()
    {
        var resolver = new LayoutVariableResolver([], ExpressionEvaluator.Default);
        var expression = new BinaryOp(BinaryOperatorType.Div, new Literal(1), new Literal(0));

        // Unlike a deferred layout definition, an unknown caller variable must resolve at operation entry.
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => resolver.Create(
            new Dictionary<string, Expr> { ["BAD"] = expression, }));
        StringAssert.StartsWith(failure.Message, "Layout expression could not be resolved: ");
        Assert.IsInstanceOfType<DivideByZeroException>(failure.InnerException);
    }

    /// <summary>A cycle diagnostic names an unresolved definition, not an earlier independent constant.</summary>
    [TestMethod]
    public void CycleDiagnostic_NamesTheUnresolvedDefinition()
    {
        Definition[] definitions =
        [
            new(new Identifier("READY"), new Literal(7)),
            new(new Identifier("FIRST"), new Identifier("SECOND")),
            new(new Identifier("SECOND"), new Identifier("FIRST")),
        ];

        // READY has no dependencies and is removed from the pending graph before the cycle is reported.
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new LayoutVariableResolver(definitions, ExpressionEvaluator.Default));
        Assert.AreEqual("Circular expression dependency detected at: FIRST", failure.Message);
    }

    /// <summary>A caller's overflowing expression is rejected rather than retained as an unevaluated layout definition.</summary>
    [TestMethod]
    public void SuppliedOverflow_IsNotDeferredAsADefinition()
    {
        var resolver = new LayoutVariableResolver([], ExpressionEvaluator.Default);
        var expression = new BinaryOp(BinaryOperatorType.Add, new Literal(int.MaxValue), new Literal(1));

        // Only declared definitions have the exact-value fallback; caller variables must be valid Int32 values.
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => resolver.Create(
            new Dictionary<string, Expr> { ["BAD"] = expression, }));
        StringAssert.StartsWith(failure.Message, "Layout expression could not be resolved: ");
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
    }

    /// <summary>An unused definition that fails both checked and exact evaluation retains its expression.</summary>
    [TestMethod]
    public void UnusedArithmeticFailure_RemainsDeferred()
    {
        var operand = new Literal(BigInteger.One << 100);
        var division = new BinaryOp(BinaryOperatorType.Div, new Literal(1), new Literal(0));
        var expression = new BinaryOp(BinaryOperatorType.Add, operand, division);
        var resolver = new LayoutVariableResolver(
            [new Definition(new Identifier("HUGE"), expression),],
            ExpressionEvaluator.Default);
        Assert.AreSame(expression, resolver.CreateStatic()["HUGE"]);
        Assert.AreSame(expression, resolver.Create(null)["HUGE"]);

        // Int32 evaluation overflows on the left; exact evaluation reaches division by zero on the right.
        // The unused macro remains deferred, but a later Int32 consumer must still reject it.
        Assert.ThrowsExactly<OverflowException>(() => ExpressionEvaluator.Default.Evaluate(resolver.CreateStatic()["HUGE"]));
    }

    /// <summary>Measures current-thread allocation for 128 retained-through-call dictionary results.</summary>
    /// <param name="create">The already-created copy or resolution delegate.</param>
    /// <returns>Total allocated bytes, excluding construction of the delegate itself.</returns>
    private static long MeasureCopies(Func<Dictionary<string, Expr>> create)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 128; index++)
        {
            GC.KeepAlive(create());
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
