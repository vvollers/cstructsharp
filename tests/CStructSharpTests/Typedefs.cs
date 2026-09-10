namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for typedefs so changes to this behavior are caught.</summary>
[TestClass]
public class Typedefs
{
    /// <summary>
    ///     typedef int myint gives int another name.
    /// </summary>
    /// <remarks>
    ///     The parsed alias must be myint and its target must be int. An alias introduces no additional field or
    ///     storage; later declarations can use the new name for the existing type.
    /// </remarks>
    [TestMethod]
    public void TestSimpleTypedef()
    {
        var def = (Typedef)CStructDefinitionParser.Typedef.ParseOrThrow("typedef int myint;");
        Assert.AreEqual("myint", def.Name.Name);
        Assert.AreEqual("int", def.Type.Name);
    }

    /// <summary>
    ///     The declaration defines a two-field struct tagged mystruct_t and gives it the alias mystruct.
    /// </summary>
    /// <remarks>
    ///     Both names must survive parsing, and the embedded fields must remain int a followed by int b. The alias and
    ///     the struct tag are related names, not two copies of the record's bytes.
    /// </remarks>
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
