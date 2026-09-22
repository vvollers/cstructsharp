namespace CStructSharp.Tests;

using System.Buffers;
using System.Runtime.InteropServices;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks cancellation, empty-read boundaries and continuation ownership in shared input buffering.</summary>
[TestClass]
public class AsyncBufferBoundaryTests
{
    /// <summary>A buffered public read releases its exact input array after decoding succeeds or fails.</summary>
    /// <param name="truncated">Whether the selected value needs more bytes than the source supplies.</param>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublicRead_ReturnsItsInputRental(bool truncated)
    {
        using var returns = new PoolReturnListener();
        using var source = new ObservedReadStream(returns) { Position = 1, };
        var layout = new CStruct(truncated ? "struct root { uint32 value; };" : "struct root { uint8 value; };");
        if (truncated)
        {
            // Decoding failure occurs after acquisition transfers its rental to the public operation.
            await Assert.ThrowsAsync<CStructReadException>(async () => await layout.ParseAsync(source, "root"));
            Assert.AreEqual(1L, source.Position);
        }
        else
        {
            Assert.AreEqual((byte)7, (await layout.ParseAsync(source, "root")).Get<byte>("value"));
            Assert.AreEqual(2L, source.Position);
        }

        Assert.IsTrue(source.ObservedRental);
        Assert.IsTrue(returns.Returned, "The operation owns and must return the exact acquisition array.");
    }

    /// <summary>Async reads use the selected array slice and remaining length, not bytes outside that slice.</summary>
    /// <param name="visible">Whether reading borrows the underlying array or acquires a separate buffer.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ArraySlice_UsesItsOffsetAndRemainingLength(bool visible)
    {
        byte[] bytes = [0xEE, 0xDD, 0xCC, 0xBB, 0x34, 0x12, 0xAA, 0x99,];
        using var source = new MemoryStream(bytes, 3, 3, writable: false, publiclyVisible: visible) { Position = 1, };
        var layout = new CStruct("struct root { uint16 value; };");

        Assert.AreEqual((ushort)0x1234, await layout.ReadValueAsync<ushort>(source, "root.value"));
        Assert.AreEqual(3L, source.Position);

        source.Position = 1;
        var oversized = new CStruct("struct root { uint32 value; };");

        // Bytes after the stream's slice must never satisfy a truncated value.
        await Assert.ThrowsAsync<CStructReadException>(async () => await oversized.ParseAsync(source, "root"));
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>A pre-cancelled read allocates no buffer; a missing stream is still reported as an invalid argument first.</summary>
    [TestMethod]
    public async Task CancellationAndNullInput_FailBeforeRenting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var stream = new MemoryStream(new byte[2]);
        var pool = new CountingPool();

        // Cancellation must be checked before acquiring pooled storage.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await AsyncStreamBuffer.RentAsync(stream, null, pool, cancellation.Token));
        Assert.AreEqual(0, pool.Rented);
        Assert.AreEqual(0L, stream.Position);

        // Argument validation precedes cancellation and must name the missing input.
        ArgumentNullException failure = await Assert.ThrowsAsync<ArgumentNullException>(async () => await AsyncStreamBuffer.RentAsync(null!, null, pool, cancellation.Token));
        Assert.AreEqual("stream", failure.ParamName);
        Assert.AreEqual(0, pool.Rented);
    }

    /// <summary>Synchronous buffering stops at capacity without asking a forward-only source for an empty extra read.</summary>
    [TestMethod]
    public void SynchronousBuffering_StopsExactlyAtCapacity()
    {
        using var source = new CStructSharpTests.AsyncStreamBufferTests.NonSeekableStream([1, 2, 3, 4,]);
        byte[] buffer = AsyncStreamBuffer.Rent(source, new ReadOptions { MaxTotalBytesRead = 1, }, out int length);
        try
        {
            Assert.AreEqual(2, length);
            Assert.AreEqual(2, source.BytesRead);
            CollectionAssert.AreEqual(new byte[] { 1, 2, }, buffer[..length]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>A pending source read resumes library buffering without dispatching through the caller's context.</summary>
    [TestMethod]
    public async Task Buffering_DoesNotCaptureTheCallersContext()
    {
        using var stream = new AsyncReadTestSupport.GatedStream();
        var context = new AsyncReadTestSupport.RecordingContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task<(byte[] Buffer, int Length)> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = AsyncStreamBuffer.RentAsync(stream, null, CancellationToken.None).AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        stream.ReleaseRead();
        (byte[] buffer, int length) = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.AreEqual(2, length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, }, buffer[..length]);
            Assert.AreEqual(0, context.Posts);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Observes the actual memory supplied to async acquisition without exposing its own source array.</summary>
    /// <param name="returns">Observer for the acquisition array's eventual return.</param>
    private sealed class ObservedReadStream(PoolReturnListener returns) : MemoryStream(new byte[] { 0xAA, 7, })
    {
        public bool ObservedRental { get; private set; }

        /// <summary>Records the first acquisition array and completes the read synchronously.</summary>
        /// <param name="buffer">The rented destination memory.</param>
        /// <param name="cancellationToken">Cancellation checked before consuming source bytes.</param>
        /// <returns>The number of source bytes copied.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!this.ObservedRental)
            {
                Assert.IsTrue(MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)buffer, out ArraySegment<byte> segment));
                returns.Watch(segment.Array!.GetHashCode());
                this.ObservedRental = true;
            }

            return ValueTask.FromResult(this.Read(buffer.Span));
        }
    }

    /// <summary>Counts unexpected acquisitions in tests that must fail before renting any storage.</summary>
    private sealed class CountingPool : ArrayPool<byte>
    {
        public int Rented { get; private set; }

        /// <summary>Counts the acquisition and supplies ordinary test-owned storage.</summary>
        /// <param name="minimumLength">The requested minimum capacity.</param>
        /// <returns>A fresh array of that capacity.</returns>
        public override byte[] Rent(int minimumLength)
        {
            this.Rented++;
            return new byte[minimumLength];
        }

        /// <summary>Releases no pooled resources because every test rental is an ordinary managed array.</summary>
        /// <param name="array">The returned array.</param>
        /// <param name="clearArray">Unused clearing request.</param>
        public override void Return(byte[] array, bool clearArray = false)
        {
        }
    }
}
