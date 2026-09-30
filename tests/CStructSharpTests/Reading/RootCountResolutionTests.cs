namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Checks how a read of a bare root array takes its count: a terminated root is scanned once, by the read itself, and
///     charged once; a multidimensional root's outermost count is checked against the element limit before the total is.
/// </summary>
[TestClass]
public class RootCountResolutionTests
{
    /// <summary>
    ///     Reading <c>uint8[]</c> over <c>01 02 00</c> needs the budget a parse of <c>struct one { uint8 a[]; }</c> needs over
    ///     the same bytes, its three bytes; one byte less fails at the scan, which reports the array's start.
    /// </summary>
    [TestMethod]
    public void TerminatedRoot_ChargesItsScanOnce()
    {
        var layout = new CStruct("struct one { uint8 a[]; };");
        byte[] data = [1, 2, 0, 9];
        var enough = new ReadOptions { MaxTotalBytesRead = 3, };

        _ = layout.Parse(data, "one", options: enough);
        CollectionAssert.AreEqual(new byte[] { 1, 2, }, ((IEnumerable<object?>)layout.ReadValue(data, "uint8[]", options: enough)!).Cast<byte>().ToArray());
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() => layout.ReadValue(data, "uint8[]", options: new ReadOptions { MaxTotalBytesRead = 2, }));
        Assert.AreEqual(0L, failure.Offset);
    }

    /// <summary>
    ///     A multidimensional root's outermost count (2) is checked before its total (6): a limit of 1 names the outermost
    ///     count, a limit of 4 the total.
    /// </summary>
    [TestMethod]
    public void MultidimensionalRoot_ChecksTheOutermostCountFirst()
    {
        var layout = new CStruct("struct r { uint8 a; };");
        byte[] data = [1, 2, 3, 4, 5, 6];

        StringAssert.StartsWith(Assert.Throws<CStructReadLimitException>(() => layout.ReadValue(data, "uint8[2][3]", options: new ReadOptions { MaxArrayElements = 1, })).Message, "Array length 2 exceeds MaxArrayElements (1)");
        StringAssert.StartsWith(Assert.Throws<CStructReadLimitException>(() => layout.ReadValue(data, "uint8[2][3]", options: new ReadOptions { MaxArrayElements = 4, })).Message, "Array length 6 exceeds MaxArrayElements (4)");
    }
}
