namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="CompiledModelQueries"/> directly against a real compiled layout. Only reachable
///     indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c> partial
///     class.
/// </summary>
[TestClass]
public class CompiledModelQueriesTests
{
    private const string Layout = """
                                  enum mode : uint8 { One=1, Two=2 };
                                  typedef struct payload { uint8 value; } payload_t;
                                  struct root { uint8 count; };
                                  """;

    private static (CompiledModelQueries Queries, CStruct CStruct) CreateQueries()
    {
        var cstruct = new CStruct(Layout);
        return (new CompiledModelQueries(cstruct.CompiledModel), cstruct);
    }

    /// <summary>A typedef root resolves to its compiled synthetic field projection.</summary>
    [TestMethod]
    public void GetCompiledRootField_KnownTypedef_ReturnsCompiledField()
    {
        (CompiledModelQueries queries, CStruct cstruct) = CreateQueries();
        CStructElement declaration = cstruct.CompiledModel.Declarations["payload_t"];

        CompiledField field = queries.GetCompiledRootField(declaration);

        Assert.AreEqual("payload", field.NamedElement!.Name.Name);
    }

    /// <summary>A declaration with no root field projection reports it cannot be resolved.</summary>
    [TestMethod]
    public void GetCompiledRootField_DeclarationWithNoProjection_Throws()
    {
        (CompiledModelQueries queries, CStruct cstruct) = CreateQueries();
        CStructElement declaration = cstruct.CompiledModel.Declarations["root"];

        Assert.Throws<InvalidOperationException>(() => queries.GetCompiledRootField(declaration));
    }

    /// <summary>A known enum resolves to its compiled integer domain.</summary>
    [TestMethod]
    public void GetCompiledEnum_KnownEnum_ReturnsCompiledType()
    {
        (CompiledModelQueries queries, CStruct cstruct) = CreateQueries();
        var enm = (CStructSharp.Structure.Enum)cstruct.CompiledModel.Declarations["mode"];

        CompiledEnumType compiled = queries.GetCompiledEnum(enm);

        Assert.AreEqual("uint8", compiled.Underlying.TerminalName);
    }

    /// <summary>A known exported name is found in the compiled declaration snapshot.</summary>
    [TestMethod]
    public void TryGetCompiledDeclaration_KnownName_ReturnsTrue()
    {
        (CompiledModelQueries queries, _) = CreateQueries();

        Assert.IsTrue(queries.TryGetCompiledDeclaration("root", out CStructElement? declaration));
        Assert.AreEqual("root", declaration!.Name.Name);
    }

    /// <summary>An unknown name is reported as not found rather than throwing.</summary>
    [TestMethod]
    public void TryGetCompiledDeclaration_UnknownName_ReturnsFalse()
    {
        (CompiledModelQueries queries, _) = CreateQueries();

        Assert.IsFalse(queries.TryGetCompiledDeclaration("missing", out _));
    }

    /// <summary>The first exported struct in source order is returned for convenience overloads.</summary>
    [TestMethod]
    public void GetFirstCompiledStructName_LayoutWithStruct_ReturnsItsName()
    {
        (CompiledModelQueries queries, _) = CreateQueries();

        Assert.AreEqual("root", queries.GetFirstCompiledStructName());
    }

    /// <summary>A layout with no struct declaration has no convenience root to select.</summary>
    [TestMethod]
    public void GetFirstCompiledStructName_NoStructDeclared_Throws()
    {
        var cstruct = new CStruct("enum mode : uint8 { One=1 };");
        var queries = new CompiledModelQueries(cstruct.CompiledModel);

        Assert.Throws<CStructLayoutException>(() => queries.GetFirstCompiledStructName());
    }

    /// <summary>A struct or enum declaration resolves to itself.</summary>
    [TestMethod]
    public void ResolveCompiledNamedElement_StructOrEnum_ReturnsTheDeclarationItself()
    {
        (CompiledModelQueries queries, CStruct cstruct) = CreateQueries();
        CStructElement root = cstruct.CompiledModel.Declarations["root"];

        Assert.AreSame(root, queries.ResolveCompiledNamedElement(root));
    }

    /// <summary>A typedef aliasing a struct resolves to that struct's declaration.</summary>
    [TestMethod]
    public void ResolveCompiledNamedElement_TypedefToStruct_ReturnsTheAliasedStruct()
    {
        (CompiledModelQueries queries, CStruct cstruct) = CreateQueries();
        CStructElement typedef = cstruct.CompiledModel.Declarations["payload_t"];

        CStructElement? resolved = queries.ResolveCompiledNamedElement(typedef);

        Assert.AreEqual("payload", resolved!.Name.Name);
    }

    /// <summary>A primitive-scalar declaration has no named type projection.</summary>
    [TestMethod]
    public void ResolveCompiledNamedElement_TypedefToPrimitive_ReturnsNull()
    {
        var cstruct = new CStruct("typedef uint32 count_t;");
        var queries = new CompiledModelQueries(cstruct.CompiledModel);
        CStructElement typedef = cstruct.CompiledModel.Declarations["count_t"];

        Assert.IsNull(queries.ResolveCompiledNamedElement(typedef));
    }
}
