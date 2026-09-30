namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;

/// <summary>
///     Checks that fixed record plans allocate no more than member-by-member reads, and the bounded stream
///     block and exact buffer return they read through.
/// </summary>
[TestClass]
public class StaticReadAllocationBoundaryTests
{
    /// <summary>Small character scratch buffers stay on the stack; larger ones move to the heap to bound stack growth.</summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void CharacterScratch_ChangesStorageAbove256CodeUnits()
    {
        int[] lengths = [255, 256, 257,];
        var allocations = new long[lengths.Length];
        for (int index = 0; index < lengths.Length; index++)
        {
            int length = lengths[index];
            var layout = new CStruct("struct root { char payload[" + length + "]; };");
            var bytes = new byte[length];
            for (int offset = 0; offset < bytes.Length; offset++)
            {
                bytes[offset] = (byte)offset;
            }

            Assert.AreEqual(Encoding.Latin1.GetString(bytes), layout.Parse(bytes.AsSpan(), "root").Get<string>("payload"));
            allocations[index] = Measure(layout, bytes, false);
        }

        // Eight parses may grow their result strings slightly at 256, but must not add a heap scratch array yet.
        Assert.IsTrue(allocations[1] <= allocations[0] + (8 * 32), $"Small scratch allocations: {allocations[0]}, {allocations[1]}.");

        // Above the boundary, each parse adds at least 257 UTF-16 code units of heap scratch storage.
        Assert.IsTrue(allocations[2] >= allocations[1] + (8 * 514), $"Large scratch allocations: {allocations[1]}, {allocations[2]}.");
    }

    /// <summary>
    ///     Both a fixed root and a runtime-count array of fixed records allocate no more through their static plans than
    ///     member by member. Member by member, the compiled engine stores each record's members straight
    ///     into its value just as the plan does, so for the array of fixed records the two allocate the same; the fixed
    ///     root allocates less through its plan.
    /// </summary>
    /// <param name="runtimeCount">Whether the array count comes from an input field rather than the declaration.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void FixedRecords_AllocateNoMoreThanMemberByMemberReads(bool runtimeCount)
    {
        string root = runtimeCount
            ? "struct root { uint16 count; record items[count]; uint8 tail; };"
            : "struct root { record items[256]; uint8 tail; };";
        var layout = new CStruct("struct record { uint8 first; uint8 second; };" + root, isLittleEndian: true);
        byte[] bytes = new byte[(256 * 2) + 1 + (runtimeCount ? 2 : 0)];
        if (runtimeCount)
        {
            bytes[1] = 1;
        }

        bytes[^1] = 99;
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(256, ((IEnumerable<object?>)parsed.items).Count());
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        long planned = Measure(layout, bytes, false);
        long memberByMember = Measure(layout, bytes, true);
        Assert.IsTrue(planned <= memberByMember, $"Fixed record plans allocated {planned} bytes; member-by-member reads allocated {memberByMember} bytes.");

        // A valid count or depth exactly at its limit should retain the same fixed-record allocation savings.
        var generous = new ReadOptions { MaxNestingDepth = 3, MaxArrayElements = 257, };
        var boundary = new ReadOptions { MaxNestingDepth = 2, MaxArrayElements = 256, };
        Assert.AreEqual(Measure(layout, bytes, false, generous), Measure(layout, bytes, false, boundary));
    }

    /// <summary>An aligned byte record at a nonzero origin uses one bounded block and returns it even after failure.</summary>
    /// <param name="size">The fixed character field's byte extent.</param>
    /// <param name="fail">Whether the physical block read throws before returning bytes.</param>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(65536, false)]
    [DataRow(65536, true)]
    [DataRow(65537, false)]
    [TestCategory(TestCategories.Allocation)]
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

        if (size <= ReadBlock.Size)
        {
            Assert.AreEqual(1, source.BlockReads);
            Assert.AreEqual(0, source.ByteReads);
            Assert.AreEqual(ReadBlock.Size, source.RentalLength);
            Assert.IsTrue(returns.Returned, "The observed fixed-block rental must be returned before parsing exits.");
        }
        else
        {
            Assert.AreEqual(0, source.BlockReads);
            Assert.AreEqual(size, source.ByteReads);
        }
    }

    /// <summary>Warms one decoding mode, then measures steady-state allocation.</summary>
    /// <param name="layout">The prepared record layout, excluded from the measured allocation.</param>
    /// <param name="bytes">The complete input, shared unchanged by both decoding modes.</param>
    /// <param name="disabled">Whether to read fixed composites member by member (<see cref="ExecutionPath.NoFastPaths"/>).</param>
    /// <param name="options">Optional read limits; construction is outside the measurement.</param>
    /// <returns>The smallest current-thread allocation across three batches of eight completed parses.</returns>
    private static long Measure(CStruct layout, byte[] bytes, bool disabled, ReadOptions? options = null)
    {
        if (disabled)
        {
            options = (options ?? new ReadOptions()) with { ExecutionPath = ExecutionPath.NoFastPaths };
        }

        for (int repeat = 0; repeat < 4; repeat++)
        {
            GC.KeepAlive(layout.Parse(bytes.AsSpan(), "root", options: options));
        }

        // Runtime initialization can add a one-off allocation after warm-up. Repeated batches isolate
        // steady-state decoder work without relaxing the exact comparison or scratch-size boundaries.
        long minimum = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 8; repeat++)
            {
                GC.KeepAlive(layout.Parse(bytes.AsSpan(), "root", options: options));
            }

            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return minimum;
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
