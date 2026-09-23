namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks array block optimizations preserve the failure position of nested element limits.</summary>
[TestClass]
public class RecordArrayLimitPositionTests
{
    /// <summary>An inner array limit fails after the preceding scalar, without consuming the remaining record block.</summary>
    [TestMethod]
    public void NestedArrayLimit_PreservesTheGeneralReadPosition()
    {
        var layout = new CStruct("struct item { uint8 prefix; uint8 values[2]; }; struct root { uint8 count; item items[count]; };");
        byte[] bytes = [1, 0x42, 0xAA, 0xBB,];

        // Expose the buffer so the reader can attempt its whole-array span optimization.
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        CStructReadLimitException error = Assert.Throws<CStructReadLimitException>(() =>
            layout.Parse(source, "root", options: new ReadOptions { MaxArrayElements = 1, }));
        Assert.AreEqual(2L, source.Position);
        Assert.AreEqual(2L, error.Offset);
    }
}
