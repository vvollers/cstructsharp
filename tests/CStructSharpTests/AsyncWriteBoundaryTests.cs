namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks async write option snapshots and exact changed-range and input-rental boundaries.</summary>
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
}
