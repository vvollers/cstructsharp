namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks array limits and runtime offset assertions while resolving only a selected field.</summary>
[TestClass]
public class AddressResolutionExtentTests
{
    /// <summary>A preceding multidimensional struct array accepts its exact leaf limit and rejects one less.</summary>
    [TestMethod]
    public void PrecedingCompositeArray_EnforcesTheTotalLeafLimit()
    {
        var layout = new CStruct("struct item { uint8 value; }; struct root { item values[2][2]; uint8 tail; };");
        byte[] source = [1, 2, 3, 4, 29,];
        Assert.AreEqual(4L, layout.ResolveAddress(source, "root.tail", options: new ReadOptions { MaxArrayElements = 4, }));

        // The limit applies to four leaves, not merely the two outer rows.
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() => layout.ResolveAddress(source, "root.tail", options: new ReadOptions { MaxArrayElements = 3, }));
        StringAssert.Contains(failure.Message, "4");
        StringAssert.Contains(failure.Message, "3");
    }

    /// <summary>A zero-length runtime array leaves its following field at a valid explicit offset zero.</summary>
    [TestMethod]
    public void RuntimeOffset_ZeroRemainsValid()
    {
        var layout = new CStruct("struct root { uint8 values[count]; uint8 tail @(0); };");
        Assert.AreEqual(0L, layout.ResolveAddress(new byte[] { 23, }, "root.tail", new Dictionary<string, int> { ["count"] = 0, }));
    }

    /// <summary>A runtime negative assertion names the field and rejected value before comparing offsets.</summary>
    [TestMethod]
    public void RuntimeOffset_NegativeValueHasItsOwnDiagnostic()
    {
        var layout = new CStruct("struct root { uint8 values[count]; uint8 tail @(count - 2); };");

        // count is provided at operation time, so this assertion cannot be validated during compilation.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => layout.ResolveAddress(new byte[] { 1, 2, }, "root.tail", new Dictionary<string, int> { ["count"] = 1, }));
        StringAssert.Contains(failure.Message, "Explicit offset assertion must be non-negative: tail = -1");
    }
}
