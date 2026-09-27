namespace CStructSharp.Tests;

/// <summary>Provides controlled asynchronous reads and a caller context for continuation-boundary tests.</summary>
internal static class AsyncReadTestSupport
{
    /// <summary>Counts context dispatches while allowing them to finish, so an accidental capture cannot hang a test.</summary>
    internal sealed class RecordingContext : SynchronizationContext
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

    /// <summary>A source whose first read stays pending until released; later reads use normal memory-stream behavior.</summary>
    internal sealed class GatedStream : MemoryStream
    {
        private readonly TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Memory<byte> destination;
        private bool firstRead = true;

        /// <summary>Creates a source containing one two-byte record.</summary>
        public GatedStream()
            : base([1, 2,])
        {
        }

        /// <summary>Copies the pending bytes and completes the first read after the caller context was restored.</summary>
        public void ReleaseRead()
        {
            this.completion.SetResult(this.Read(this.destination.Span));
        }

        /// <summary>Holds the first destination until ReleaseRead supplies bytes, then permits ordinary reads through EOF.</summary>
        /// <param name="buffer">Memory that remains owned by the pending reader.</param>
        /// <param name="cancellationToken">Token checked before accepting the read.</param>
        /// <returns>The pending first read or a subsequent ordinary read.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!this.firstRead)
            {
                return base.ReadAsync(buffer, cancellationToken);
            }

            this.firstRead = false;
            this.destination = buffer;
            return new ValueTask<int>(this.completion.Task);
        }
    }
}
