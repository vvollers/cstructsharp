namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks array limits and runtime offset assertions while resolving only a selected field.</summary>
[TestClass]
public class AddressResolutionExtentTests
{
    /// <summary>
    ///     Runtime extent multiplication cannot wrap when selecting the field after a huge array, and selecting the
    ///     array itself needs only its start: neither reads input or allocates the array.
    /// </summary>
    [TestMethod]
    public void RuntimeExtent_RejectsOverflowBeforeReadingOrAllocating()
    {
        var layout = new CStruct("struct root { uint16 values[count]; uint8 tail; };");
        using var source = new MemoryStream();
        var variables = new Dictionary<string, int> { ["count"] = int.MaxValue, };
        var options = new ReadOptions { MaxArrayElements = int.MaxValue, };

        // The array's start does not depend on its extent.
        Assert.AreEqual(0L, layout.ResolveAddress(source, "root.values", variables, options));

        // Traversal knows the count without input; multiplying it by two to place tail must fail, not wrap.
        Assert.Throws<OverflowException>(() => layout.ResolveAddress(source, "root.tail", variables, options));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual(0L, source.Length);
    }

    /// <summary>A runtime offset mismatch reports the requested and actual byte positions and selected field.</summary>
    [TestMethod]
    public void RuntimeOffset_MismatchExplainsBothPositions()
    {
        var layout = new CStruct("struct root { uint8 values[count]; uint8 tail @(9); };");

        // The preceding runtime array puts tail at byte one, not at the asserted byte nine.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => layout.ResolveAddress(
            new byte[] { 11, 22, }, "root.tail", new Dictionary<string, int> { ["count"] = 1, }));
        StringAssert.StartsWith(failure.Message, "Field 'tail' asserts offset 9 but computed offset is 1");
    }

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
}
