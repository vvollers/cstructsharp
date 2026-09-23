namespace CStructSharp.Tests;

using CStructSharp.Introspection;

/// <summary>Checks constants that remain expressions because their static evaluation cannot produce a value.</summary>
[TestClass]
public class DeferredConstantBoundaryTests
{
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
}
