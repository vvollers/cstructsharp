namespace CStructSharp.Tests;

/// <summary>Checks that an array-element budget does not prohibit scalar field traversal.</summary>
[TestClass]
public class ZeroArrayBudgetScalarTraversalTests
{
    /// <summary>Fixed primitives, variable-width integers and nested scalar records need no array-element allowance.</summary>
    /// <param name="prefix">A scalar declaration occupying the first two input bytes.</param>
    [TestMethod]
    [DataRow("uint16 first;")]
    [DataRow("uleb128_32 first;")]
    [DataRow("struct { uint8 low; uint8 high; } first;")]
    public void ScalarPrefix_CanBeMeasuredWithNoArrayAllowance(string prefix)
    {
        var layout = new CStruct("struct root { " + prefix + " uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 0x81, 0x01, 99, });
        var options = new ReadOptions { MaxArrayElements = 0, };

        Assert.AreEqual(2L, layout.ResolveAddress(source, "root.tail", options: options));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail", options: options));
    }
}
