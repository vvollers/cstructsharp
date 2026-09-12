namespace CStructSharp.Benchmarks;

using System.Dynamic;
using BenchmarkDotNet.Attributes;

[BenchmarkCategory("Read")]
public class ReadBenchmarks
{
    private CStruct array1KiBLayout = null!;
    private CStruct array1MiBLayout = null!;
    private CStruct nestedUnalignedLayout = null!;
    private CStruct nestedAlignedLayout = null!;
    private CStruct pointerGraphLayout = null!;
    private CStruct scalarLayout = null!;
    private CStruct typedLayout = null!;
    private MemoryStream array1KiBStream = null!;
    private MemoryStream array1MiBStream = null!;
    private MemoryStream nestedUnalignedStream = null!;
    private MemoryStream nestedAlignedStream = null!;
    private MemoryStream pointerGraphStream = null!;
    private MemoryStream scalarStream = null!;
    private MemoryStream typedStream = null!;
    private byte[] scalarBytes = null!;
    private byte[] typedBytes = null!;
    private ReadOptions largeArrayOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.array1KiBLayout = new CStruct("struct root { uint8 values[1024]; };");
        this.array1MiBLayout = new CStruct("struct root { uint8 values[1048576]; };");
        this.nestedUnalignedLayout = new CStruct(
            "struct leaf { uint8 marker; uint32 value; }; struct root { leaf items[16]; uint16 tail; };");
        this.nestedAlignedLayout = new CStruct(
            "struct leaf { uint8 marker; uint32 value; }; struct root { leaf items[16]; uint16 tail; };",
            aligned: true);
        this.pointerGraphLayout = new CStruct(
            "struct node { node *next; uint8 value; }; struct root { node *head; };",
            pointerSize: 1);
        this.scalarLayout = new CStruct("struct root { uint16 value; };");
        this.typedLayout = new CStruct(
            "struct child { uint16 value; }; struct root { byte count; child children[count]; };");

