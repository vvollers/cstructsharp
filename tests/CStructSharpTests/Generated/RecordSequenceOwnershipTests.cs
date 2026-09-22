namespace CStructSharp.Tests.Generated;

using System.Buffers;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using CStructSharp.Generated;

/// <summary>Checks record-sequence buffer ownership and oversized segmented input without allocating huge payloads.</summary>
[TestClass]
public class RecordSequenceOwnershipTests
{
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
        using var stream = new GatedStream();
        var context = new RecordingContext();
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

    /// <summary>Observes the runtime's pool-return event instead of assuming which array a later rental will choose.</summary>
    private sealed class PoolReturnListener : EventListener
    {
        private int watched;
        private int returned;

        public bool Returned => Volatile.Read(ref this.returned) == 1;

        /// <summary>Starts observing one currently rented array, clearing the previous observation.</summary>
        /// <param name="bufferId">Runtime array identity reported to ArrayPool's event source.</param>
        public void Watch(int bufferId)
        {
            Volatile.Write(ref this.returned, 0);
            Volatile.Write(ref this.watched, bufferId);
        }

        /// <summary>Enables only the runtime ArrayPool diagnostics when that source exists or is created.</summary>
        /// <param name="eventSource">The source announced by the runtime.</param>
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
            {
                this.EnableEvents(eventSource, EventLevel.Verbose);
            }
        }

        /// <summary>Records only returns for the watched array; unrelated global pool traffic cannot satisfy the assertion.</summary>
        /// <param name="eventData">The runtime event, whose first BufferReturned payload is the array identity.</param>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Runtime source contract: dotnet/runtime System/Buffers/ArrayPoolEventSource.cs, BufferReturned.
            if (eventData.EventName == "BufferReturned" && eventData.Payload?[0] is int id && id == Volatile.Read(ref this.watched))
            {
                Volatile.Write(ref this.returned, 1);
            }
        }
    }

    /// <summary>Counts context dispatches while allowing them to finish, so an accidental capture cannot hang the test.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        private int posts;

        public int Posts => Volatile.Read(ref this.posts);

        /// <summary>Records the dispatch and delegates execution to the thread pool.</summary>
        /// <param name="d">The continuation to execute.</param>
        /// <param name="state">The continuation's state.</param>
        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref this.posts);
            base.Post(d, state);
        }
    }

    /// <summary>A controlled asynchronous source whose first read stays pending until the test releases it.</summary>
    private sealed class GatedStream : MemoryStream
    {
        private readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Memory<byte> destination;

        /// <summary>Creates a source containing one two-byte record.</summary>
        public GatedStream()
            : base([1, 2,])
        {
        }

        /// <summary>Copies the pending bytes and completes the asynchronous read after the caller context was restored.</summary>
        public void ReleaseRead()
        {
            this.completion.SetResult(this.Read(this.destination.Span));
        }

        /// <summary>Holds the requested destination until ReleaseRead supplies its bytes.</summary>
        /// <param name="buffer">Memory that remains owned by the pending reader.</param>
        /// <param name="cancellationToken">Unused token; the test explicitly controls completion.</param>
        /// <returns>A pending read that completes when released by the test.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.destination = buffer;
            return new ValueTask<int>(this.completion.Task);
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
