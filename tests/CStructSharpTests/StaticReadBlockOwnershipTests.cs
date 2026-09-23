namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Reading;

/// <summary>Checks the bounded stream block and exact buffer return used by fixed composite plans.</summary>
[TestClass]
[DoNotParallelize]
public class StaticReadBlockOwnershipTests
{
    /// <summary>An aligned byte record at a nonzero origin uses one bounded block and returns it even after failure.</summary>
    /// <param name="size">The fixed character field's byte extent.</param>
    /// <param name="fail">Whether the physical block read throws before returning bytes.</param>
    [TestMethod]
    [DataRow(65536, false)]
    [DataRow(65536, true)]
    [DataRow(65537, false)]
    public void FixedBlock_RespectsItsSizeAndOwnership(int size, bool fail)
    {
        var layout = new CStruct("struct root { char payload[" + size + "]; };", aligned: true);
        using var returns = new PoolReturnListener();
        using var source = new ObservedStream(size, returns, fail);
        if (fail)
        {
            // The source observes the rental before failing, so cleanup must return this specific buffer.
            CStructReadException error = Assert.Throws<CStructReadException>(() => layout.Parse(source, "root"));
            Assert.AreSame(source.Failure, error.InnerException);
        }
        else
        {
            Assert.AreEqual(size, layout.Parse(source, "root").Get<string>("payload").Length);
            Assert.AreEqual((long)size + 1, source.Position);
        }

        if (size <= StaticReadPlan.MaximumBlockSize)
        {
            Assert.AreEqual(1, source.BlockReads);
            Assert.AreEqual(0, source.ByteReads);
            Assert.AreEqual(StaticReadPlan.MaximumBlockSize, source.RentalLength);
            Assert.IsTrue(returns.Returned, "The observed fixed-block rental must be returned before parsing exits.");
        }
        else
        {
            Assert.AreEqual(0, source.BlockReads);
            Assert.AreEqual(size, source.ByteReads);
        }
    }

    /// <summary>A non-exposable input that records physical reads and the rental present when a block is requested.</summary>
    private sealed class ObservedStream : MemoryStream
    {
        private readonly PoolReturnListener returns;
        private readonly bool fail;

        /// <summary>Creates zero-filled input at byte origin one without exposing its buffer to the span fast path.</summary>
        /// <param name="size">The input byte count.</param>
        /// <param name="returns">The current-thread observer for the actual rented block.</param>
        /// <param name="fail">Whether span reads throw the stable physical failure.</param>
        public ObservedStream(int size, PoolReturnListener returns, bool fail)
            : base(new byte[size + 1])
        {
            this.returns = returns;
            this.fail = fail;
            this.Position = 1;
        }

        /// <summary>Gets the original physical read failure.</summary>
        public IOException Failure { get; } = new("Fixed block read failed.");

        /// <summary>Gets the number of span reads.</summary>
        public int BlockReads { get; private set; }

        /// <summary>Gets the number of scalar byte reads.</summary>
        public int ByteReads { get; private set; }

        /// <summary>Gets the actual rental length observed at the first span read.</summary>
        public int RentalLength { get; private set; }

        /// <summary>Observes the exact block before supplying bytes or throwing the configured failure.</summary>
        /// <param name="buffer">The destination span borrowed from the reader's rental.</param>
        /// <returns>The number of input bytes copied.</returns>
        /// <exception cref="IOException">The configured physical failure is enabled.</exception>
        public override int Read(Span<byte> buffer)
        {
            this.BlockReads++;
            if (this.BlockReads == 1)
            {
                this.RentalLength = this.returns.LastRentalLength;
                this.returns.Watch(this.returns.LastRental);
            }

            if (this.fail)
            {
                throw this.Failure;
            }

            return base.Read(buffer);
        }

        /// <summary>Counts ordinary character reads without changing the underlying stream behavior.</summary>
        /// <returns>The next byte, or -1 at the end of the input.</returns>
        public override int ReadByte()
        {
            this.ByteReads++;
            return base.ReadByte();
        }
    }
}
