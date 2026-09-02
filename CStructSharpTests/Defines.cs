namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for defines so changes to this behavior are caught.</summary>
[TestClass]
public class Defines
{
    /// <summary>
    ///     C-style defines often encode arithmetic over previously declared symbols to drive sizes and offsets. This test
    ///     verifies expression evaluation with identifier substitution, not just literal constants.
    /// </summary>
    [TestMethod]
    public void TestMoreComplexDefine()
    {
        var test1 = (Structure.Defines)CStructDefinitionParser.Define.ParseOrThrow(
         "#define SUPERCOMPLEX_VAR6 2+myvariable*4");
        Assert.AreEqual("SUPERCOMPLEX_VAR6", test1.Name.Name);
        Dictionary<string, Expr> variables = new();
        variables["myvariable"] = new Literal(5);
        Assert.AreEqual(22, test1.Value.Calc(variables));
    }

    /// <summary>
    ///     Parses the basic #define pattern of symbolic name plus literal value. This is the foundation for constant-driven C
    ///     struct declarations and array lengths.
    /// </summary>
    [TestMethod]
    public void TestSimpleDefine()
    {
        var test1 = (Structure.Defines)CStructDefinitionParser.Define.ParseOrThrow("#define ABC 123");
        Assert.AreEqual("ABC", test1.Name.Name);
        Assert.AreEqual(123, test1.Value.Calc());
    }
}
