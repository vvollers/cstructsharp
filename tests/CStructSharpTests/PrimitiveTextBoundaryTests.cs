namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks exact text budgets, short-read diagnostics and ownership of encoded payload rentals.</summary>
[TestClass]
[DoNotParallelize]
public class PrimitiveTextBoundaryTests
{
    /// <summary>A bounded text read accepts its exact byte limit and reports a physical shortfall as a read error.</summary>
    /// <param name="shortInput">Whether the source lacks the last declared byte.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BoundedText_UsesTheExactByteBudget(bool shortInput)
    {
        using var source = new MemoryStream(shortInput ? new byte[] { 65, 66, } : new byte[] { 65, 66, 67, });
        using var reader = new ReadBudgetStream(source, 3, 100);
        if (shortInput)
        {
            // The declared extent is affordable but physically unavailable, not a string-budget failure.
            CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => PrimitiveCodecs.ReadBoundedText(reader, 3, "latin1"));
            Assert.IsInstanceOfType<EndOfStreamException>(failure.InnerException);
        }
        else
        {
            Assert.AreEqual("ABC", PrimitiveCodecs.ReadBoundedText(reader, 3, "latin1"));
            Assert.AreEqual(3L, reader.Position);
        }
    }

    /// <summary>The exact rented encoded payload is returned after successful writes and destination failures.</summary>
    /// <param name="fail">Whether the destination throws while receiving the encoded bytes.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TerminatedWrite_ReturnsItsPayload(bool fail)
    {
        using var returns = new PoolReturnListener();
        using var destination = new ObservedPayloadStream(returns, fail);
        if (fail)
        {
            // The stream observes the actual array before throwing, so the assertion cannot match an unrelated rental.
            IOException failure = Assert.ThrowsExactly<IOException>(() => PrimitiveCodecs.WriteTerminatedString(destination, PrimitiveCodecs.StrictUtf8Encoding, "hello", '\0'));
            Assert.AreSame(destination.Failure, failure);
        }
        else
        {
            PrimitiveCodecs.WriteTerminatedString(destination, PrimitiveCodecs.StrictUtf8Encoding, "hello", '\0');
            CollectionAssert.AreEqual(new byte[] { 104, 101, 108, 108, 111, 0, }, destination.ToArray());
        }

        Assert.AreEqual(1, destination.WriteCalls);
        Assert.IsTrue(returns.Returned, "The observed encoded payload must be returned before the writer exits.");
    }

    /// <summary>A chunk ending exactly at its terminator has no unread tail and requires no repositioning.</summary>
    [TestMethod]
    public void ExactTerminator_DoesNotSeekBackZeroBytes()
    {
        using var source = new ObservedPositionStream();
        Assert.AreEqual("A", PrimitiveCodecs.ReadIntoString(source, PrimitiveCodecs.StrictAsciiEncoding, '\0'));
        Assert.AreEqual(0, source.PositionWrites);
        Assert.AreEqual(2L, source.Position);
    }

    /// <summary>Observes the encoded array received by the destination, optionally failing before accepting it.</summary>
    private sealed class ObservedPayloadStream : MemoryStream
    {
        private readonly PoolReturnListener returns;
        private readonly bool fail;

        /// <summary>Stores the exact-rental observer and failure mode.</summary>
        /// <param name="returns">Listener to watch the received array.</param>
        /// <param name="fail">Whether every write fails.</param>
        public ObservedPayloadStream(PoolReturnListener returns, bool fail)
        {
            this.returns = returns;
            this.fail = fail;
        }

        /// <summary>Gets the stable destination failure for exception-identity assertions.</summary>
        public IOException Failure { get; } = new("Destination write failed.");

        /// <summary>Gets the number of attempted payload writes.</summary>
        public int WriteCalls { get; private set; }

        /// <summary>Watches the supplied array and either writes its requested bytes or throws the configured failure.</summary>
        /// <param name="buffer">The codec-owned rented payload.</param>
        /// <param name="offset">First payload byte.</param>
        /// <param name="count">Number of payload bytes.</param>
        /// <exception cref="IOException">The destination is configured to reject writes.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteCalls++;
            this.returns.Watch(buffer.GetHashCode());
            if (this.fail)
            {
                throw this.Failure;
            }

            base.Write(buffer, offset, count);
        }
    }

    /// <summary>Counts repositioning independently of sequential reads from a two-byte terminated value.</summary>
    private sealed class ObservedPositionStream : MemoryStream
    {
        /// <summary>Creates a readable A followed by its zero terminator.</summary>
        public ObservedPositionStream()
            : base([65, 0,])
        {
        }

        /// <summary>Gets the number of explicit position assignments.</summary>
        public int PositionWrites { get; private set; }

        /// <summary>Gets or changes the cursor while counting explicit repositioning requests.</summary>
        public override long Position
        {
            get => base.Position;
            set
            {
                this.PositionWrites++;
                base.Position = value;
            }
        }
    }
}
