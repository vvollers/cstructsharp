namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks exact sparse-update rental ownership without relying on shared-pool reuse or timing.</summary>
[TestClass]
public class SparseUpdatePoolTests
{
    /// <summary>The independently disposable sparse page returns its rental once even when disposed repeatedly.</summary>
    [TestMethod]
    public void SparseChunk_RepeatedDisposalReturnsItsRentalOnce()
    {
        var pool = new TrackingPool();
        Type chunkType = typeof(SparseUpdateStream).GetNestedType("StagedChunk", System.Reflection.BindingFlags.NonPublic)!;

        // Exercise the page's ownership contract directly; the outer stream suppresses its own repeated cleanup.
        var chunk = (IDisposable)Activator.CreateInstance(chunkType, pool)!;
        try
        {
            Assert.HasCount(1, pool.Rentals);
        }
        finally
        {
            chunk.Dispose();
        }

        chunk.Dispose();
        Assert.HasCount(1, pool.Returns);
        Assert.AreSame(pool.Rentals[0], pool.Returns[0]);
    }

    /// <summary>Two distant edits allocate only their sparse pages and compact metadata, not the untouched input extent.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void DistantWrites_KeepPageMetadataSmall()
    {
        using var baseline = new MemoryStream(new byte[1024 * 1024]);
        long allocated = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            // Warm the same path once before measuring; the provider allocates fresh exact-sized pages on both passes.
            var pool = new TrackingPool();
            long before = GC.GetAllocatedBytesForCurrentThread();
            using (var staging = new SparseUpdateStream(baseline, 0, pool))
            {
                staging.WriteByte(17);
                staging.Position = baseline.Length - 1;
                staging.WriteByte(29);
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.HasCount(3, pool.Rentals);
            Assert.HasCount(3, pool.Returns);
        }

        Assert.IsLessThan(64L * 1024, allocated, "Two 1-KiB sparse pages and their bitmaps should need far less than 64 KiB.");
    }

    /// <summary>Rewriting an exact-capacity range does not rent; growth doubles capacity and returns cleared storage once.</summary>
    [TestMethod]
    public void ContiguousGrowth_ReturnsClearedBuffersToTheirProvider()
    {
        var pool = new TrackingPool();
        using var baseline = new MemoryStream(new byte[256]);
        var staging = new SparseUpdateStream(baseline, 3, pool);
        try
        {
            byte[] initial = Enumerable.Repeat((byte)17, 64).ToArray();
            staging.Write(initial);
            Assert.HasCount(1, pool.Rentals);
            Assert.AreEqual(64, pool.Rentals[0].Length);
            staging.Position = 3;
            staging.Write(initial);
            Assert.HasCount(1, pool.Rentals, "An exact-capacity overwrite must reuse its range.");
            staging.WriteByte(29);
            Assert.HasCount(2, pool.Rentals);
            Assert.AreEqual(128, pool.Rentals[1].Length);
            Assert.HasCount(1, pool.Returns);
            Assert.AreSame(pool.Rentals[0], pool.Returns[0]);
            staging.CommitTo(baseline);
            CollectionAssert.AreEqual(initial, baseline.ToArray()[3..67]);
            Assert.AreEqual((byte)29, baseline.ToArray()[67]);
        }
        finally
        {
            staging.Dispose();
        }

        staging.Dispose();
        Assert.HasCount(2, pool.Returns, "Repeated disposal must not return a rental twice.");
        Assert.IsTrue(baseline.CanRead);
    }

    /// <summary>Promoting separated writes releases the old range and later returns every rented sparse page once.</summary>
    [TestMethod]
    public void SparsePromotion_ReturnsRangeAndEveryPage()
    {
        var pool = new TrackingPool();
        using var baseline = new MemoryStream(new byte[2048]);
        var staging = new SparseUpdateStream(baseline, 1, pool);
        try
        {
            staging.WriteByte(17);
            staging.Position = 1025;
            staging.WriteByte(29);
            Assert.HasCount(3, pool.Rentals);
            Assert.HasCount(1, pool.Returns);
            Assert.AreSame(pool.Rentals[0], pool.Returns[0]);
            staging.CommitTo(baseline);
            Assert.AreEqual((byte)17, baseline.ToArray()[1]);
            Assert.AreEqual((byte)29, baseline.ToArray()[1025]);
        }
        finally
        {
            staging.Dispose();
        }

        staging.Dispose();
        Assert.HasCount(3, pool.Returns);
        CollectionAssert.AreEquivalent(pool.Rentals, pool.Returns);
        Assert.IsTrue(baseline.CanWrite);
    }

    /// <summary>A commit seek failure preserves its cause and intended offset when querying the destination also fails.</summary>
    [TestMethod]
    public void CommitSeekFailure_PreservesCauseAndAttemptedOffset()
    {
        using var baseline = new MemoryStream(new byte[8]);
        using var staging = new SparseUpdateStream(baseline, 5);
        staging.WriteByte(17);
        using var destination = new FailedSeekStream();

        // The seek is the operation failure; a subsequent broken position getter must not replace it.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => staging.CommitTo(destination));
        StringAssert.StartsWith(failure.Message, "Cannot seek to a validated update range in the destination stream");
        Assert.AreSame(destination.Failure, failure.InnerException);
        Assert.AreEqual(5L, failure.Offset);
        CollectionAssert.AreEqual(new byte[8], baseline.ToArray());
    }

    /// <summary>Allocates exact requested capacities and verifies one clear-requested return for each rental.</summary>
    private sealed class TrackingPool : ArrayPool<byte>
    {
        public List<byte[]> Rentals { get; } = [];

        public List<byte[]> Returns { get; } = [];

        /// <summary>Records a fresh exact-capacity rental so growth requests can be checked independently of pool buckets.</summary>
        /// <param name="minimumLength">Requested byte capacity.</param>
        /// <returns>A fresh array owned by this pool until returned.</returns>
        public override byte[] Rent(int minimumLength)
        {
            var buffer = new byte[minimumLength];
            this.Rentals.Add(buffer);
            return buffer;
        }

        /// <summary>Rejects foreign or duplicate returns and requires the caller to request clearing.</summary>
        /// <param name="array">The previously rented buffer.</param>
        /// <param name="clearArray">Whether the provider must clear the buffer before retaining it.</param>
        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.Contains(array, this.Rentals);
            Assert.DoesNotContain(array, this.Returns);
            Assert.IsTrue(clearArray, "Sparse staging must request clearing when relinquishing an owned buffer.");
            array.AsSpan().Clear();
            this.Returns.Add(array);
        }
    }

    /// <summary>Fails a commit seek and makes diagnostic position lookup unavailable.</summary>
    private sealed class FailedSeekStream : MemoryStream
    {
        public IOException Failure { get; } = new("seek failed");

        public override long Position
        {
            get => throw new InvalidOperationException("position unavailable");
            set => throw this.Failure;
        }
    }
}
