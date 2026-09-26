namespace CStructSharp.Comparison;

using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Comparison.FlatBuffers;
using CStructSharp.Comparison.Model;
using FlatSharp;
using Kaitai;
using MemoryPack;
using MessagePack;

/// <summary>
///     Deserializes the sample record: turn one encoded record into values and read every member once (the
///     <see cref="Fingerprints" /> consumer, identical for every case). <c>SameBytes</c> cases read the 79-byte C
///     layout; <c>OwnFormat</c> cases read the bytes their own library wrote (see <see cref="Payloads" />).
/// </summary>
/// <remarks>
///     Method names are the keys that <c>tools/quality/comparison-benchmarks.mjs</c> uses to place each result in the
///     README tables; rename both together.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class DeserializeBenchmarks
{
    private Payloads payloads = null!;
    private CStruct runtimeLayout = null!;
    private ReadingAccessors accessors = null!;
    private MemoryStream layoutStream = null!;
    private BinaryReader layoutReader = null!;

    /// <summary>Encodes the payloads and prepares the reusable reader state (the runtime layout, the stream).</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.payloads = new Payloads();
        this.runtimeLayout = ReadingLayout.Layout;
        this.accessors = new ReadingAccessors(this.runtimeLayout);
        this.layoutStream = new MemoryStream(this.payloads.Layout, writable: false);
        this.layoutReader = new BinaryReader(this.layoutStream);
    }

    /// <summary>Releases the reader created by <see cref="Setup" />.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.layoutReader.Dispose();

    /// <summary>CStructSharp generated view: members are decoded from the bytes when read, nothing is allocated.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong CStructSharp_GeneratedView() => Fingerprints.Of(new ReadingLayout.ReadingView(this.payloads.Layout));

    /// <summary>CStructSharp generated <c>Parse</c> into the generated class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong CStructSharp_GeneratedParse() => Fingerprints.Of(ReadingLayout.Parse(this.payloads.Layout));

    /// <summary>CStructSharp runtime <c>ReadValue&lt;T&gt;</c>: the interpreted reader, mapped into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong CStructSharp_RuntimeReadValue() => Fingerprints.Of(this.runtimeLayout.ReadValue<ReadingDto>(this.payloads.Layout, "reading"));

    /// <summary>CStructSharp runtime <c>Parse</c> into an untyped <c>StructValue</c>, members read by path.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong CStructSharp_RuntimeParse() => Fingerprints.Of(this.runtimeLayout.Parse(this.payloads.Layout, "reading"));

    /// <summary>CStructSharp runtime view: <c>CreateView</c>, then every member read through a prepared accessor, decoded from the bytes.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong CStructSharp_RuntimeView() => Fingerprints.Of(this.runtimeLayout.CreateView(this.payloads.Layout, "reading"), this.accessors);

    /// <summary>CStructSharp runtime <c>Parse</c> into a <c>StructValue</c>, members read through prepared accessors instead of path strings.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong CStructSharp_RuntimeParseAccessors() => Fingerprints.Of(this.runtimeLayout.Parse(this.payloads.Layout, "reading"), this.accessors);

    /// <summary>Hand-written <c>BinaryPrimitives</c> reader into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong HandWritten_BinaryPrimitives() => Fingerprints.Of(HandWrittenCodec.Read(this.payloads.Layout));

    /// <summary><c>MemoryMarshal.Read</c>: copies the bytes into an unmanaged struct (native byte order).</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong Stock_MemoryMarshal()
    {
        BlittableReading reading = MemoryMarshal.Read<BlittableReading>(this.payloads.Layout);
        return Fingerprints.Of(reading);
    }

    /// <summary><c>BinaryReader</c> over a reused <c>MemoryStream</c>, into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong Stock_BinaryReader()
    {
        this.layoutStream.Position = 0;
        BinaryReader reader = this.layoutReader;
        var reading = new ReadingDto
        {
            Id = reader.ReadUInt32(),
            Timestamp = reader.ReadInt64(),
            Position = new Vec3Dto { X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle() },
            Velocity = new Vec3Dto { X = reader.ReadSingle(), Y = reader.ReadSingle(), Z = reader.ReadSingle() },
            Flags = reader.ReadUInt16(),
            Kind = reader.ReadByte(),
            Value = reader.ReadDouble(),
            Samples = new int[8],
        };
        for (int index = 0; index < reading.Samples.Length; index++)
        {
            reading.Samples[index] = reader.ReadInt32();
        }

        return Fingerprints.Of(reading);
    }

    /// <summary><c>Marshal.PtrToStructure</c>: the interop marshaller copies the pinned bytes into a struct.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public unsafe ulong Stock_MarshalPtrToStructure()
    {
        fixed (byte* source = this.payloads.Layout)
        {
            InteropReading reading = Marshal.PtrToStructure<InteropReading>((nint)source);
            return Fingerprints.Of(reading);
        }
    }

    /// <summary>Kaitai Struct: the reader compiled from <c>Kaitai/sensor_reading.ksy</c>, over a new stream.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public ulong Kaitai_Struct() => Fingerprints.Of(new SensorReading(new KaitaiStream(this.payloads.Layout)));

    /// <summary>MemoryPack into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public ulong Own_MemoryPack() => Fingerprints.Of(MemoryPackSerializer.Deserialize<ReadingDto>(this.payloads.MemoryPack)!);

    /// <summary>MessagePack-CSharp into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public ulong Own_MessagePack() => Fingerprints.Of(MessagePackSerializer.Deserialize<ReadingDto>(this.payloads.MessagePack));

    /// <summary>protobuf-net into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public ulong Own_ProtobufNet() => Fingerprints.Of(ProtoBuf.Serializer.Deserialize<ReadingDto>((ReadOnlySpan<byte>)this.payloads.Protobuf));

    /// <summary>FlatSharp in lazy mode: a table over the buffer whose members are decoded when read.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public ulong Own_FlatSharp() => Fingerprints.Of(FlatReading.Serializer.Parse(this.payloads.FlatBuffers));

    /// <summary>System.Text.Json with source-generated metadata into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public ulong Own_SystemTextJson() => Fingerprints.Of(JsonSerializer.Deserialize((ReadOnlySpan<byte>)this.payloads.Json, ReadingJsonContext.Default.ReadingDto)!);
}
