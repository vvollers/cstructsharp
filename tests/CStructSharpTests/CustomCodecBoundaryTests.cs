namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks codec byte-count contracts and bounded scratch-window growth independently of layout compilation.</summary>
[TestClass]
public class CustomCodecBoundaryTests
{
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

    /// <summary>Records actual input and output windows while requesting one controlled value extent.</summary>
    private sealed class RecordingCodec : ICustomCodec
    {
        private readonly int required;
        private readonly int? reported;

        /// <summary>Creates a codec with an optional deliberately invalid successful byte count.</summary>
        /// <param name="required">Bytes needed before the operation can succeed.</param>
        /// <param name="fixedSize">Advertised initial window, or null for a growing window.</param>
        /// <param name="reported">Override for the successful byte count.</param>
        public RecordingCodec(int required, int? fixedSize, int? reported = null)
        {
            this.required = required;
            this.FixedSize = fixedSize;
            this.reported = reported;
        }

        public string Name => "recorded";

        public int? FixedSize { get; }

        public int Alignment => 1;

        public List<int> ReadWindows { get; } = [];

        public List<int> WriteWindows { get; } = [];

        /// <summary>Records the window and returns a fixed marker only when all required bytes are available.</summary>
        /// <param name="source">The adapter's current input window.</param>
        /// <param name="value">The marker on success, otherwise null.</param>
        /// <param name="bytesConsumed">The configured count on success, otherwise zero.</param>
        /// <returns>Done or NeedMoreData according to the available window.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            this.ReadWindows.Add(source.Length);
            bool done = source.Length >= this.required;
            value = done ? (byte)17 : null;
            bytesConsumed = done ? this.reported ?? this.required : 0;
            return done ? OperationStatus.Done : OperationStatus.NeedMoreData;
        }

        /// <summary>Records the window and fills the requested extent, keeping deliberately invalid counts separate from writes.</summary>
        /// <param name="destination">The adapter's current output window.</param>
        /// <param name="value">Unused marker value.</param>
        /// <param name="bytesWritten">The configured count on success, otherwise zero.</param>
        /// <returns>Done or DestinationTooSmall according to the available window.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            this.WriteWindows.Add(destination.Length);
            bool done = destination.Length >= this.required;
            if (done)
            {
                destination[..this.required].Fill(17);
            }

            bytesWritten = done ? this.reported ?? this.required : 0;
            return done ? OperationStatus.Done : OperationStatus.DestinationTooSmall;
        }
    }
}
