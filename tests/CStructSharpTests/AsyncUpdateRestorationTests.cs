namespace CStructSharp.Tests;

/// <summary>Checks which failure survives when an async update cannot restore the caller's origin.</summary>
[TestClass]
public class AsyncUpdateRestorationTests
{
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
}
