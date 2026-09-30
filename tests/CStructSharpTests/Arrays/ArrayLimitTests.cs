namespace CStructSharp.Tests;

using System.Dynamic;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>
///     Verifies that array work limits and declared-count mismatches fail through the operation-specific exception
///     types, that a count the input cannot back fails before its array is allocated, and that a failed selection
///     restores the count scan's starting position.
/// </summary>
[TestClass]
public class ArrayLimitTests
{
    private const string Layout = "struct p { uint32 n; uint64 data[n]; };";

    /// <summary>
    ///     values[2] requires exactly two elements.
    /// </summary>
    /// <remarks>
    ///     A write limit of one must reject even a correctly shaped value, and supplying only one element must fail
    ///     even with the default limit. Reading with a restrictive array budget must use the read-limit exception,
    ///     distinguishing excessive read work from invalid write input.
    /// </remarks>
    [TestMethod]
    public void ArrayLimits_AreExplicitAndOperationSpecific()
    {
        var cstruct = new CStruct("struct root { byte values[2]; };");
        dynamic data = new ExpandoObject();
        data.values = new List<object> { (byte)1, (byte)2, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize("root", data, options: new WriteOptions { MaxArrayElements = 1, }));

        data.values = new List<object> { (byte)1, };
        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", data));

        using var readStream = new MemoryStream([0x2A,]);
        Assert.Throws<CStructReadLimitException>(
            () => cstruct.Parse(
                readStream,
                "root",
                new Dictionary<string, int>(),
                new ReadOptions { MaxArrayElements = 1, }));
    }

    /// <summary>A memory-backed read with a huge count allocates almost nothing before failing.</summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
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
