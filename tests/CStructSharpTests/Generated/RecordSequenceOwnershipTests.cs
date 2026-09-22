namespace CStructSharp.Tests.Generated;

using System.Buffers;
using System.Runtime.InteropServices;
using CStructSharp.Generated;

/// <summary>Checks record-sequence buffer ownership and oversized segmented input without allocating huge payloads.</summary>
[TestClass]
public class RecordSequenceOwnershipTests
{
    /// <summary>A sequence-copy failure returns its exact rented array and preserves the original source exception.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void CopySequence_ReturnsItsRentalWhenSourceMemoryThrows()
    {
        using var returns = new PoolReturnListener();
        using var memory = new ThrowingMemory(returns);
        var source = new ReadOnlySequence<byte>(memory.Descriptor);
        IOException failure = Assert.Throws<IOException>(() => ReadCursor.CopySequence(source, null, out _));
        Assert.AreSame(memory.Failure, failure);
        Assert.IsTrue(returns.Returned, "The copy must return the exact buffer rented before reading the failing memory.");
    }

    /// <summary>Disposing each pooled input form returns the exact array used by its record reader.</summary>
    /// <remarks>Pool diagnostics are process-wide, so this test must not overlap allocation-measurement tests.</remarks>
    [TestMethod]
    [DoNotParallelize]
    public async Task EarlyDisposal_ReturnsEachBorrowedPoolBuffer()
    {
        using var returns = new PoolReturnListener();
        byte[] bytes = [1, 0, 2, 0,];
        var first = new Segment(bytes.AsMemory(0, 2));
        Segment last = first.Append(bytes.AsMemory(2));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        using (var records = RecordSequence.FromSequence(sequence, 2, "pair", null, ReadBufferIdentity).GetEnumerator())
        {
            Assert.IsTrue(records.MoveNext());
            returns.Watch(records.Current);
        }

        Assert.IsTrue(returns.Returned, "disposing a copied multi-segment sequence releases its array");
        using var sync = new MemoryStream(bytes);
        using (var records = RecordSequence.FromStream(sync, 2, "pair", null, ReadBufferIdentity).GetEnumerator())
        {
            Assert.IsTrue(records.MoveNext());
            returns.Watch(records.Current);
        }

        Assert.IsTrue(returns.Returned, "disposing a synchronous fixed-size iterator releases its array");
        using var asyncStream = new MemoryStream(bytes);
        await using (var records = RecordSequence.FromStreamAsync(asyncStream, 2, "pair", null, ReadBufferIdentity).GetAsyncEnumerator())
        {
            Assert.IsTrue(await records.MoveNextAsync());
            returns.Watch(records.Current);
        }

        Assert.IsTrue(returns.Returned, "disposing an asynchronous fixed-size iterator releases its array");
    }

    /// <summary>A valid segmented sequence beyond Int32 capacity rejects the conversion before renting a truncated array.</summary>
    [TestMethod]
    public void OversizedSequence_FailsBeforeCopying()
    {
        // Reusing one block represents repeated bytes in a valid >2 GiB sequence without allocating that payload.
        var block = new byte[1024 * 1024];
        var first = new Segment(block);
        Segment last = first;
        for (int index = 1; index < 2049; index++)
        {
            last = last.Append(block);
        }

        var source = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        Assert.IsGreaterThan((long)int.MaxValue, source.Length);
        using var records = RecordSequence.FromSequence(source, 2, "pair", null, ReadBufferIdentity).GetEnumerator();
        Assert.Throws<OverflowException>(() => records.MoveNext());
    }

    /// <summary>Waiting for stream bytes does not dispatch library continuations through the caller's UI context.</summary>
    [TestMethod]
    public async Task AsyncRecordRead_DoesNotCaptureTheCallersContext()
    {
        using var stream = new AsyncReadTestSupport.GatedStream();
        var context = new AsyncReadTestSupport.RecordingContext();
        await using var records = RecordSequence.FromStreamAsync(stream, 2, "pair", null, ReadBufferIdentity).GetAsyncEnumerator();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task<bool> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = records.MoveNextAsync().AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        stream.ReleaseRead();
        Assert.IsTrue(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(0, context.Posts, "library I/O continuations must not depend on the caller's context making progress");
    }

    /// <summary>Captures the pool buffer identity while consuming a two-byte record, without retaining the borrowed memory.</summary>
    /// <param name="source">Memory borrowed only for this callback.</param>
    /// <param name="offset">Unused relative record offset.</param>
    /// <param name="index">Unused record index.</param>
    /// <param name="shift">Unused absolute origin.</param>
    /// <param name="options">Unused read settings.</param>
    /// <param name="consumed">The two bytes consumed by this record.</param>
    /// <returns>The buffer's runtime identity used by ArrayPool diagnostic events.</returns>
    private static int ReadBufferIdentity(ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed)
    {
        Assert.IsTrue(MemoryMarshal.TryGetArray(source, out ArraySegment<byte> segment));
        consumed = 2;
        return segment.Array!.GetHashCode();
    }

    /// <summary>Provides a valid one-byte memory descriptor whose storage becomes unreadable when copied.</summary>
    private sealed class ThrowingMemory : MemoryManager<byte>
    {
        private readonly PoolReturnListener returns;

        /// <summary>Creates a descriptor without accessing the intentionally failing span.</summary>
        /// <param name="returns">Listener used to identify the rental immediately before the copy fails.</param>
        public ThrowingMemory(PoolReturnListener returns)
        {
            this.returns = returns;
        }

        public Memory<byte> Descriptor => this.CreateMemory(1);

        public IOException Failure { get; } = new("The source storage is unavailable.");

        /// <summary>Captures the copy's most recent rental and throws the same source-storage failure each time.</summary>
        /// <returns>No span is returned.</returns>
        /// <exception cref="IOException">The test source deliberately cannot provide storage.</exception>
        public override Span<byte> GetSpan()
        {
            this.returns.Watch(this.returns.LastRental);
            throw this.Failure;
        }

        /// <summary>Rejects pinning, which the sequence-copy operation must not require.</summary>
        /// <param name="elementIndex">Unused requested pin offset.</param>
        /// <returns>No pin is returned.</returns>
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        /// <summary>Releases no pin because this fixture never creates one.</summary>
        public override void Unpin()
        {
        }

        /// <summary>Releases no resources because the fixture contains no actual storage.</summary>
        /// <param name="disposing">Whether disposal is explicit.</param>
        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>A sequence segment with contiguous logical offsets, even when several segments reuse the same memory.</summary>
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        /// <summary>Creates the first segment over borrowed test memory.</summary>
        /// <param name="memory">The bytes represented by this segment.</param>
        public Segment(ReadOnlyMemory<byte> memory)
        {
            this.Memory = memory;
        }

        /// <summary>Appends borrowed memory immediately after this segment in the logical input.</summary>
        /// <param name="memory">The bytes of the next segment.</param>
        /// <returns>The appended segment.</returns>
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = this.RunningIndex + this.Memory.Length, };
            this.Next = next;
            return next;
        }
    }
}
