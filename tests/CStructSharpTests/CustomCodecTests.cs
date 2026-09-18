namespace CStructSharp.Tests;

using System.Buffers;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The span-based <see cref="ICustomCodec"/> contract as the library drives it: memory input hands the codec
///     the remaining bytes, a stream source gets a window that grows on <see cref="OperationStatus.NeedMoreData"/>,
///     writes grow their window on <see cref="OperationStatus.DestinationTooSmall"/>, and every misbehaviour is a
///     read or write error at the field.
/// </summary>
[TestClass]
public class CustomCodecTests
{
    private static readonly CStructCompilationOptions Options = new() { Codecs = [LengthPrefixed.Instance, Strict.Instance], };

    private static CStruct Layout => new("struct root { uint8 head; blob text; uint8 tail; };", compilationOptions: Options);

    /// <summary>The same variable-length value decodes from memory, a MemoryStream, and a fragmenting stream whose window must grow.</summary>
    [TestMethod]
    public void Read_VariableLength_FromMemoryAndStreams()
    {
        byte[] payload = Encoding.ASCII.GetBytes(new string('x', 700));
        byte[] bytes = [1, .. LengthPrefixed.Encode(payload), 2];

        StructValue fromMemory = Layout.Parse(bytes, "root");
        Assert.AreEqual(700, fromMemory.Get<string>("text").Length);
        Assert.AreEqual((byte)2, fromMemory.Get<byte>("tail"));

        using var memoryStream = new MemoryStream(bytes);
        Assert.AreEqual(700, Layout.Parse(memoryStream, "root").Get<string>("text").Length);
        Assert.AreEqual(bytes.Length, memoryStream.Position);

        using var chunked = new ChunkedMemoryStream(bytes, maximumReadSize: 7, writable: false);
        Assert.AreEqual(700, Layout.Parse(chunked, "root").Get<string>("text").Length);
        Assert.AreEqual(bytes.Length, chunked.Position);
        Assert.AreEqual(3L + 700, Layout.ResolveAddress(new MemoryStream(bytes), "root.tail"));
    }

    /// <summary>A value cut off by the end of the input is a read error naming the codec, from memory and from a stream.</summary>
    [TestMethod]
    public void Read_Truncated_IsReadError()
    {
        byte[] bytes = [1, 0x05, 0x00, (byte)'a', (byte)'b'];
        CStructReadException memory = Assert.Throws<CStructReadException>(() => Layout.Parse(bytes, "root"));
        StringAssert.Contains(memory.Message, "custom codec 'blob' needs more");
        StringAssert.Contains(memory.Message, "field 'text'");

        using var chunked = new ChunkedMemoryStream(bytes, maximumReadSize: 2, writable: false);
        CStructReadException stream = Assert.Throws<CStructReadException>(() => Layout.Parse(chunked, "root"));
        StringAssert.Contains(stream.Message, "custom codec 'blob' needs more");
    }

    /// <summary>InvalidData, a thrown exception, and an out-of-range consumed count are all read errors, never crashes or silent misreads.</summary>
    [TestMethod]
    public void Read_Misbehaviour_IsReadError()
    {
        var layout = new CStruct("struct root { strict value; };", compilationOptions: Options);
        StringAssert.Contains(Assert.Throws<CStructReadException>(() => layout.Parse([Strict.Invalid], "root")).Message, "rejected the input");
        StringAssert.Contains(Assert.Throws<CStructReadException>(() => layout.Parse([Strict.Throws], "root")).Message, "failed to decode a value: boom");
        StringAssert.Contains(Assert.Throws<CStructReadException>(() => layout.Parse([Strict.OverConsume], "root")).Message, "reported 9 bytes consumed");
        Assert.AreEqual((byte)7, layout.Parse([7], "root").Get<byte>("value"));
    }

    /// <summary>A value that needs more than MaxStringBytes from a stream source is a limit error rather than an unbounded window.</summary>
    [TestMethod]
    public void Read_WindowGrowth_StopsAtMaxStringBytes()
    {
        byte[] payload = new byte[600];
        byte[] bytes = [1, .. LengthPrefixed.Encode(payload), 2];
        using var chunked = new ChunkedMemoryStream(bytes, maximumReadSize: 64, writable: false);
        var limited = new ReadOptions { MaxStringBytes = 512, };
        Assert.Throws<CStructReadLimitException>(() => Layout.Parse(chunked, "root", options: limited));
        Assert.AreEqual(600, Layout.Parse(new ChunkedMemoryStream(bytes, 64, false), "root", options: new ReadOptions { MaxStringBytes = 1024, }).Get<string>("text").Length);
    }

