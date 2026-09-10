namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="CompiledSizeQueries"/> directly against a real compiled layout. Only reachable
///     indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c> partial
///     class. Also proves the type is usable purely from its own constructor inputs - the same shape <c>CStruct</c>
///     relies on to answer a union member's fixed-storage check mid-construction, before its own compiled model
///     exists.
/// </summary>
[TestClass]
public class CompiledSizeQueriesTests
{
    private const string Layout = """
                                  struct inner { uint8 a; uint8 b; };
                                  union choice { uint16 wide; inner nested; };
                                  struct root {
                                      uint8 count;
                                      uint8 values[count];
                                      inner payload;
                                  };
                                  """;

    private static (CompiledSizeQueries Queries, CStruct CStruct) CreateQueries(bool aligned = false)
    {
        var cstruct = new CStruct(Layout, aligned: aligned);
        var evaluator = new LayoutExpressionEvaluator(new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000)));
        return (new CompiledSizeQueries(cstruct.CompiledModel.Composites, aligned, evaluator), cstruct);
    }

    /// <summary>A known struct declaration resolves to its bound composite descriptor.</summary>
    [TestMethod]
    public void GetCompiledComposite_KnownStruct_ReturnsBoundDescriptor()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct inner = cstruct.GetStruct("inner");

        CompiledCompositeType composite = queries.GetCompiledComposite(inner);

        Assert.AreEqual(2, composite.Fields.Length);
    }

    /// <summary>A fixed-size struct's total extent is the sum of its fields' storage.</summary>
    [TestMethod]
    public void GetCompiledStructSizeInBytes_FixedSizeStruct_SumsFieldStorage()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct inner = cstruct.GetStruct("inner");
        CompiledCompositeType composite = queries.GetCompiledComposite(inner);

        int size = queries.GetCompiledStructSizeInBytes(composite, new Dictionary<string, Expr>(), true);

        Assert.AreEqual(2, size);
    }

    /// <summary>A union's total extent is its largest member's storage, not the sum of all members.</summary>
    [TestMethod]
    public void GetCompiledStructSizeInBytes_Union_UsesLargestMember()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct choice = cstruct.GetStruct("choice");
        CompiledCompositeType composite = queries.GetCompiledComposite(choice);

        int size = queries.GetCompiledStructSizeInBytes(composite, new Dictionary<string, Expr>(), true);

        Assert.AreEqual(2, size);
    }

    /// <summary>Aligned mode rounds a composite's total extent up to its own alignment boundary.</summary>
    [TestMethod]
    public void GetCompiledStructSizeInBytes_AlignedMode_RoundsUpToAlignment()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries(aligned: true);
        Struct choice = cstruct.GetStruct("choice");
        CompiledCompositeType composite = queries.GetCompiledComposite(choice);

        int size = queries.GetCompiledStructSizeInBytes(composite, new Dictionary<string, Expr>(), true);

        Assert.AreEqual(2, size);
        Assert.AreEqual(0, size % composite.Symbol.Alignment);
    }

    /// <summary>An empty composite has zero storage.</summary>
    [TestMethod]
    public void GetCompiledStructSizeInBytes_NoFields_ReturnsZero()
    {
        var cstruct = new CStruct("struct empty { };");
        var evaluator = new LayoutExpressionEvaluator(new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000)));
        var queries = new CompiledSizeQueries(cstruct.CompiledModel.Composites, false, evaluator);
        Struct empty = cstruct.GetStruct("empty");

        int size = queries.GetCompiledStructSizeInBytes(queries.GetCompiledComposite(empty), new Dictionary<string, Expr>(), true);

        Assert.AreEqual(0, size);
    }

    /// <summary>A field's storage is its element size multiplied by its element count.</summary>
    [TestMethod]
    public void GetCompiledFieldStorageSize_ScalarField_ReturnsElementSize()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct inner = cstruct.GetStruct("inner");
        CompiledField field = queries.GetCompiledComposite(inner).FieldsByName["a"];

        int size = queries.GetCompiledFieldStorageSize(field, new Dictionary<string, Expr>(), true);

        Assert.AreEqual(1, size);
    }

    /// <summary>A field whose element type is a nested struct recurses through the composite size calculation.</summary>
    [TestMethod]
    public void GetCompiledFieldElementSize_NestedStructField_RecursesIntoCompositeSize()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct root = cstruct.GetStruct("root");
        CompiledField payload = queries.GetCompiledComposite(root).FieldsByName["payload"];

        int elementSize = queries.GetCompiledFieldElementSize(payload, new Dictionary<string, Expr>(), true);

        Assert.AreEqual(2, elementSize);
    }

    /// <summary>A scalar field always occupies exactly one element.</summary>
    [TestMethod]
    public void GetCompiledArrayCount_ScalarField_ReturnsOne()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct inner = cstruct.GetStruct("inner");
        CompiledField field = queries.GetCompiledComposite(inner).FieldsByName["a"];

        Assert.AreEqual(1, queries.GetCompiledArrayCount(field, new Dictionary<string, Expr>(), true));
    }

    /// <summary>A flexible (unsized) array field has no fixed count to report.</summary>
    [TestMethod]
    public void GetCompiledArrayCount_FlexibleArray_Throws()
    {
        var cstruct = new CStruct("struct root { char name[]; };");
        var evaluator = new LayoutExpressionEvaluator(new ExpressionEvaluator(new ExpressionEvaluationLimits(64, 10_000)));
        var queries = new CompiledSizeQueries(cstruct.CompiledModel.Composites, false, evaluator);
        Struct root = cstruct.GetStruct("root");
        CompiledField name = queries.GetCompiledComposite(root).FieldsByName["name"];

        Assert.Throws<CStructLayoutException>(
            () => queries.GetCompiledArrayCount(name, new Dictionary<string, Expr>(), true));
    }

    /// <summary>A runtime-counted array field evaluates its count expression against the supplied variables.</summary>
    [TestMethod]
    public void GetCompiledArrayCount_RuntimeCountedArray_EvaluatesAgainstSuppliedVariables()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct root = cstruct.GetStruct("root");
        CompiledField values = queries.GetCompiledComposite(root).FieldsByName["values"];
        var variables = new Dictionary<string, Expr> { ["count"] = new Literal(5), };

        int count = queries.GetCompiledArrayCount(values, variables, true);

        Assert.AreEqual(5, count);
    }

    /// <summary>An unresolvable runtime count is wrapped with field context when a fixed size is required.</summary>
    [TestMethod]
    public void GetCompiledArrayCount_RequireFixedSizeWithUnresolvedVariable_ThrowsWithContext()
    {
        (CompiledSizeQueries queries, CStruct cstruct) = CreateQueries();
        Struct root = cstruct.GetStruct("root");
        CompiledField values = queries.GetCompiledComposite(root).FieldsByName["values"];

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => queries.GetCompiledArrayCount(values, new Dictionary<string, Expr>(), true));

        StringAssert.Contains(exception.Message, "values");
    }
}
