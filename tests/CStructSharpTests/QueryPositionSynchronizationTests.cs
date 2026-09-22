namespace CStructSharp.Tests;

/// <summary>Checks that source-position synchronization failures are not silently skipped by read-only queries.</summary>
[TestClass]
public class QueryPositionSynchronizationTests
{
    /// <summary>An exposed memory source can report a physical failure when its position is synchronized.</summary>
    [TestMethod]
    public void AddressQuery_PropagatesFinalPositionFailure()
    {
        var layout = new CStruct("struct root { uint8 count; uint8 values[count]; uint8 tail; };");
        using var source = new RejectPositionStream(new byte[] { 99, 2, 5, 6, 7, });
        source.Position = 1;
        source.RejectPositionWrites = true;

        // Buffer borrowing avoids physical reads, but completing the query still synchronizes the owned cursor.
        IOException failure = Assert.Throws<IOException>(() => layout.ResolveAddress(source, "root.tail"));
        Assert.AreSame(source.Failure, failure);
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>Exposes its buffer while permitting a controlled failure of its virtual position setter.</summary>
    private sealed class RejectPositionStream : MemoryStream
    {
        /// <summary>Creates a read-only source whose backing storage is available to the reader.</summary>
        /// <param name="bytes">The source bytes, retained without copying.</param>
        public RejectPositionStream(byte[] bytes)
            : base(bytes, 0, bytes.Length, writable: false, publiclyVisible: true)
        {
        }

        /// <summary>Gets the exact physical failure used to verify exception identity.</summary>
        public IOException Failure { get; } = new("Cannot synchronize the source position.");

        /// <summary>Gets or sets whether subsequent position assignments fail.</summary>
        public bool RejectPositionWrites { get; set; }

        /// <summary>Reads the actual cursor or rejects an armed physical synchronization attempt.</summary>
        /// <exception cref="IOException">Position assignments fail once rejection is enabled.</exception>
        public override long Position
        {
            get => base.Position;
            set
            {
                if (this.RejectPositionWrites)
                {
                    throw this.Failure;
                }

                base.Position = value;
            }
        }
    }
}
