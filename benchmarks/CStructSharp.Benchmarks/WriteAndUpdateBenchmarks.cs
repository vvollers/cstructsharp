namespace CStructSharp.Benchmarks;

using System.Buffers;
using System.Dynamic;
using BenchmarkDotNet.Attributes;

[BenchmarkCategory("Write", "Update")]
public class WriteAndUpdateBenchmarks
{
    private CStruct objectLayout = null!;
    private CStruct primitiveUpdateLayout = null!;
    private CStruct bitfieldUpdateLayout = null!;
    private CStruct arrayUpdateLayout = null!;
    private CStruct pointerUpdateLayout = null!;
    private CStruct unionUpdateLayout = null!;
    private CStruct asciiLayout = null!;
    private CStruct utf8Layout = null!;
    private CStruct utf16LittleEndianLayout = null!;
    private CStruct utf16BigEndianLayout = null!;
    private ExpandoObject expandoData = null!;
    private Dictionary<string, object> dictionaryData = null!;
    private BenchmarkPoco pocoData = null!;
    private Dictionary<string, object> asciiData = null!;
    private Dictionary<string, object> utf8Data = null!;
    private Dictionary<string, object> utf16Data = null!;
    private byte[] primitiveUpdateData = null!;
    private byte[] bitfieldUpdateData = null!;
    private byte[] arrayUpdateData = null!;
    private byte[] pointerUpdateData = null!;
    private byte[] unionUpdateData = null!;
    private MemoryStream directWriteStream = null!;
    private MemoryStream primitiveUpdateStream = null!;
    private MemoryStream bitfieldUpdateStream = null!;
    private MemoryStream arrayUpdateStream = null!;
    private MemoryStream pointerUpdateStream = null!;
    private MemoryStream unionUpdateStream = null!;
    private byte[] spanDestination = null!;
    private ArrayBufferWriter<byte> bufferWriter = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.objectLayout = new CStruct(
            "struct root { uint32 id; uint16 count; uint8 samples[16]; };");
        this.primitiveUpdateLayout = new CStruct("struct root { uint32 value; };");
        this.bitfieldUpdateLayout = new CStruct("struct root { uint8 low:4; uint8 high:4; };");
        this.arrayUpdateLayout = new CStruct("struct root { uint8 values[256]; };");
        this.pointerUpdateLayout = new CStruct("struct root { uint16 *value; };", pointerSize: 1);
        this.unionUpdateLayout = new CStruct(
            "union choice { uint8 small; uint32 large; }; struct root { choice value; };");
        this.asciiLayout = new CStruct("struct root { char text[]; };");
        this.utf8Layout = new CStruct("struct root { utf8_string_zero text; };");
        this.utf16LittleEndianLayout = new CStruct("struct root { wchar< text[]; };");
        this.utf16BigEndianLayout = new CStruct("struct root { wchar> text[]; };");

        byte[] samples = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        dynamic expando = new ExpandoObject();
        expando.id = 0x12345678U;
        expando.count = (ushort)samples.Length;
        expando.samples = samples;
        this.expandoData = expando;
        this.dictionaryData = new Dictionary<string, object>
        {
            ["id"] = 0x12345678U,
            ["count"] = (ushort)samples.Length,
            ["samples"] = samples,
        };
        this.pocoData = new BenchmarkPoco
        {
            Id = 0x12345678U,
            Count = (ushort)samples.Length,
            Samples = samples,
        };

        this.asciiData = new Dictionary<string, object> { ["text"] = new string('A', 4096), };
        this.utf8Data = new Dictionary<string, object> { ["text"] = string.Concat(Enumerable.Repeat("Grüße世界", 512)), };
        this.utf16Data = new Dictionary<string, object> { ["text"] = string.Concat(Enumerable.Repeat("Hello世界", 512)), };

