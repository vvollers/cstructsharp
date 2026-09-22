namespace CStructSharp.Tests.Generated;

using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Distinguishes positioning from writing at fixed-buffer and existing-update boundaries.</summary>
[TestClass]
public class WriteCursorSeekBoundaryTests
{
    /// <summary>A forward seek within capacity clears its gap, including when the destination is not yet full.</summary>
    [TestMethod]
    public void FixedSeek_AllowsAPartiallyFilledDestination()
    {
        byte[] bytes = [9, 9, 9, 9,];
        var cursor = new WriteCursor(bytes);
        cursor.Seek(2, "padding", "uint8");
        Assert.AreEqual(2, cursor.Position);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 9, 9, }, bytes);
    }

    /// <summary>An Int32 endpoint is representable but outside a small region; larger addresses fail the capacity domain.</summary>
    /// <param name="position">The requested byte offset.</param>
    /// <param name="message">The precise expected positioning diagnostic.</param>
    [TestMethod]
    [DataRow((long)int.MaxValue, "The requested position is outside the supplied memory region")]
    [DataRow((long)int.MaxValue + 1, "The serialized value exceeds the supplied destination capacity")]
    public void FixedSeek_PreservesThePositionFailureCategory(long position, string message)
    {
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => new WriteCursor(new byte[2]).Seek(position, "padding", "uint8"));
        StringAssert.StartsWith(failure.Message, message);
        Assert.AreEqual("padding", failure.Member);
    }

    /// <summary>A bitfield start outside the destination is a positioning failure before its storage can be reserved.</summary>
    [TestMethod]
    public void Unit_ValidatesItsStartBeforeExtendingStorage()
    {
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => new WriteCursor(new byte[2]).Unit(3, 1, "bits", "uint8"));
        StringAssert.StartsWith(failure.Message, "The requested position is outside the supplied memory region");
        Assert.AreEqual("bits", failure.Member);
    }

    /// <summary>Positioning and borrowing existing update storage do not charge untouched bytes against a smaller write budget.</summary>
    [TestMethod]
    public void ExistingUpdateExtent_DoesNotChargeNoOpPositioning()
    {
        byte[] bytes = [1, 2, 3, 4,];
        var options = new WriteOptions { MaxTotalBytesWritten = 1, };
        var cursor = WriteCursor.ForUpdate(bytes, options);
        cursor.Seek(4, "tail", "uint8");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(1, 0, "tail", "uint8");
        Assert.AreEqual(4, cursor.Position);
        CollectionAssert.AreEqual(bytes, cursor.Unit(0, 4, "bits", "uint32").ToArray());
        Assert.AreEqual(4, cursor.Position);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, }, bytes);
    }
}
