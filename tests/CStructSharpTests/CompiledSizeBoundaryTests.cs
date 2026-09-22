namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>Checks compiled size arithmetic and failures without allocating an output of the requested size.</summary>
[TestClass]
public class CompiledSizeBoundaryTests
{
    /// <summary>Missing conditional data is a layout error for fixed queries and a read error for live queries.</summary>
    [TestMethod]
    public void ConditionalFailure_UsesTheQueryErrorDomain()
    {
        var layout = new CStruct("struct root { uint8 tag; if (tag) { uint8 value; } };");
        CompiledSizeQueries queries = Queries(layout);
        CompiledCompositeType root = queries.GetCompiledComposite(layout.GetStruct("root"));
        var missing = new Dictionary<string, Expr>();

        // A fixed query has no input from which to obtain the condition's missing tag.
        CStructLayoutException fixedFailure = Assert.Throws<CStructLayoutException>(() => queries.GetCompiledStructSizeInBytes(root, missing, true));
        StringAssert.Contains(fixedFailure.Message, "tag");

        // During a variables-driven query the same missing tag is a data-read failure.
        CStructReadException liveFailure = Assert.Throws<CStructReadException>(() => queries.GetCompiledStructSizeInBytes(root, missing, false));
        StringAssert.Contains(liveFailure.Message, "tag");
    }

    /// <summary>A nested runtime-sized field obtains its full element extent from supplied variables.</summary>
    [TestMethod]
    public void DynamicNestedElement_RecursesWithTheCurrentVariables()
    {
        var layout = new CStruct("struct inner { uint8 count; uint16 values[count]; }; struct root { inner payload; };");
        CompiledSizeQueries queries = Queries(layout);
        CompiledField field = queries.GetCompiledComposite(layout.GetStruct("root")).FieldsByName["payload"];
        var variables = new Dictionary<string, Expr> { ["count"] = new Literal(3), };
        Assert.AreEqual(7, queries.GetCompiledFieldElementSize(field, variables, false));
        Assert.AreEqual(7, queries.GetCompiledFieldStorageSize(field, variables, false));
    }

    /// <summary>Runtime element counts cannot wrap a multiplied storage extent into a small positive size.</summary>
    [TestMethod]
    public void RuntimeStorageExtent_RejectsIntegerOverflow()
    {
        var layout = new CStruct("struct root { uint32 count; uint16 values[count]; };");
        CompiledSizeQueries queries = Queries(layout);
        CompiledField field = queries.GetCompiledComposite(layout.GetStruct("root")).FieldsByName["values"];
        var variables = new Dictionary<string, Expr> { ["count"] = new Literal(int.MaxValue), };

        // The low-level arithmetic rejects the extent before any reader or writer can allocate it.
        Assert.Throws<OverflowException>(() => queries.GetCompiledFieldStorageSize(field, variables, false));
    }

    /// <summary>Every input-terminated array strategy refuses to invent a count without its input bytes.</summary>
    /// <param name="declaration">One flexible, zero-terminated or end-of-input array field.</param>
    [TestMethod]
    [DataRow("char values[];")]
    [DataRow("uint8 values[];")]
    [DataRow("uint8 values[EOF];")]
    public void InputTerminatedCounts_ReportWhyNoFixedSizeExists(string declaration)
    {
        var layout = new CStruct("struct root { " + declaration + " };");
        CompiledSizeQueries queries = Queries(layout);
        CompiledField field = queries.GetCompiledComposite(layout.GetStruct("root")).FieldsByName["values"];
        var variables = new Dictionary<string, Expr>();

        // Both count entrypoints must reject the strategy before trying to evaluate its marker expression.
        CStructLayoutException count = Assert.Throws<CStructLayoutException>(() => queries.GetCompiledArrayCount(field, variables, true));
        Assert.AreEqual("Flexible array has no fixed storage size: values", count.Message);

        // Multiplying dimensions must preserve the same explanation as the outer-count query.
        CStructLayoutException total = Assert.Throws<CStructLayoutException>(() => queries.GetCompiledFieldTotalElementCount(field, variables, true));
        Assert.AreEqual(count.Message, total.Message);
    }

    /// <summary>A variable-width text primitive has no fixed per-element footprint.</summary>
    [TestMethod]
    public void VariableWidthElement_IdentifiesItsType()
    {
        var layout = new CStruct("struct root { cstring name; };");
        CompiledSizeQueries queries = Queries(layout);
        CompiledField field = queries.GetCompiledComposite(layout.GetStruct("root")).FieldsByName["name"];

        // A zero-terminated string's length cannot be derived from its type alone.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => queries.GetCompiledFieldElementSize(field, new Dictionary<string, Expr>(), true));

        // Compiled diagnostics use the canonical primitive spelling after resolving the cstring alias.
        Assert.AreEqual("Variable-length type has no fixed storage size: ascii_string_zero", failure.Message);
    }

    /// <summary>An unbound predeclared symbol is rejected while the compiled model is being constructed.</summary>
    [TestMethod]
    public void UnboundComposite_IdentifiesTheIncompleteDeclaration()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        Struct declaration = layout.GetStruct("root");
        var symbols = new Dictionary<Struct, CompiledTypeSymbol> { [declaration] = CompiledTypeSymbol.PredeclareComposite(declaration), };
        var evaluator = new LayoutExpressionEvaluator(new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000)));
        var queries = new CompiledSizeQueries(symbols, false, BitfieldPacking.SysV, false, evaluator);

        // This models the construction phase before fields and placement have been bound to their symbol.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => queries.GetCompiledComposite(declaration));
        Assert.AreEqual("Composite type is not bound: root", failure.Message);
    }

    /// <summary>Creates packed-layout size queries against the supplied immutable compiled model.</summary>
    /// <param name="layout">The layout whose symbols resolve nested field types.</param>
    /// <returns>A variables-only size calculator using the normal bounded expression evaluator.</returns>
    private static CompiledSizeQueries Queries(CStruct layout)
    {
        var evaluator = new LayoutExpressionEvaluator(new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000)));
        return new CompiledSizeQueries(layout.CompiledModel.Composites, false, BitfieldPacking.SysV, false, evaluator);
    }
}
