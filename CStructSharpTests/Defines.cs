namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for defines so changes to this behavior are caught.</summary>
[TestClass]
public class Defines
{
    /// <summary>
    ///     The definition gives a name to 2 + myvariable * 4.
    /// </summary>
    /// <remarks>
    ///     Supplying myvariable = 5 must produce 22, because multiplication happens before addition. Such expressions
    ///     can later determine array lengths; this test checks the expression itself without reading binary data.
    /// </remarks>
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
    ///     Parsing #define ABC 123 must retain the name ABC and the number 123.
    /// </summary>
    /// <remarks>
    ///     A define supplies a reusable constant; it does not add a field or occupy bytes in a binary record.
    /// </remarks>
    [TestMethod]
    public void TestSimpleDefine()
    {
        var test1 = (Structure.Defines)CStructDefinitionParser.Define.ParseOrThrow("#define ABC 123");
        Assert.AreEqual("ABC", test1.Name.Name);
        Assert.AreEqual(123, test1.Value.Calc());
    }
}
