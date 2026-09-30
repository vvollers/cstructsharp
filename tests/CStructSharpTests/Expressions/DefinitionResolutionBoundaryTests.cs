namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Introspection;
using CStructSharp.Syntax;
using Definition = CStructSharp.Syntax.Defines;

/// <summary>
///     Checks how <c>#define</c> values resolve: deferred capture, constants that stay expressions when they cannot be
///     evaluated statically, exact enum expressions and their shift widths, and resolution diagnostics.
/// </summary>
[TestClass]
public class DefinitionResolutionBoundaryTests
{
    /// <summary>Fresh literal overrides need no expression compilation compared with previously supplied literals.</summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void LiteralOverrides_DoNotCompileDependencyPrograms()
    {
        var resolver = new LayoutVariableResolver([], new ExpressionEvaluator(new ExpressionEvaluationLimits(256, 100_000)));
        var reused = new Dictionary<string, int>();
        var fresh = new Dictionary<string, int>();
        for (int index = 0; index < 32; index++)
        {
            reused.Add("VALUE" + index, index);
            fresh.Add("VALUE" + index, index);
        }

        // Input construction is outside the measurement. Both operations copy the same values and key shape.
        for (int index = 0; index < 32; index++)
        {
            _ = resolver.CreateIntegers(reused);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        Dictionary<string, Expr> repeatedResult = resolver.CreateIntegers(reused);
        long repeatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Dictionary<string, Expr> freshResult = resolver.CreateIntegers(fresh);
        long freshBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(32, repeatedResult.Count);
        Assert.AreEqual(32, freshResult.Count);
        Assert.IsTrue(freshBytes <= repeatedBytes + 1024, $"Fresh literals allocated {freshBytes} bytes versus {repeatedBytes}; literal overrides must not compile expression programs.");
    }

    /// <summary>Without overrides, a resolved layout needs only its isolated dictionary copy, not another evaluation session.</summary>
    /// <param name="emptyOverrides">Whether the caller supplies an empty map instead of null.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void UnchangedDefinitions_AllocateOnlyTheOperationCopy(bool emptyOverrides)
    {
        var definitions = new Definition[32];
        for (int index = 0; index < definitions.Length; index++)
        {
            definitions[index] = new Definition(new Identifier("VALUE" + index), new Literal(index));
        }

        var resolver = new LayoutVariableResolver(definitions, ExpressionEvaluator.Default);
        var baseline = (IDictionary<string, Expr>)resolver.CreateStatic();
        IReadOnlyDictionary<string, int>? supplied = emptyOverrides ? new Dictionary<string, int>() : null;

        // Both delegates allocate the required isolated dictionary; only repeated resolution adds avoidable work.
        Func<Dictionary<string, Expr>> copy = () => new LayoutVariables(baseline);
        Func<Dictionary<string, Expr>> resolve = () => resolver.CreateIntegers(supplied);
        _ = MeasureCopies(copy);
        _ = MeasureCopies(resolve);
        long copyBytes = MeasureCopies(copy);
        long resolutionBytes = MeasureCopies(resolve);
        Assert.IsTrue(resolutionBytes <= copyBytes + (64 * 128), "An unchanged baseline must not rebuild expression sessions or dependency collections for each copy.");
    }

    /// <summary>A wide exact-enum expression retains its original interpretation in every operation's variables.</summary>
    [TestMethod]
    public void ExactEnumWithoutDependencies_KeepsItsExpression()
    {
        var expression = new BinaryOp(BinaryOperatorType.Add, new Literal(Int128.MaxValue), new Literal(1));
        var resolver = new LayoutVariableResolver(
            [new Definition(new Identifier("WIDE"), expression),],
            ExpressionEvaluator.Default,
            ["WIDE",]);
        Assert.AreSame(expression, resolver.CreateStatic()["WIDE"]);
        Assert.AreSame(expression, resolver.CreateIntegers(null)["WIDE"]);
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

    /// <summary>An unused definition that fails both checked and exact evaluation retains its expression.</summary>
    [TestMethod]
    public void UnusedArithmeticFailure_RemainsDeferred()
    {
        var operand = new Literal(BigInteger.One << 200);
        var division = new BinaryOp(BinaryOperatorType.Div, new Literal(1), new Literal(0));
        var expression = new BinaryOp(BinaryOperatorType.Add, operand, division);
        var resolver = new LayoutVariableResolver(
            [new Definition(new Identifier("HUGE"), expression),],
            ExpressionEvaluator.Default);
        Assert.AreSame(expression, resolver.CreateStatic()["HUGE"]);
        Assert.AreSame(expression, resolver.CreateIntegers(null)["HUGE"]);

        // Domain evaluation rejects the literal beyond 128 bits on the left; exact evaluation reaches division by zero
        // on the right. The unused macro remains deferred, but a later consumer must still reject it.
        Assert.ThrowsExactly<InvalidOperationException>(() => ExpressionEvaluator.Default.Evaluate(resolver.CreateStatic()["HUGE"]));
    }

    /// <summary>An unused out-of-range shift stays an expression instead of being cast to a numeric constant.</summary>
    /// <param name="expression">The shift expression outside both ordinary and exact evaluation limits.</param>
    [TestMethod]
    [DataRow("1 << 128")]
    [DataRow("1 << -1")]
    public void UnevaluatedStaticDefinition_RemainsAnExpression(string expression)
    {
        var layout = new CStruct("#define UNUSED " + expression + "\nstruct root { uint8 value; };");

        Assert.AreEqual(LayoutConstantKind.Expression, layout.Constants["UNUSED"].Kind);
        Assert.IsNull(layout.Constants["UNUSED"].Value);
    }

    /// <summary>A wide intermediate shift remains invalid even when later arithmetic would produce a small value.</summary>
    /// <param name="definitions">The direct or transitive definition chain used by the enum member.</param>
    [TestMethod]
    [DataRow("#define VALUE ((1 << 127) >> 127)\n")]
    [DataRow("#define BASE ((1 << 127) >> 127)\n#define VALUE BASE\n")]
    public void DefinitionShift_UsesTheEnumWidth(string definitions)
    {
        // 1 << 127 overflows the signed 128-bit domain, so the definition keeps its expression for the enum, whose
        // exact evaluation permits shift counts only below its width: through 63 for a uint64 enum.
        Assert.Throws<CStructLayoutException>(() => new CStruct(definitions + "enum flags : uint64 { Selected = VALUE }; struct root { flags value; };"));
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
