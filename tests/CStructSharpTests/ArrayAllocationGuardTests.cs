namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     A count that the input provably cannot back fails before the element array is allocated, so a hostile
///     length prefix in a tiny input costs a comparison rather than a multi-megabyte allocation.
/// </summary>
[TestClass]
public class ArrayAllocationGuardTests
{
    private const string Layout = "struct p { uint32 n; uint64 data[n]; };";

    /// <summary>A memory-backed read with a huge count allocates almost nothing before failing.</summary>
    [TestMethod]
    public void HugeCountOnShortMemory_FailsBeforeAllocating()
    {
        var layout = new CStruct(Layout);
        byte[] bytes = [0x40, 0x42, 0x0F, 0x00, 1, 2, 3, 4, 5, 6, 7, 8,]; // n = 1,000,000, one element present

        layout.Parse(new byte[] { 1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, }, "p"); // warm the code path
        long before = GC.GetAllocatedBytesForCurrentThread();
        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "p"));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        StringAssert.Contains(exception.Message, "Not enough bytes");
        Assert.AreEqual(CStructErrorCode.ReadFailed, exception.Code);
        Assert.IsTrue(allocated < 64 * 1024, $"allocated {allocated} bytes for a count the input cannot back");
    }

    /// <summary>A seekable stream is checked the same way and ends at the same position a short read would leave.</summary>
    [TestMethod]
    public void HugeCountOnShortSeekableStream_FailsAtEnd()
    {
        var layout = new CStruct(Layout);
        using var stream = new MemoryStream([0x40, 0x42, 0x0F, 0x00, 1, 2, 3, 4, 5, 6, 7, 8,]);

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(stream, "p"));

        StringAssert.Contains(exception.Message, "Not enough bytes");
        Assert.AreEqual(stream.Length, stream.Position);
    }

    /// <summary>An exact count still reads every element.</summary>
    [TestMethod]
    public void ExactCount_Reads()
    {
        var layout = new CStruct(Layout);
        dynamic value = layout.Parse(new byte[] { 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, }, "p");

        Assert.AreEqual(2, ((IList<object?>)value.data).Count);
    }
}
