namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for typedefs so changes to this behavior are caught.</summary>
[TestClass]
public class Typedefs
{
    /// <summary>
    ///     A typedef in C introduces an alias name for an existing type without changing memory representation. This test
    ///     verifies alias and underlying primitive are captured correctly.
    /// </summary>
    [TestMethod]
    public void TestSimpleTypedef()
    {
        var def = (Typedef)CStructDefinitionParser.Typedef.ParseOrThrow("typedef int myint;");
        Assert.AreEqual("myint", def.Name.Name);
        Assert.AreEqual("int", def.Type.Name);
    }

    /// <summary>
    ///     Tests typedef struct syntax where a struct definition and public alias are declared together. It validates that
    ///     both the inner struct layout and the exported typedef name are preserved.
    /// </summary>
    [TestMethod]
    public void TestTypedefStruct()
    {
        var def = (Typedef)CStructDefinitionParser.Typedefstruct.ParseOrThrow(
                                                                              "typedef struct mystruct_t { int a; int b; } mystruct;");
        Assert.AreEqual("mystruct", def.Name.Name);
        Assert.AreEqual("struct", def.Type.Name);
        Assert.IsNotNull(def.Struct);
        Assert.AreEqual("mystruct_t", def.Struct.Name.Name);
        Assert.HasCount(2, def.Struct.Fields);
        Assert.AreEqual("a", def.Struct.Fields[0].Name.Name);
        Assert.AreEqual("int", def.Struct.Fields[0].Type.Name);
        Assert.AreEqual("b", def.Struct.Fields[1].Name.Name);
        Assert.AreEqual("int", def.Struct.Fields[1].Type.Name);
    }
}
