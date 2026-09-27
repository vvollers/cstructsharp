namespace CStructSharp.Benchmarks.Scenarios;

using System.Buffers;
using BenchmarkDotNet.Attributes;
using CStructSharp.Values;

/// <summary>S-WRITE: serialize from parsed dynamic data, plain dictionaries, and mapped classes into every destination shape.</summary>
[BenchmarkCategory("Scenario", "Write")]
public class WriteBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private FixtureCase strings1K = null!;
    private FixtureCase strings64K = null!;
    private StructValue primExpando = null!;
    private Dictionary<string, object> primDictionary = null!;
    private PathAndTypedBenchmarks.PrimRecord primPoco = null!;
    private StructValue nestedExpando = null!;
    private StructValue strings1KExpando = null!;
    private StructValue strings64KExpando = null!;
    private byte[] primDestination = null!;
    private ArrayBufferWriter<byte> bufferWriter = null!;
    private MemoryStream primStream = null!;

    /// <summary>
    ///     Loads the fixtures and builds each input shape: parsed values, a plain dictionary and a mapped class of the
    ///     28-byte record, and the destinations the span, buffer-writer and stream cases write into.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.strings1K = FixtureCase.Load("strings-1024");
        this.strings64K = FixtureCase.Load("strings-65536");
        this.primExpando = this.primRecord.ParseSpan();
        this.primDictionary = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> member in (IDictionary<string, object?>)this.primExpando)
        {
            this.primDictionary[member.Key] = member.Value!;
        }

        this.primPoco = this.primRecord.Layout.ReadValue<PathAndTypedBenchmarks.PrimRecord>(this.primRecord.Bytes.AsSpan(), "root");
        this.nestedExpando = this.nested.ParseSpan();
        this.strings1KExpando = this.strings1K.ParseSpan();
        this.strings64KExpando = this.strings64K.ParseSpan();
        this.primDestination = new byte[this.primRecord.Bytes.Length];
        this.bufferWriter = new ArrayBufferWriter<byte>(this.nested.Bytes.Length);
        this.primStream = new MemoryStream();
    }

    /// <summary>Releases the stream the stream-write case reuses.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.primStream.Dispose();
    }

    /// <summary>Serializes the record from its parsed <see cref="StructValue"/>.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] Serialize_Prim_Expando_ToArray()
    {
        return this.primRecord.Layout.Serialize("root", this.primExpando);
    }

    /// <summary>Serializes the record from a plain dictionary.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact")]
    public byte[] Serialize_Prim_Dictionary_ToArray()
    {
        return this.primRecord.Layout.Serialize("root", this.primDictionary);
    }

    /// <summary>Serializes the record from a mapped class into a new array.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("MemoryIo")]
    public byte[] Serialize_Prim_Poco_ToArray()
    {
        return this.primRecord.Layout.Serialize("root", this.primPoco);
    }

    /// <summary>Serializes the record from a mapped class into a caller-provided span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("Gate", "Impact", "MemoryIo")]
    public int Serialize_Prim_Poco_ToSpan()
    {
        return this.primRecord.Layout.Serialize(this.primDestination.AsSpan(), "root", this.primPoco);
    }

    /// <summary>Serializes the record from a mapped class into a reused <see cref="ArrayBufferWriter{T}"/>.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("Gate", "MemoryIo")]
    public long Serialize_Prim_Poco_ToBufferWriter()
    {
        this.bufferWriter.Clear();
        return this.primRecord.Layout.Serialize(this.bufferWriter, "root", this.primPoco);
    }

    /// <summary>Writes the prim record from a plain dictionary to a reused memory stream.</summary>
    /// <returns>The stream length after the write.</returns>
    [Benchmark]
    public long Write_Prim_Dictionary_ToStream()
    {
        this.primStream.SetLength(0);
        this.primStream.Position = 0;
        this.primRecord.Layout.Write(this.primStream, "root", this.primDictionary);
        return this.primStream.Length;
    }

    /// <summary>Serializes the 256 nested records from their parsed value.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] Serialize_Nested256_Expando_ToArray()
    {
        return this.nested.Layout.Serialize("root", this.nestedExpando);
    }

    /// <summary>Serializes the 256 nested records into a reused buffer writer.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    public long Serialize_Nested256_Expando_ToBufferWriter()
    {
        this.bufferWriter.Clear();
        return this.nested.Layout.Serialize(this.bufferWriter, "root", this.nestedExpando);
    }

    /// <summary>Serializes the fixed, terminated and wide strings of the 1 KiB strings fixture.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] Serialize_Strings1K_ToArray()
    {
        return this.strings1K.Layout.Serialize("root", this.strings1KExpando);
    }

    /// <summary>Serializes the strings of the 64 KiB strings fixture.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] Serialize_Strings64K_ToArray()
    {
        return this.strings64K.Layout.Serialize("root", this.strings64KExpando);
    }
}
