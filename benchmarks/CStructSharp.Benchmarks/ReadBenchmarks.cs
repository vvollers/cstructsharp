namespace CStructSharp.Benchmarks;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Runtime reads of small hand-written layouts: primitive arrays of 1 KiB and 1 MiB, nested structs with and without
///     alignment, a bounded pointer graph, a five-byte record with a counted array (parsed, and read into a mapped
///     class), and one selected scalar; from a reused stream, a new stream per call, or memory.
/// </summary>
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

    /// <summary>Compiles the layouts and creates their input streams and byte arrays.</summary>
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

    /// <summary>Disposes the input streams.</summary>
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

    /// <summary>Parses <c>uint8[1024]</c> from a stream.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact")]
    public StructValue ParsePrimitiveArray1KiB()
    {
        this.array1KiBStream.Position = 0;
        return this.array1KiBLayout.Parse(this.array1KiBStream, "root");
    }

    /// <summary>Parses <c>uint8[1048576]</c> from a stream, with read limits raised to allow it.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [InvocationCount(1)]
    public StructValue ParsePrimitiveArray1MiB()
    {
        this.array1MiBStream.Position = 0;
        return this.array1MiBLayout.Parse(
            this.array1MiBStream,
            "root",
            new Dictionary<string, int>(),
            this.largeArrayOptions);
    }

    /// <summary>Parses 16 five-byte structs and a tail, packed without padding.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public StructValue ParseNestedUnaligned()
    {
        this.nestedUnalignedStream.Position = 0;
        return this.nestedUnalignedLayout.Parse(this.nestedUnalignedStream, "root");
    }

    /// <summary>Parses the same structs with C alignment, so each struct is padded to eight bytes.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public StructValue ParseNestedAligned()
    {
        this.nestedAlignedStream.Position = 0;
        return this.nestedAlignedLayout.Parse(this.nestedAlignedStream, "root");
    }

    /// <summary>Parses a two-node linked list with a pointer-depth limit of two.</summary>
    /// <returns>The parsed root.</returns>
    [BenchmarkCategory("Impact")]
    [Benchmark]
    public StructValue ParseBoundedPointerGraph()
    {
        this.pointerGraphStream.Position = 0;
        return this.pointerGraphLayout.Parse(
            this.pointerGraphStream,
            "root",
            new Dictionary<string, int>(),
            new ReadOptions { MaxPointerDepth = 2, MaxTotalBytesRead = 32, });
    }

    /// <summary>Parses the five-byte record (a count and two children) from a reused stream.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [BenchmarkCategory("TypedRead")]
    public StructValue ParseSmallRoot()
    {
        this.typedStream.Position = 0;
        return this.typedLayout.Parse(this.typedStream, "root");
    }

    /// <summary>Parses the five-byte record from a memory stream created for each call.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [BenchmarkCategory("TypedRead", "MemoryIo")]
    public StructValue ParseSmallRootNewMemoryStream()
    {
        using var stream = new MemoryStream(this.typedBytes, writable: false);
        return this.typedLayout.Parse(stream, "root");
    }

    /// <summary>Parses the five-byte record from memory.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [BenchmarkCategory("TypedRead", "MemoryIo")]
    public StructValue ParseSmallRootMemory()
    {
        return this.typedLayout.Parse(this.typedBytes.AsSpan(), "root");
    }

    /// <summary>Reads the five-byte record into <see cref="TypedRoot"/> from a reused stream.</summary>
    /// <returns>The mapped record.</returns>
    [Benchmark]
    [BenchmarkCategory("TypedRead")]
    public TypedRoot ReadTypedSmallRoot()
    {
        this.typedStream.Position = 0;
        return this.typedLayout.ReadValue<TypedRoot>(this.typedStream, "root");
    }

    /// <summary>Reads the five-byte record into <see cref="TypedRoot"/> from memory.</summary>
    /// <returns>The mapped record.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "TypedRead", "MemoryIo")]
    public TypedRoot ReadTypedSmallRootMemory()
    {
        return this.typedLayout.ReadValue<TypedRoot>(this.typedBytes.AsSpan(), "root");
    }

    /// <summary>Reads <c>root.value</c> as a <see cref="ushort"/> from a memory stream created for each call.</summary>
    /// <returns>The value.</returns>
    [Benchmark]
    [BenchmarkCategory("ScalarRead", "MemoryIo")]
    public ushort ReadSelectedScalarTypedNewMemoryStream()
    {
        using var stream = new MemoryStream(this.scalarBytes, writable: false);
        return this.scalarLayout.ReadValue<ushort>(stream, "root.value");
    }

    /// <summary>Reads <c>root.value</c> as a <see cref="ushort"/> from memory.</summary>
    /// <returns>The value.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "ScalarRead", "MemoryIo")]
    public ushort ReadSelectedScalarTypedMemory()
    {
        return this.scalarLayout.ReadValue<ushort>(this.scalarBytes.AsSpan(), "root.value");
    }

    /// <summary>The mapped class of <c>struct child</c>.</summary>
    public sealed class TypedChild : ICStructMapped<TypedChild>
    {
        /// <summary>Gets or sets the child's 16-bit <c>value</c>.</summary>
        public ushort Value { get; set; }

        /// <summary>Creates a child from its parsed value.</summary>
        /// <param name="source">The parsed <c>child</c>.</param>
        /// <returns>The mapped child.</returns>
        public static TypedChild ReadFrom(StructValue source)
        {
            return new TypedChild { Value = source.Get<ushort>("value"), };
        }

        /// <summary>Copies a child into a value for writing.</summary>
        /// <param name="value">The mapped child.</param>
        /// <param name="target">The <c>child</c> value to fill.</param>
        public static void WriteTo(TypedChild value, StructValue target)
        {
            target["value"] = value.Value;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<TypedChild>();
        }
    }

    /// <summary>The mapped class of <c>struct root</c>: a count and that many children.</summary>
    public sealed class TypedRoot : ICStructMapped<TypedRoot>
    {
        /// <summary>Gets or sets the <c>count</c> byte that sizes the child array.</summary>
        public byte Count { get; set; }

        /// <summary>Gets or sets the count-controlled <c>children</c> array.</summary>
        public TypedChild[] Children { get; set; } = [];

        /// <summary>Creates a root from its parsed value.</summary>
        /// <param name="source">The parsed <c>root</c>.</param>
        /// <returns>The mapped root.</returns>
        public static TypedRoot ReadFrom(StructValue source)
        {
            return new TypedRoot { Count = source.Get<byte>("count"), Children = source.Get<TypedChild[]>("children"), };
        }

        /// <summary>Copies a root into a value for writing.</summary>
        /// <param name="value">The mapped root.</param>
        /// <param name="target">The <c>root</c> value to fill.</param>
        public static void WriteTo(TypedRoot value, StructValue target)
        {
            target["count"] = value.Count;
            target["children"] = value.Children;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<TypedRoot>();
        }
    }
}
