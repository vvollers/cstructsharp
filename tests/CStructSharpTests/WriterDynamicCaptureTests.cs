namespace CStructSharp.Tests;

using CStructSharp.Syntax;

/// <summary>Checks writer capture when internal expression overrides refer to otherwise unreferenced fields.</summary>
[TestClass]
public class WriterDynamicCaptureTests
{
    /// <summary>A deferred definition override can use a field that the original layout did not reference.</summary>
    [TestMethod]
    public void DeferredDefinition_CapturesItsRuntimeField()
    {
        var layout = new CStruct("#define COUNT 1\nstruct root { uint8 count; uint8 values[COUNT]; };");
        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)2,
            ["values"] = new byte[] { 11, 12, },
        };
        var variables = new Dictionary<string, Expr> { ["COUNT"] = new Identifier("count"), };

        CollectionAssert.AreEqual(new byte[] { 2, 11, 12, }, layout.Serialize("root", data, variables));
    }
}
