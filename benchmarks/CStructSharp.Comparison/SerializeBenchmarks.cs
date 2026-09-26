namespace CStructSharp.Comparison;

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Comparison.FlatBuffers;
using CStructSharp.Comparison.Model;
using CStructSharp.Values;
using FlatSharp;
using MemoryPack;
using MessagePack;

/// <summary>
///     Serializes the sample record from each library's in-memory type into memory the benchmark owns and reuses:
///     a byte array for writers that fill a span, an <see cref="ArrayBufferWriter{T}" /> for writers that append.
///     Reusing the destination keeps the measurement on encoding rather than on allocating the output array.
///     <c>SameBytes</c> cases write the 79-byte C layout; <c>OwnFormat</c> cases write their own library's format.
/// </summary>
/// <remarks>
///     Every case returns the number of bytes it wrote. Method names are the keys that
///     <c>tools/quality/comparison-benchmarks.mjs</c> uses to place each result in the README tables.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SerializeBenchmarks
{
    private ReadingDto dto = null!;
    private ReadingLayout.Reading generated = null!;
    private StructValue structValue = null!;
    private BlittableReading blittable;
    private InteropReading interop;
    private FlatReading flat = null!;
    private CStruct runtimeLayout = null!;
    private MemoryStream layoutStream = null!;
    private BinaryWriter layoutWriter = null!;
    private Utf8JsonWriter jsonWriter = null!;

    /// <summary>Gets the reused destination of the span writers; verification reads the output from its start.</summary>
    public byte[] Destination { get; private set; } = [];

    /// <summary>Gets the reused destination of the appending writers; verification reads its written span.</summary>
    public ArrayBufferWriter<byte> BufferWriter { get; private set; } = new();

    /// <summary>Creates the record in every in-memory type and the reusable destinations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.dto = SampleRecord.CreateDto();
        this.generated = SampleRecord.ToGenerated(this.dto);
        this.blittable = SampleRecord.ToBlittable(this.dto);
        this.interop = SampleRecord.ToInterop(this.dto);
        this.flat = SampleRecord.ToFlat(this.dto);
        this.runtimeLayout = ReadingLayout.Layout;
        this.structValue = this.runtimeLayout.Parse(new Payloads().Layout, "reading");

        // Large enough for every format; FlatSharp states its own worst case for this value.
        this.Destination = new byte[Math.Max(1024, FlatReading.Serializer.GetMaxSize(this.flat))];
        this.BufferWriter = new ArrayBufferWriter<byte>(1024);
        this.layoutStream = new MemoryStream(this.Destination, writable: true);
        this.layoutWriter = new BinaryWriter(this.layoutStream);
        this.jsonWriter = new Utf8JsonWriter(this.BufferWriter);
    }

    /// <summary>Releases the writers created by <see cref="Setup" />.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.layoutWriter.Dispose();
        this.jsonWriter.Dispose();
    }

    /// <summary>CStructSharp generated <c>Serialize</c> from the generated class into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public int CStructSharp_GeneratedSerialize() => ReadingLayout.Serialize(this.generated, this.Destination);

    /// <summary>CStructSharp runtime <c>Serialize</c> of the shared class through its generated mapper, into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public int CStructSharp_RuntimeSerializeMapped() => this.runtimeLayout.Serialize(this.Destination, "reading", this.dto);

    /// <summary>CStructSharp runtime <c>Serialize</c> of an untyped <c>StructValue</c>, into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public int CStructSharp_RuntimeSerializeStructValue() => this.runtimeLayout.Serialize(this.Destination, "reading", this.structValue);

    /// <summary>Hand-written <c>BinaryPrimitives</c> writer from the shared class.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public int HandWritten_BinaryPrimitives() => HandWrittenCodec.Write(this.dto, this.Destination);

    /// <summary><c>MemoryMarshal.Write</c>: copies the unmanaged struct as raw memory (native byte order).</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public int Stock_MemoryMarshal()
    {
        MemoryMarshal.Write(this.Destination, in this.blittable);
        return SampleRecord.LayoutSize;
    }

    /// <summary><c>BinaryWriter</c> over a reused <c>MemoryStream</c>, from the shared class.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public int Stock_BinaryWriter()
    {
        this.layoutStream.Position = 0;
        BinaryWriter writer = this.layoutWriter;
        ReadingDto value = this.dto;
        writer.Write(value.Id);
        writer.Write(value.Timestamp);
        writer.Write(value.Position.X);
        writer.Write(value.Position.Y);
        writer.Write(value.Position.Z);
        writer.Write(value.Velocity.X);
        writer.Write(value.Velocity.Y);
        writer.Write(value.Velocity.Z);
        writer.Write(value.Flags);
        writer.Write(value.Kind);
        writer.Write(value.Value);
        foreach (int sample in value.Samples)
        {
            writer.Write(sample);
        }

        return (int)this.layoutStream.Position;
    }

    /// <summary><c>Marshal.StructureToPtr</c>: the interop marshaller copies the struct into the pinned destination.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("SameBytes")]
    public unsafe int Stock_MarshalStructureToPtr()
    {
        fixed (byte* destination = this.Destination)
        {
            Marshal.StructureToPtr(this.interop, (nint)destination, fDeleteOld: false);
        }

        return SampleRecord.LayoutSize;
    }

    /// <summary>MemoryPack from the shared class, appended to the reused buffer writer.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public int Own_MemoryPack()
    {
        this.BufferWriter.ResetWrittenCount();
        ArrayBufferWriter<byte> writer = this.BufferWriter;
        MemoryPackSerializer.Serialize(writer, this.dto);
        return writer.WrittenCount;
    }

    /// <summary>MessagePack-CSharp from the shared class, appended to the reused buffer writer.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public int Own_MessagePack()
    {
        this.BufferWriter.ResetWrittenCount();
        MessagePackSerializer.Serialize(this.BufferWriter, this.dto);
        return this.BufferWriter.WrittenCount;
    }

    /// <summary>protobuf-net from the shared class, appended to the reused buffer writer.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public int Own_ProtobufNet()
    {
        this.BufferWriter.ResetWrittenCount();
        ProtoBuf.Serializer.Serialize(this.BufferWriter, this.dto);
        return this.BufferWriter.WrittenCount;
    }

    /// <summary>FlatSharp from its generated table class into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public int Own_FlatSharp() => FlatReading.Serializer.Write(this.Destination, this.flat);

    /// <summary>System.Text.Json with source-generated metadata, through a reused <c>Utf8JsonWriter</c>.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("OwnFormat")]
    public int Own_SystemTextJson()
    {
        this.BufferWriter.ResetWrittenCount();
        this.jsonWriter.Reset(this.BufferWriter);
        JsonSerializer.Serialize(this.jsonWriter, this.dto, ReadingJsonContext.Default.ReadingDto);
        this.jsonWriter.Flush();
        return this.BufferWriter.WrittenCount;
    }
}
