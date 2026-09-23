namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks codec byte counts, bounded scratch-window growth and caller-visible value handling.</summary>
[TestClass]
public class CustomCodecBoundaryTests
{
    /// <summary>A fixed-size custom array is skipped arithmetically when selecting a later field.</summary>
    [TestMethod]
    public void FixedCustomArray_DoesNotDecodeUnselectedElements()
    {
        var codec = new RecordingCodec(1, 1);
        var layout = new CStruct("struct root { recorded values[2]; uint8 tail; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        using var source = new MemoryStream(new byte[] { 11, 12, 99, });
        Assert.AreEqual(2L, layout.ResolveAddress(source, "root.tail"));
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail", options: new ReadOptions { MaxTotalBytesRead = 1, }));
        Assert.IsEmpty(codec.ReadWindows);
    }

    /// <summary>Unnamed padding is zero storage even when a custom codec would encode a supplied zero differently.</summary>
    /// <param name="runtime">Whether a preceding runtime array prevents a whole-record static write plan.</param>
    /// <param name="array">Whether the padding contains three elements rather than one scalar.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void CustomPadding_WritesZeroStorageWithoutInvokingTheCodec(bool runtime, bool array)
    {
        var codec = new RecordingCodec(1, 1);
        string preceding = runtime ? "uint8 count; uint8 data[count]; " : string.Empty;
        string padding = array ? "_[3]" : "_";
        var layout = new CStruct("struct root { " + preceding + "uint8 prefix; recorded " + padding + "; uint8 tail; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        var data = new Dictionary<string, object?> { ["prefix"] = (byte)9, ["tail"] = (byte)7, };
        if (runtime)
        {
            data["count"] = (byte)0;
            data["data"] = Array.Empty<byte>();
        }

        byte[] expected = new byte[(runtime ? 1 : 0) + (array ? 3 : 1) + 2];
        expected[runtime ? 1 : 0] = 9;
        expected[^1] = 7;
        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
        using var destination = new MemoryStream();
        layout.Write(destination, "root", data);
        CollectionAssert.AreEqual(expected, destination.ToArray());
        using var existing = new MemoryStream(Enumerable.Repeat((byte)0xcc, expected.Length).ToArray());
        layout.Write(existing, "root", data);
        CollectionAssert.AreEqual(expected, existing.ToArray());
        Assert.IsEmpty(codec.WrittenValues);
    }

    /// <summary>A full codec window needs no extra zero-byte read from the caller-owned stream.</summary>
    [TestMethod]
    public void FullReadWindow_DoesNotTouchTheSourceAgain()
    {
        var codec = new RecordingCodec(4, 4);
        using var source = new NoEmptyReadStream();
        Assert.AreEqual((byte)17, CustomCodecAdapter.Read(codec, source));
        Assert.AreEqual(4L, source.Position);
        CollectionAssert.AreEqual(new[] { 4, }, codec.ReadWindows);
    }

    /// <summary>Growing scratch windows are returned between attempts, and a successful read releases its final rental.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void ReadGrowth_ReturnsEveryScratchWindow()
    {
        using var returns = new PoolReturnListener();
        var codec = new RecordingCodec(300, null, returns: returns);
        using var stream = new MemoryStream(new byte[512]);
        Assert.AreEqual((byte)17, CustomCodecAdapter.Read(codec, stream));
        Assert.AreEqual(300L, stream.Position);
        Assert.IsTrue(returns.Returned, "The final read window belongs to the adapter and is returned before success.");
        CollectionAssert.AreEqual(new[] { 256, 512, }, codec.ReadWindows);
    }

    /// <summary>A growing encoder releases old windows but transfers the successful final buffer to its caller.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void WriteGrowth_TransfersOnlyTheSuccessfulRental()
    {
        using var returns = new PoolReturnListener();
        var codec = new RecordingCodec(300, null, returns: returns);
        byte[] buffer = CustomCodecAdapter.EncodeToRented(codec, (byte)17, 512, out int written);
        try
        {
            Assert.AreEqual(300, written);
            Assert.AreEqual(buffer.GetHashCode(), returns.LastRental);
            Assert.IsFalse(returns.Returned, "The returned bytes must remain owned by the caller until it returns them.");
            CollectionAssert.AreEqual(new[] { 256, 512, }, codec.WriteWindows);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        Assert.IsTrue(returns.Returned);
    }

    /// <summary>Read and write limit failures return the scratch window instead of transferring or leaking it.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void LimitFailures_ReturnTheLastScratchWindow()
    {
        using var returns = new PoolReturnListener();
        var codec = new RecordingCodec(300, null, returns: returns);
        using var stream = new MemoryStream(new byte[512]);
        using var budget = new ReadBudgetStream(stream, new ReadOptions { MaxStringBytes = 256, });

        // The adapter owns the read rental even when the value cannot fit its byte limit.
        Assert.Throws<CStructReadLimitException>(() => CustomCodecAdapter.Read(codec, budget));
        Assert.IsTrue(returns.Returned);

        // A failed encoder transfers no buffer to its caller, so it must return the write rental itself.
        Assert.Throws<CStructWriteLimitException>(() => CustomCodecAdapter.EncodeToRented(codec, (byte)17, 256, out _));
        Assert.IsTrue(returns.Returned);
    }

    /// <summary>The stream-writing adapter returns the encoded rental after copying the value into the destination.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void StreamWrite_ReturnsItsEncodedBuffer()
    {
        using var returns = new PoolReturnListener();
        var codec = new RecordingCodec(4, 4, returns: returns);
        using var stream = new MemoryStream();
        CustomCodecAdapter.Write(codec, stream, (byte)17);
        Assert.IsTrue(returns.Returned);
        CollectionAssert.AreEqual(new byte[] { 17, 17, 17, 17, }, stream.ToArray());
    }

    /// <summary>A successful codec may consume or write zero bytes without being mistaken for an invalid count.</summary>
    [TestMethod]
    public void ZeroByteSuccess_IsAcceptedForMemoryStreamsAndWrites()
    {
        var codec = new RecordingCodec(0, 4);
        Assert.IsNull(CustomCodecAdapter.DecodeFromMemory(codec, new byte[4], out object? value, out int consumed));
        Assert.AreEqual((byte)17, value);
        Assert.AreEqual(0, consumed);
        using var stream = new MemoryStream(new byte[8]) { Position = 2, };
        Assert.AreEqual((byte)17, CustomCodecAdapter.Read(codec, stream));
        Assert.AreEqual(2L, stream.Position);
        byte[] buffer = CustomCodecAdapter.EncodeToRented(codec, (byte)17, 4, out int written);
        try
        {
            Assert.AreEqual(0, written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The advertised fixed size is the initial codec window, not the default variable-size capacity.</summary>
    [TestMethod]
    public void FixedSize_ControlsTheFirstReadAndWriteWindows()
    {
        var codec = new RecordingCodec(4, 4);
        using var stream = new MemoryStream(new byte[512]);
        Assert.AreEqual((byte)17, CustomCodecAdapter.Read(codec, stream));
        CollectionAssert.AreEqual(new[] { 4, }, codec.ReadWindows);
        Assert.AreEqual(4L, stream.Position);
        byte[] buffer = CustomCodecAdapter.EncodeToRented(codec, (byte)17, 512, out int written);
        try
        {
            Assert.AreEqual(4, written);
            CollectionAssert.AreEqual(new[] { 4, }, codec.WriteWindows);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Negative counts and counts beyond the supplied window are categorized before seeking or returning a buffer.</summary>
    /// <param name="reported">The invalid byte count reported by an otherwise successful codec.</param>
    [TestMethod]
    [DataRow(-1)]
    [DataRow(5)]
    public void InvalidSuccessfulCounts_AreRejected(int reported)
    {
        var codec = new RecordingCodec(4, 4, reported);
        using var stream = new MemoryStream(new byte[8]);
        CStructReadException read = Assert.Throws<CStructReadException>(() => CustomCodecAdapter.Read(codec, stream));
        StringAssert.Contains(read.Message, $"reported {reported} bytes consumed");
        CStructWriteException write = Assert.Throws<CStructWriteException>(() => CustomCodecAdapter.EncodeToRented(codec, (byte)17, 512, out _));
        StringAssert.Contains(write.Message, $"reported {reported} bytes written");
    }

    /// <summary>Growth stops exactly at a non-power-of-two byte limit and does not silently grant a larger window.</summary>
    [TestMethod]
    public void VariableWindows_StopAtTheExactByteLimit()
    {
        var codec = new RecordingCodec(301, null);
        using var source = new MemoryStream(new byte[512]);
        using var budget = new ReadBudgetStream(source, new ReadOptions { MaxStringBytes = 300, });
        Assert.Throws<CStructReadLimitException>(() => CustomCodecAdapter.Read(codec, budget));
        CollectionAssert.AreEqual(new[] { 256, 300, }, codec.ReadWindows);
        Assert.Throws<CStructWriteLimitException>(() => CustomCodecAdapter.EncodeToRented(codec, (byte)17, 300, out _));
        CollectionAssert.AreEqual(new[] { 256, 300, }, codec.WriteWindows);
    }

    /// <summary>Models a source that reports an I/O failure if asked to read after its requested window is already full.</summary>
    private sealed class NoEmptyReadStream : MemoryStream
    {
        /// <summary>Creates enough bytes for one fixed-size value and a following value.</summary>
        public NoEmptyReadStream()
            : base(new byte[8])
        {
        }

        /// <summary>Rejects an unnecessary empty read and otherwise reads ordinary source bytes.</summary>
        /// <param name="buffer">The requested input window.</param>
        /// <returns>The number of bytes supplied.</returns>
        /// <exception cref="IOException">The caller requested an empty window.</exception>
        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                throw new IOException("An extra empty read reached the source.");
            }

            return base.Read(buffer);
        }
    }

    /// <summary>Records actual input and output windows while requesting one controlled value extent.</summary>
    private sealed class RecordingCodec : ICustomCodec
    {
        private readonly int required;
        private readonly int? reported;
        private readonly PoolReturnListener? returns;

        /// <summary>Creates a codec with an optional deliberately invalid successful byte count.</summary>
        /// <param name="required">Bytes needed before the operation can succeed.</param>
        /// <param name="fixedSize">Advertised initial window, or null for a growing window.</param>
        /// <param name="reported">Override for the successful byte count.</param>
        /// <param name="returns">Optional exact-rental observer for ownership tests.</param>
        public RecordingCodec(int required, int? fixedSize, int? reported = null, PoolReturnListener? returns = null)
        {
            this.required = required;
            this.FixedSize = fixedSize;
            this.reported = reported;
            this.returns = returns;
        }

        public string Name => "recorded";

        public int? FixedSize { get; }

        public int Alignment => 1;

        public List<int> ReadWindows { get; } = [];

        public List<int> WriteWindows { get; } = [];

        /// <summary>Gets the exact values supplied to each encoding attempt.</summary>
        public List<object?> WrittenValues { get; } = [];

        /// <summary>Records the window and returns a fixed marker only when all required bytes are available.</summary>
        /// <param name="source">The adapter's current input window.</param>
        /// <param name="value">The marker on success, otherwise null.</param>
        /// <param name="bytesConsumed">The configured count on success, otherwise zero.</param>
        /// <returns>Done or NeedMoreData according to the available window.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            this.RecordWindow(this.ReadWindows, source.Length);
            bool done = source.Length >= this.required;
            value = done ? (byte)17 : null;
            bytesConsumed = done ? this.reported ?? this.required : 0;
            return done ? OperationStatus.Done : OperationStatus.NeedMoreData;
        }

        /// <summary>Records the value and window, then fills the requested extent while keeping invalid counts separate from writes.</summary>
        /// <param name="destination">The adapter's current output window.</param>
        /// <param name="value">The exact value supplied by the caller or library writer.</param>
        /// <param name="bytesWritten">The configured count on success, otherwise zero.</param>
        /// <returns>Done or DestinationTooSmall according to the available window.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            this.WrittenValues.Add(value);
            this.RecordWindow(this.WriteWindows, destination.Length);
            bool done = destination.Length >= this.required;
            if (done)
            {
                destination[..this.required].Fill(17);
            }

            bytesWritten = done ? this.reported ?? this.required : 0;
            return done ? OperationStatus.Done : OperationStatus.DestinationTooSmall;
        }

        /// <summary>Checks that growth released the preceding rental, then watches the currently borrowed window.</summary>
        /// <param name="windows">The read or write window history for this operation.</param>
        /// <param name="length">The current window length in bytes.</param>
        private void RecordWindow(List<int> windows, int length)
        {
            if (this.returns is not null)
            {
                int rental = this.returns.LastRental;
                if (windows.Count > 0)
                {
                    Assert.IsTrue(this.returns.Returned, "Growing a codec window must return the preceding rental.");
                }

                this.returns.Watch(rental);
            }

            windows.Add(length);
        }
    }
}
