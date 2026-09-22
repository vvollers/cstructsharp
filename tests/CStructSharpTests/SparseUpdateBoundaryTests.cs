namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks sparse staging at range-growth, repeated-write, early-end and ownership boundaries.</summary>
[TestClass]
[DoNotParallelize]
public class SparseUpdateBoundaryTests
{
    /// <summary>Growing a contiguous range returns its original rental and preserves its prefix until commit.</summary>
    [TestMethod]
    public void RangeGrowth_ReturnsOldRentalAndPreservesPrefix()
    {
        using var listener = new PoolReturnListener();
        using var baseline = new MemoryStream(new byte[8192]);
        var staging = new SparseUpdateStream(baseline, 3);
        staging.WriteByte(17);
        int original = listener.LastRental;
        int capacity = listener.LastRentalLength;
        Assert.IsTrue(capacity >= 64 && capacity < 4096);
        listener.Watch(original);
        byte[] suffix = Enumerable.Repeat((byte)29, capacity).ToArray();
        staging.Write(suffix);
        Assert.IsTrue(listener.Returned, "Growing must return the superseded buffer.");
        int replacement = listener.LastRental;
        Assert.AreNotEqual(original, replacement);
        using var destination = new MemoryStream(new byte[8192]);
        staging.CommitTo(destination);
        byte[] actual = destination.ToArray();
        Assert.AreEqual((byte)17, actual[3]);
        CollectionAssert.AreEqual(suffix, actual[4..(4 + capacity)]);
        CollectionAssert.AreEqual(new byte[8192], baseline.ToArray());
        listener.Watch(replacement);
        staging.Dispose();
        Assert.IsTrue(listener.Returned, "Disposal must return the grown buffer.");
        staging.Dispose();
        Assert.IsTrue(baseline.CanRead);
    }

    /// <summary>Opening a gap transfers the original range into chunks and releases both kinds of rentals.</summary>
    [TestMethod]
    public void RangePromotion_ReturnsRangeAndChunkRentals()
    {
        using var listener = new PoolReturnListener();
        using var baseline = new MemoryStream(new byte[4096]);
        var staging = new SparseUpdateStream(baseline, 1);
        staging.WriteByte(13);
        listener.Watch(listener.LastRental);
        staging.Position = 2050;
        staging.WriteByte(27);
        Assert.IsTrue(listener.Returned, "Promotion must release the original range.");
        int chunkRental = listener.LastRental;
        staging.Position = 1;
        Assert.AreEqual(13, staging.ReadByte());
        staging.Position = 2050;
        Assert.AreEqual(27, staging.ReadByte());
        listener.Watch(chunkRental);
        staging.Dispose();
        Assert.IsTrue(listener.Returned, "Disposal must release sparse chunk storage.");
        staging.Dispose();
    }

    /// <summary>Rewriting bytes in a chunk keeps their written bits set instead of toggling them off.</summary>
    [TestMethod]
    public void RepeatedChunkWrites_RetainLastValuesAcrossBitmapWords()
    {
        using var baseline = new MemoryStream(new byte[2050]);
        using var staging = new SparseUpdateStream(baseline, 1023);
        staging.Write(new byte[] { 10, 11, });
        staging.Position = 63;
        staging.Write(new byte[] { 20, 21, });
        staging.Position = 1023;
        staging.Write(new byte[] { 30, 31, });
        staging.Position = 63;
        staging.Write(new byte[] { 40, 41, });
        using var destination = new MemoryStream(new byte[2050]);
        staging.CommitTo(destination);
        byte[] expected = new byte[2050];
        expected[63] = 40;
        expected[64] = 41;
        expected[1023] = 30;
        expected[1024] = 31;
        CollectionAssert.AreEqual(expected, destination.ToArray());
        staging.Position = 0;
        byte[] overlay = new byte[2050];
        staging.ReadExactly(overlay);
        CollectionAssert.AreEqual(expected, overlay);
    }

    /// <summary>An untouched view commits no range and does not reposition its destination.</summary>
    [TestMethod]
    public void EmptyCommit_LeavesDestinationPositionAlone()
    {
        using var baseline = new MemoryStream(new byte[4]);
        using var staging = new SparseUpdateStream(baseline, 0);
        using var destination = new MemoryStream(new byte[] { 1, 2, 3, 4, });
        destination.Position = 3;
        staging.CommitTo(destination);
        Assert.AreEqual(3L, destination.Position);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, }, destination.ToArray());
    }

    /// <summary>Empty reads before EOF and nonempty reads after EOF must not access the baseline or move the cursor.</summary>
    [TestMethod]
    public void EmptyAndPastEndReads_DoNotTouchBaseline()
    {
        using var baseline = new MemoryStream(new byte[] { 1, 2, 3, });
        using var staging = new SparseUpdateStream(baseline, 0);
        baseline.Position = 2;
        Assert.AreEqual(0, staging.Read(Span<byte>.Empty));
        Assert.AreEqual(2L, baseline.Position);
        staging.Position = 4;
        Assert.AreEqual(0, staging.Read(new byte[1]));
        Assert.AreEqual(-1, staging.ReadByte());
        Assert.AreEqual(4L, staging.Position);
        Assert.AreEqual(2L, baseline.Position);
    }

    /// <summary>A baseline that ends earlier than its captured length returns short reads without inventing bytes.</summary>
    [TestMethod]
    public void ShortenedBaseline_StopsAtActualEnd()
    {
        using var baseline = new MemoryStream(new byte[] { 1, 2, 3, 4, });
        using var staging = new SparseUpdateStream(baseline, 1);
        baseline.SetLength(2);
        byte[] buffer = new byte[] { 99, 99, 99, };
        Assert.AreEqual(1, staging.Read(buffer));
        CollectionAssert.AreEqual(new byte[] { 2, 99, 99, }, buffer);
        Assert.AreEqual(2L, staging.Position);
        Assert.AreEqual(-1, staging.ReadByte());
        Assert.AreEqual(2L, staging.Position);
    }

    /// <summary>Invalid extent operations explain whether the start, cursor, length or replacement extent is wrong.</summary>
    [TestMethod]
    public void InvalidExtents_ExplainTheRejectedOperation()
    {
        using var baseline = new MemoryStream(new byte[2]);
        using var staging = new SparseUpdateStream(baseline, 2);

        // Each operation has a distinct diagnostic so callers can distinguish an address error from resizing.
        StringAssert.StartsWith(Assert.Throws<CStructWriteException>(() => new SparseUpdateStream(baseline, 3)).Message, "Update target starts beyond the existing destination stream.");
        StringAssert.StartsWith(Assert.Throws<CStructWriteException>(() => staging.Position = -1).Message, "An update cannot seek before the start of the destination.");
        StringAssert.StartsWith(Assert.Throws<CStructWriteException>(() => staging.SetLength(3)).Message, "Update operations cannot change the destination stream length.");
        StringAssert.StartsWith(Assert.Throws<CStructWriteException>(() => staging.WriteByte(3)).Message, "Update output would extend beyond the existing destination stream.");
        staging.Position = long.MaxValue;

        // Arithmetic overflow is distinguished from an ordinary out-of-range extent and retains its cause.
        CStructWriteException seek = Assert.Throws<CStructWriteException>(() => staging.Seek(1, SeekOrigin.Current));
        Assert.IsInstanceOfType<OverflowException>(seek.InnerException);
        StringAssert.StartsWith(seek.Message, "Update staging position exceeded the supported stream range.");
    }
}