        this.primitiveUpdateData = new byte[4];
        this.bitfieldUpdateData = new byte[] { 0xA5, };
        this.arrayUpdateData = new byte[256];
        this.pointerUpdateData = new byte[] { 0x01, 0x00, 0x00, };
        this.unionUpdateData = new byte[4];
        this.directWriteStream = new MemoryStream();
        this.primitiveUpdateStream = new MemoryStream(this.primitiveUpdateData, writable: true);
        this.bitfieldUpdateStream = new MemoryStream(this.bitfieldUpdateData, writable: true);
        this.arrayUpdateStream = new MemoryStream(this.arrayUpdateData, writable: true);
        this.pointerUpdateStream = new MemoryStream(this.pointerUpdateData, writable: true);
        this.unionUpdateStream = new MemoryStream(this.unionUpdateData, writable: true);
        this.spanDestination = new byte[this.objectLayout.GetStructSizeInBytes("root")];
        this.bufferWriter = new ArrayBufferWriter<byte>(this.spanDestination.Length);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.directWriteStream.Dispose();
        this.primitiveUpdateStream.Dispose();
        this.bitfieldUpdateStream.Dispose();
        this.arrayUpdateStream.Dispose();
        this.pointerUpdateStream.Dispose();
        this.unionUpdateStream.Dispose();
    }

    [Benchmark]
    public byte[] SerializeExpando()
    {
        return this.objectLayout.Serialize("root", this.expandoData);
    }

    [Benchmark]
    public byte[] SerializeDictionary()
    {
        return this.objectLayout.Serialize("root", this.dictionaryData);
    }

    [Benchmark]
    [BenchmarkCategory("MemoryIo")]
    public byte[] SerializePoco()
    {
        return this.objectLayout.Serialize("root", this.pocoData);
    }

    [Benchmark]
    [BenchmarkCategory("MemoryIo", "ReleaseGate")]
    public int SerializePocoToSpan()
    {
        return this.objectLayout.Serialize(this.spanDestination.AsSpan(), "root", this.pocoData);
    }

    [Benchmark]
    [BenchmarkCategory("MemoryIo", "ReleaseGate")]
    public long SerializePocoToBufferWriter()
    {
        this.bufferWriter.Clear();
        return this.objectLayout.Serialize(this.bufferWriter, "root", this.pocoData);
    }

    [Benchmark]
    public long WriteStream()
    {
        this.directWriteStream.SetLength(0);
        this.directWriteStream.Position = 0;
        this.objectLayout.WriteStream(this.directWriteStream, "root", this.dictionaryData);
        return this.directWriteStream.Length;
    }

    [Benchmark]
    public byte UpdatePrimitive()
    {
        this.primitiveUpdateLayout.UpdateStream(this.primitiveUpdateStream, "root.value", 0x12345678U);
        return this.primitiveUpdateData[0];
    }

    [Benchmark]
    public byte UpdateLaterBitfield()
    {
        this.bitfieldUpdateLayout.UpdateStream(this.bitfieldUpdateStream, "root.high", 3);
        return this.bitfieldUpdateData[0];
    }

    [Benchmark]
    public byte UpdateIndexedArrayElement()
    {
        this.arrayUpdateLayout.UpdateStream(this.arrayUpdateStream, "root.values[127]", (byte)0x5A);
        return this.arrayUpdateData[127];
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public byte UpdatePointerTarget()
    {
        this.pointerUpdateLayout.UpdateStream(this.pointerUpdateStream, "root.value.value", (ushort)0xBEEF);
        return this.pointerUpdateData[1];
    }

    [Benchmark]
    public byte UpdateUnionMember()
    {
        this.unionUpdateLayout.UpdateStream(this.unionUpdateStream, "root.value.small", (byte)0x7E);
        return this.unionUpdateData[0];
    }

    [Benchmark]
    public byte[] SerializeTerminatedAscii()
    {
        return this.asciiLayout.Serialize("root", this.asciiData);
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public byte[] SerializeTerminatedUtf8()
    {
        return this.utf8Layout.Serialize("root", this.utf8Data);
    }

    [Benchmark]
    public byte[] SerializeTerminatedUtf16LittleEndian()
    {
        return this.utf16LittleEndianLayout.Serialize("root", this.utf16Data);
    }

    [Benchmark]
    public byte[] SerializeTerminatedUtf16BigEndian()
    {
        return this.utf16BigEndianLayout.Serialize("root", this.utf16Data);
    }

    public sealed class BenchmarkPoco
    {
        public ushort Count { get; init; }

        public uint Id { get; init; }

        public byte[] Samples { get; init; } = [];
    }
}