        this.array1KiBStream = new MemoryStream(new byte[1024], writable: false);
        this.array1MiBStream = new MemoryStream(new byte[1024 * 1024], writable: false);
        this.nestedUnalignedStream = new MemoryStream(
            new byte[this.nestedUnalignedLayout.GetStructSizeInBytes("root")],
            writable: false);
        this.nestedAlignedStream = new MemoryStream(
            new byte[this.nestedAlignedLayout.GetStructSizeInBytes("root")],
            writable: false);
        this.pointerGraphStream = new MemoryStream(
            new byte[] { 0x01, 0x03, 0x11, 0x00, 0x22, },
            writable: false);
        this.scalarBytes = [0x34, 0x12,];
        this.typedBytes = [0x02, 0x34, 0x12, 0x78, 0x56,];
        this.scalarStream = new MemoryStream(this.scalarBytes, writable: false);
        this.typedStream = new MemoryStream(this.typedBytes, writable: false);
        this.largeArrayOptions = new ReadOptions
        {
            MaxArrayElements = 1024 * 1024,
            MaxTotalBytesRead = 2 * 1024 * 1024,
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.array1KiBStream.Dispose();
        this.array1MiBStream.Dispose();
        this.nestedUnalignedStream.Dispose();
        this.nestedAlignedStream.Dispose();
        this.pointerGraphStream.Dispose();
        this.scalarStream.Dispose();
        this.typedStream.Dispose();
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public ExpandoObject ParsePrimitiveArray1KiB()
    {
        this.array1KiBStream.Position = 0;
        return this.array1KiBLayout.ParseStream(this.array1KiBStream, "root");
    }

    [Benchmark]
    [InvocationCount(1)]
    public ExpandoObject ParsePrimitiveArray1MiB()
    {
        this.array1MiBStream.Position = 0;
        return this.array1MiBLayout.ParseStream(
            this.array1MiBStream,
            "root",
            new Dictionary<string, int>(),
            this.largeArrayOptions);
    }

    [Benchmark]
    public ExpandoObject ParseNestedUnaligned()
    {
        this.nestedUnalignedStream.Position = 0;
        return this.nestedUnalignedLayout.ParseStream(this.nestedUnalignedStream, "root");
    }

    [Benchmark]
    public ExpandoObject ParseNestedAligned()
    {
        this.nestedAlignedStream.Position = 0;
        return this.nestedAlignedLayout.ParseStream(this.nestedAlignedStream, "root");
    }

    [Benchmark]
    public ExpandoObject ParseBoundedPointerGraph()
    {
        this.pointerGraphStream.Position = 0;
        return this.pointerGraphLayout.ParseStream(
            this.pointerGraphStream,
            "root",
            new Dictionary<string, int>(),
            new ReadOptions { MaxPointerDepth = 2, MaxTotalBytesRead = 32, });
    }

    [Benchmark]
    public (List<DebugData> DebugData, dynamic Result) ParseWithDebug()
    {
        this.array1KiBStream.Position = 0;
        return this.array1KiBLayout.ParseStreamWithDebug(this.array1KiBStream, "root");
    }

    [Benchmark]
    [BenchmarkCategory("TypedRead")]
    public ExpandoObject ParseSmallRoot()
    {
        this.typedStream.Position = 0;
        return this.typedLayout.ParseStream(this.typedStream, "root");
    }

    [Benchmark]
    [BenchmarkCategory("TypedRead", "MemoryIo")]
    public ExpandoObject ParseSmallRootNewMemoryStream()
    {
        using var stream = new MemoryStream(this.typedBytes, writable: false);
        return this.typedLayout.ParseStream(stream, "root");
    }

    [Benchmark]
    [BenchmarkCategory("TypedRead", "MemoryIo", "ReleaseGate")]
    public ExpandoObject ParseSmallRootMemory()
    {
        return this.typedLayout.Parse(this.typedBytes.AsSpan(), "root");
    }

    [Benchmark]
    [BenchmarkCategory("TypedRead")]
    public TypedRoot ReadTypedSmallRoot()
    {
        this.typedStream.Position = 0;
        return this.typedLayout.ReadValue<TypedRoot>(this.typedStream, "root");
    }

    [Benchmark]
    [BenchmarkCategory("TypedRead", "MemoryIo", "ReleaseGate")]
    public TypedRoot ReadTypedSmallRootMemory()
    {
        return this.typedLayout.ReadValue<TypedRoot>(this.typedBytes.AsSpan(), "root");
    }

    [Benchmark]
    [BenchmarkCategory("ScalarRead")]
    public object? ReadSelectedScalarNatural()
    {
        this.scalarStream.Position = 0;
        return this.scalarLayout.ReadValue(this.scalarStream, "root.value");
    }

    [Benchmark]
    [BenchmarkCategory("ScalarRead")]
    public ushort ReadSelectedScalarTyped()
    {
        this.scalarStream.Position = 0;
        return this.scalarLayout.ReadValue<ushort>(this.scalarStream, "root.value");
    }

    [Benchmark]
    [BenchmarkCategory("ScalarRead", "MemoryIo")]
    public ushort ReadSelectedScalarTypedNewMemoryStream()
    {
        using var stream = new MemoryStream(this.scalarBytes, writable: false);
        return this.scalarLayout.ReadValue<ushort>(stream, "root.value");
    }

    [Benchmark]
    [BenchmarkCategory("ScalarRead", "MemoryIo", "ReleaseGate")]
    public ushort ReadSelectedScalarTypedMemory()
    {
        return this.scalarLayout.ReadValue<ushort>(this.scalarBytes.AsSpan(), "root.value");
    }

    public sealed class TypedChild
    {
        public ushort Value { get; set; }
    }

    public sealed class TypedRoot
    {
        public byte Count { get; set; }

        public TypedChild[] Children { get; set; } = [];
    }
}