    /// <summary>Writes round-trip through a growing window; DestinationTooSmall past the limit and InvalidData are write errors.</summary>
    [TestMethod]
    public void Write_GrowsWindowAndReportsFailures()
    {
        string text = new('y', 900);
        var value = new StructValue { ["head"] = 1, ["text"] = text, ["tail"] = 2 };
        byte[] bytes = Layout.Serialize("root", value);
        byte[] expected = [1, .. LengthPrefixed.Encode(Encoding.ASCII.GetBytes(text)), 2];
        CollectionAssert.AreEqual(expected, bytes);
        Assert.AreEqual(text, Layout.Parse(bytes, "root").Get<string>("text"));

        using var stream = new MemoryStream();
        Layout.Write(stream, "root", value);
        CollectionAssert.AreEqual(bytes, stream.ToArray());

        Assert.Throws<CStructWriteLimitException>(() => Layout.Serialize("root", value, options: new WriteOptions { MaxStringBytes = 512, }));
        var strict = new CStruct("struct root { strict value; };", compilationOptions: Options);
        StringAssert.Contains(Assert.Throws<CStructWriteException>(() => strict.Serialize("root", new StructValue { ["value"] = "bad" })).Message, "cannot encode the value bad");
        StringAssert.Contains(Assert.Throws<CStructWriteException>(() => strict.Serialize("root", new StructValue { ["value"] = "throw" })).Message, "failed to encode throw: boom");
        CollectionAssert.AreEqual(new byte[] { 7 }, strict.Serialize("root", new StructValue { ["value"] = (byte)7 }));
    }

    /// <summary>A 16-bit little-endian length prefix followed by that many ASCII bytes; decoded as a string.</summary>
    private sealed class LengthPrefixed : ICustomCodec
    {
        public static readonly LengthPrefixed Instance = new();

        public string Name => "blob";

        public int? FixedSize => null;

        public int Alignment => 1;

        public static byte[] Encode(byte[] payload)
        {
            return [(byte)payload.Length, (byte)(payload.Length >> 8), .. payload];
        }

        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = null;
            bytesConsumed = 0;
            if (source.Length < 2)
            {
                return OperationStatus.NeedMoreData;
            }

            int length = source[0] | (source[1] << 8);
            if (source.Length < 2 + length)
            {
                return OperationStatus.NeedMoreData;
            }

            value = Encoding.ASCII.GetString(source.Slice(2, length));
            bytesConsumed = 2 + length;
            return OperationStatus.Done;
        }

        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            byte[] payload = Encoding.ASCII.GetBytes((string)value);
            bytesWritten = 0;
            if (destination.Length < 2 + payload.Length)
            {
                return OperationStatus.DestinationTooSmall;
            }

            destination[0] = (byte)payload.Length;
            destination[1] = (byte)(payload.Length >> 8);
            payload.CopyTo(destination[2..]);
            bytesWritten = 2 + payload.Length;
            return OperationStatus.Done;
        }
    }

    /// <summary>A one-byte codec whose input byte selects how it misbehaves.</summary>
    private sealed class Strict : ICustomCodec
    {
        public const byte Invalid = 0xF0;
        public const byte Throws = 0xF1;
        public const byte OverConsume = 0xF2;
        public static readonly Strict Instance = new();

        public string Name => "strict";

        public int? FixedSize => 1;

        public int Alignment => 1;

        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = source[0];
            bytesConsumed = source[0] == OverConsume ? 9 : 1;
            return source[0] switch
            {
                Invalid => OperationStatus.InvalidData,
                Throws => throw new InvalidOperationException("boom"),
                _ => OperationStatus.Done,
            };
        }

        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 1;
            switch (value)
            {
                case "bad":
                    return OperationStatus.InvalidData;
                case "throw":
                    throw new InvalidOperationException("boom");
                default:
                    destination[0] = Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture);
                    return OperationStatus.Done;
            }
        }
    }
}
