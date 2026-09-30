namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Checks how a read of a bare root array takes its count: a terminated root is scanned once to resolve the root as a
///     path and once more by the read itself, and both scans are charged to the read budget; a multidimensional root's
///     outermost count is checked against the element limit before the total is.
/// </summary>
[TestClass]
public class RootCountResolutionTests
{
    /// <summary>
    ///     Reading <c>uint8[]</c> over <c>01 02 00</c> charges the three-byte scan twice and the two elements and the
    ///     terminator once more: eight bytes. One byte less fails at the terminator's scan.
    /// </summary>
    [TestMethod]
    public void TerminatedRoot_ChargesTheResolutionScanAndTheRead()
    {
        var layout = new CStruct("struct r { uint8 a; };");
        byte[] data = [1, 2, 0, 9];

        CollectionAssert.AreEqual(new byte[] { 1, 2, }, ((IEnumerable<object?>)layout.ReadValue(data, "uint8[]", options: new ReadOptions { MaxTotalBytesRead = 8, })!).Cast<byte>().ToArray());
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() => layout.ReadValue(data, "uint8[]", options: new ReadOptions { MaxTotalBytesRead = 7, }));
        Assert.AreEqual(2L, failure.Offset);
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
