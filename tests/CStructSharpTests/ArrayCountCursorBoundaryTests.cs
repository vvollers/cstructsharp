namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks prerequisite array counting restores the input cursor before a selected read fails.</summary>
[TestClass]
public class ArrayCountCursorBoundaryTests
{
    /// <summary>A terminated-array scan cannot leave a failed selection positioned after its speculative reads.</summary>
    /// <param name="limited">Whether counting exceeds the element limit instead of selecting an invalid index.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedSelection_RestoresTheCountScanOrigin(bool limited)
    {
        var layout = new CStruct("struct root { uint8 prefix[2]; uint8 items[]; };");
        using var source = new MemoryStream(new byte[] { 0xEE, 0xAA, 0xBB, 7, 8, 0, });
        source.Position = 1;

        // Counting is a prerequisite query, not consumption of the selected value.
        CStructException error = limited
            ? Assert.Throws<CStructReadLimitException>(() => layout.ReadValue(source, "root.items[0]", options: new ReadOptions { MaxArrayElements = 1, }))
            : Assert.Throws<CStructPathException>(() => layout.ReadValue(source, "root.items[3]"));
        Assert.AreEqual(1L, source.Position);
        Assert.AreEqual(1L, error.Offset);
    }
}
