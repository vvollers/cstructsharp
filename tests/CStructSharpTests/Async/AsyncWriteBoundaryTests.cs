namespace CStructSharp.Tests;

using System.Collections;
using CStructSharp.Diagnostics;

/// <summary>
///     Checks the asynchronous write and update boundaries: option snapshots (without an extra copy when the write
///     cannot be canceled), exact changed ranges and input rentals, the original-byte snapshot an update returns, which
///     failure survives when the origin cannot be restored, and pending I/O that never captures the caller's
///     synchronization context.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AsyncWriteBoundaryTests
{
    /// <summary>Linking either cancellation source must retain the caller's zero-byte output budget.</summary>
    /// <param name="argumentToken">Whether the cancellable token comes from the method argument rather than options.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task WriteAsync_LinkingRetainsTheOutputBudget(bool argumentToken)
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var options = new WriteOptions { MaxTotalBytesWritten = 0, CancellationToken = argumentToken ? default : cancellation.Token, };
        var value = new Dictionary<string, object?> { ["value"] = (byte)1, };

        // The token can cancel but has not been cancelled; the byte budget must be the reason for rejection.
        await Assert.ThrowsAsync<CStructWriteLimitException>(async () => await layout.WriteAsync(destination, "root", value, options: options, cancellationToken: argumentToken ? cancellation.Token : default));
        Assert.AreEqual(0L, destination.Length);
    }

    /// <summary>Async acquisition respects the traversal window before trying to update a field beyond that window.</summary>
    [TestMethod]
    public async Task UpdateAsync_RetainsTheTraversalWindow()
    {
        var layout = new CStruct("struct root { uint8 padding[4]; uint8 tail; };");
        using var destination = new MemoryStream(new byte[5]);
        using var cancellation = new CancellationTokenSource();

        // The bounded acquisition includes a lookahead byte, but cannot supply the field at byte four.
        await Assert.ThrowsAsync<CStructException>(async () => await layout.UpdateAsync(destination, "root.tail", (byte)9, options: new UpdateOptions { MaxTraversalBytesRead = 1, }, cancellationToken: cancellation.Token));
        CollectionAssert.AreEqual(new byte[5], destination.ToArray());
        Assert.AreEqual(0L, destination.Position);
    }

    /// <summary>A changed run ending at the exact buffer length is written once, flushed and followed by rental return.</summary>
    [TestMethod]
    public async Task UpdateAsync_ExactEndFlushesAndReturnsTheInputRental()
    {
        using var listener = new PoolReturnListener();
        var layout = new CStruct("struct root { uint8 values[16]; };");
        using var destination = new ObservedStream(new byte[16], listener);
        byte[] replacement = Enumerable.Repeat((byte)7, 16).ToArray();
        await layout.UpdateAsync(destination, "root.values", replacement);
        CollectionAssert.AreEqual(replacement, destination.ToArray());
        Assert.AreEqual(1, destination.WriteCalls);
        Assert.AreEqual(16, destination.LastWriteLength);
        Assert.AreEqual(1, destination.FlushCalls);
        Assert.AreEqual(0L, destination.Position);
        Assert.IsTrue(destination.ObservedRental);
        Assert.IsTrue(listener.Returned, "The async input rental must be returned after commit.");
    }

    /// <summary>Read-only destinations are rejected with the write operation's capability explanation.</summary>
    [TestMethod]
    public async Task WriteAsync_ReadOnlyDestinationExplainsItsRequirement()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var destination = new MemoryStream(new byte[1], writable: false);

        // Capability validation precedes value serialization and must identify the rejected stream argument.
        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(async () => await layout.WriteAsync(destination, "root.value", (byte)1));
        Assert.AreEqual("stream", failure.ParamName);
        StringAssert.StartsWith(failure.Message, "Writing requires a writable stream.");
    }

    /// <summary>The original-byte snapshot is returned after either successful staging or a value-validation failure.</summary>
    /// <param name="invalidValue">Whether to supply a number outside the selected byte field's range.</param>
    /// <returns>Completion after update and exact buffer-return assertions.</returns>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Update_ReturnsItsOriginalSnapshot(bool invalidValue)
    {
        var layout = new CStruct("typedef uint8 item;");
        using var destination = new MemoryStream(new byte[17]) { Position = 1, };
        using var returns = new PoolReturnListener();
        var variables = new ObservedVariables(returns);
        if (invalidValue)
        {
            // Validation fails after both the input rental and original-byte snapshot have been acquired.
            await Assert.ThrowsAsync<CStructWriteException>(async () =>
                await layout.UpdateAsync(destination, "item", 256, variables));
        }
        else
        {
            await layout.UpdateAsync(destination, "item", (byte)7, variables);
        }

        Assert.IsTrue(variables.Observed);
        Assert.IsTrue(returns.Returned, "The exact original-byte snapshot must be returned, not just the input buffer.");
        Assert.AreEqual(1L, destination.Position);
        byte[] expected = new byte[17];
        if (!invalidValue)
        {
            expected[1] = 7;
        }

        CollectionAssert.AreEqual(expected, destination.ToArray());
    }

    /// <summary>A restoration error surfaces only when there is no earlier I/O failure to preserve.</summary>
    /// <param name="failFlush">Whether flushing produces a primary failure before origin restoration also fails.</param>
    /// <returns>Completion after the observed failure and destination-state assertions.</returns>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RestorationFailure_PreservesTheCorrectCause(bool failFlush)
    {
        var layout = new CStruct("typedef uint8 item;");
        using var destination = new FailingRestorationStream(failFlush);

        // Both errors are I/O failures, so check object identity rather than accepting either exception type.
        IOException failure = await Assert.ThrowsAsync<IOException>(async () =>
            await layout.UpdateAsync(destination, "item", (byte)7));
        Assert.AreSame(failFlush ? destination.FlushFailure : destination.RestorationFailure, failure);
        CollectionAssert.AreEqual(new byte[] { 1, 7, 3, }, destination.ToArray());
        Assert.AreEqual(2L, destination.Position, "A destination refusing restoration cannot retain the origin contract.");
    }

    /// <summary>A genuinely pending I/O operation resumes without posting through the caller's context.</summary>
    /// <param name="update">Whether to update existing bytes instead of writing a new encoded value.</param>
    /// <param name="operation">The I/O boundary held pending until the caller context has been restored.</param>
    /// <returns>Completion after the released operation and its continuation assertions.</returns>
    [TestMethod]
    [DataRow(false, "write")]
    [DataRow(true, "read")]
    [DataRow(true, "write")]
    [DataRow(true, "flush")]
    public async Task PendingIo_DoesNotCaptureCallerContext(bool update, string operation)
    {
        var layout = new CStruct("typedef uint8 item;");
        using var stream = new GatedDestination(operation);
        var context = new AsyncReadTestSupport.RecordingContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = update
                ? layout.UpdateAsync(stream, "item", (byte)7).AsTask()
                : layout.WriteAsync(stream, "item", (byte)7).AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.IsTrue(stream.Waiting, "The selected I/O boundary must actually suspend.");
        Assert.IsFalse(pending.IsCompleted);
        stream.Release();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, context.Posts);
        Assert.AreEqual(update ? 1L : 2L, stream.Position);
        CollectionAssert.AreEqual(new byte[] { 1, 7, 3, }, stream.ToArray());
    }

    /// <summary>A non-cancellable wrapper does not invoke a caller-defined record copy constructor an extra time.</summary>
    /// <returns>Completion after the write and comparison with synchronous snapshot behavior.</returns>
    [TestMethod]
    public async Task NonCancellableWrite_DoesNotAddAnOptionsCopy()
    {
        var layout = new CStruct("typedef uint8 item;");
        int[] copies = [0,];
        var options = new ObservedOptions(copies);
        byte[] expected = layout.Serialize("item", (byte)7, options: options);
        int synchronousCopies = copies[0];
        Assert.IsGreaterThan(0, synchronousCopies);

        copies[0] = 0;
        using var destination = new MemoryStream();
        await layout.WriteAsync(destination, "item", (byte)7, options: options);

        // Compare with the synchronous operation instead of hard-coding its internal number of snapshots.
        Assert.AreEqual(synchronousCopies, copies[0]);
        CollectionAssert.AreEqual(expected, destination.ToArray());
    }

    /// <summary>Records physical async I/O and watches the first input rental without making pool reuse assumptions.</summary>
    private sealed class ObservedStream : MemoryStream
    {
        private readonly PoolReturnListener listener;

        /// <summary>Creates a seekable writable stream over the supplied existing destination bytes.</summary>
        /// <param name="bytes">The destination storage.</param>
        /// <param name="listener">The pool observer on this synchronously completing test's thread.</param>
        public ObservedStream(byte[] bytes, PoolReturnListener listener)
            : base(bytes)
        {
            this.listener = listener;
        }

        public bool ObservedRental { get; private set; }

        public int WriteCalls { get; private set; }

        public int LastWriteLength { get; private set; }

        public int FlushCalls { get; private set; }

        /// <summary>Watches the caller's input rental before filling it with existing destination bytes.</summary>
        /// <param name="buffer">The input-acquisition rental.</param>
        /// <param name="cancellationToken">Cancellation checked before reading.</param>
        /// <returns>A synchronously completed byte count.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!this.ObservedRental)
            {
                this.listener.Watch(this.listener.LastRental);
                this.ObservedRental = true;
            }

            return ValueTask.FromResult(this.Read(buffer.Span));
        }

        /// <summary>Records one physical changed range and writes exactly its requested bytes.</summary>
        /// <param name="buffer">The changed bytes to commit.</param>
        /// <param name="cancellationToken">Cancellation checked before changing storage.</param>
        /// <returns>A completed write.</returns>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.WriteCalls++;
            this.LastWriteLength = buffer.Length;
            this.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        /// <summary>Records the explicit post-commit flush without additional storage work.</summary>
        /// <param name="cancellationToken">Cancellation checked before recording the flush.</param>
        /// <returns>A completed flush.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.FlushCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Observes the last rental when update validation first inspects caller variables.</summary>
    /// <param name="returns">Pool diagnostics already enabled before the update starts.</param>
    private sealed class ObservedVariables(PoolReturnListener returns) : IReadOnlyDictionary<string, int>
    {
        public bool Observed { get; private set; }

        /// <summary>Reports an empty variable set after identifying the update's original-byte snapshot.</summary>
        public int Count
        {
            get
            {
                if (!this.Observed)
                {
                    // Acquisition requests 17 bytes and receives a 32-byte bucket. The subsequent snapshot
                    // requests the actual 16-byte region. No staging rentals precede variable inspection.
                    Assert.AreEqual(16, returns.LastRentalLength);
                    Assert.AreNotEqual(0, returns.LastRental);
                    returns.Watch(returns.LastRental);
                    this.Observed = true;
                }

                return 0;
            }
        }

        public IEnumerable<string> Keys => Array.Empty<string>();

        public IEnumerable<int> Values => Array.Empty<int>();

        public int this[string key] => throw new KeyNotFoundException();

        /// <summary>Reports that the empty fixture contains no key.</summary>
        /// <param name="key">The unused candidate name.</param>
        /// <returns>False.</returns>
        public bool ContainsKey(string key) => false;

        /// <summary>Reports a missing value without inventing an override.</summary>
        /// <param name="key">The unused candidate name.</param>
        /// <param name="value">Zero, unused because the lookup fails.</param>
        /// <returns>False.</returns>
        public bool TryGetValue(string key, out int value)
        {
            value = 0;
            return false;
        }

        /// <summary>Enumerates no variable overrides.</summary>
        /// <returns>An empty key/value enumerator.</returns>
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
            => ((IEnumerable<KeyValuePair<string, int>>)Array.Empty<KeyValuePair<string, int>>()).GetEnumerator();

        /// <summary>Provides the same empty enumeration through the non-generic interface.</summary>
        /// <returns>An empty enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    /// <summary>Allows acquisition and write-back, then refuses all position restoration after flushing begins.</summary>
    private sealed class FailingRestorationStream : MemoryStream
    {
        private readonly bool failFlush;
        private bool refusePosition;

        /// <summary>Creates a writable region whose origin is one byte into the stream.</summary>
        /// <param name="failFlush">Whether to fail flushing in addition to the later restoration.</param>
        public FailingRestorationStream(bool failFlush)
            : base([1, 2, 3,])
        {
            this.failFlush = failFlush;
            this.Position = 1;
        }

        public IOException FlushFailure { get; } = new("flush failure");

        public IOException RestorationFailure { get; } = new("restoration failure");

        public override long Position
        {
            get => base.Position;
            set
            {
                if (this.refusePosition)
                {
                    throw this.RestorationFailure;
                }

                base.Position = value;
            }
        }

        /// <summary>Arms restoration failure and optionally returns the earlier flush failure.</summary>
        /// <param name="cancellationToken">Unused token; this fixture exercises physical I/O failures.</param>
        /// <returns>A failed or completed flush task.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            this.refusePosition = true;
            return this.failFlush ? Task.FromException(this.FlushFailure) : Task.CompletedTask;
        }
    }

    /// <summary>Suspends one selected I/O kind while other operations complete as ordinary memory-stream calls.</summary>
    private sealed class GatedDestination : MemoryStream
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string operation;

        /// <summary>Creates a writable three-byte destination with a one-byte prefix outside the operation's region.</summary>
        /// <param name="operation">The read, write or flush boundary to suspend.</param>
        public GatedDestination(string operation)
            : base([1, 2, 3,])
        {
            this.operation = operation;
            this.Position = 1;
        }

        public bool Waiting { get; private set; }

        /// <summary>Completes the gate after the test has restored the caller's original context.</summary>
        public void Release() => this.completion.SetResult();

        /// <summary>Reads the original region after the optional read gate has completed.</summary>
        /// <param name="buffer">The caller-owned destination memory.</param>
        /// <param name="cancellationToken">Cancellation passed to the underlying read.</param>
        /// <returns>The number of bytes copied.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await this.GateAsync("read").ConfigureAwait(false);
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Writes the changed bytes after the optional write gate has completed.</summary>
        /// <param name="buffer">The caller-owned bytes, retained until this operation completes.</param>
        /// <param name="cancellationToken">Cancellation passed to the underlying write.</param>
        /// <returns>Completion after the destination has received the bytes.</returns>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await this.GateAsync("write").ConfigureAwait(false);
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Completes the update's final flush after the optional flush gate has completed.</summary>
        /// <param name="cancellationToken">Cancellation passed to the underlying flush.</param>
        /// <returns>Completion of the memory-stream flush.</returns>
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await this.GateAsync("flush").ConfigureAwait(false);
            await base.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Returns the pending gate only for the selected I/O kind; other kinds do not suspend.</summary>
        /// <param name="kind">The I/O operation currently entering the destination.</param>
        /// <returns>The selected gate or an already completed task.</returns>
        private Task GateAsync(string kind)
        {
            if (kind != this.operation)
            {
                return Task.CompletedTask;
            }

            this.Waiting = true;
            return this.completion.Task;
        }
    }

    /// <summary>A caller-defined options record whose ordinary copy constructor has an observable side effect.</summary>
    private sealed record ObservedOptions : WriteOptions
    {
        /// <summary>Creates the immutable options while retaining a shared diagnostic counter.</summary>
        /// <param name="copies">A one-element counter incremented by each copy constructor call.</param>
        public ObservedOptions(int[] copies)
        {
            this.Copies = copies;
        }

        /// <summary>Preserves base options and records the caller-observable copy.</summary>
        /// <param name="original">The options being snapshotted.</param>
        private ObservedOptions(ObservedOptions original)
            : base(original)
        {
            this.Copies = original.Copies;
            this.Copies[0]++;
        }

        private int[] Copies { get; }
    }
}
