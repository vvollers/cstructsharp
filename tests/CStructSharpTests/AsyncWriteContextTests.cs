namespace CStructSharp.Tests;

/// <summary>Checks caller-context independence at each asynchronous write and update I/O boundary.</summary>
[TestClass]
public class AsyncWriteContextTests
{
    /// <summary>A genuinely pending I/O operation resumes without posting through the caller's context.</summary>
    /// <param name="update">Whether to update existing bytes instead of writing a new encoded value.</param>
    /// <param name="operation">The I/O boundary held pending until the caller context has been restored.</param>
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
}
