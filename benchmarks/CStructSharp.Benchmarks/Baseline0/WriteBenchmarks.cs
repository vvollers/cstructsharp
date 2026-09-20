namespace CStructSharp.Benchmarks.Baseline0;

using System.Buffers;
using BenchmarkDotNet.Attributes;
using CStructSharp.Values;

/// <summary>S-WRITE: serialize from parsed dynamic data, plain dictionaries, and mapped classes into every destination shape.</summary>
[BenchmarkCategory("Baseline0", "Write")]
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
    }

    [Benchmark]
    public byte[] Serialize_Prim_Expando_ToArray()
    {
        return this.primRecord.Layout.Serialize("root", this.primExpando);
    }

    [Benchmark]
    public byte[] Serialize_Prim_Dictionary_ToArray()
    {
        return this.primRecord.Layout.Serialize("root", this.primDictionary);
    }

    [Benchmark]
    public byte[] Serialize_Prim_Poco_ToArray()
    {
        return this.primRecord.Layout.Serialize("root", this.primPoco);
    }

    [Benchmark]
    public int Serialize_Prim_Poco_ToSpan()
    {
        return this.primRecord.Layout.Serialize(this.primDestination.AsSpan(), "root", this.primPoco);
    }

    [Benchmark]
    public long Serialize_Prim_Poco_ToBufferWriter()
    {
        this.bufferWriter.Clear();
        return this.primRecord.Layout.Serialize(this.bufferWriter, "root", this.primPoco);
    }

    [Benchmark]
    public byte[] Serialize_Nested256_Expando_ToArray()
    {
        return this.nested.Layout.Serialize("root", this.nestedExpando);
    }

    [Benchmark]
    public long Serialize_Nested256_Expando_ToBufferWriter()
    {
        this.bufferWriter.Clear();
        return this.nested.Layout.Serialize(this.bufferWriter, "root", this.nestedExpando);
    }

    [Benchmark]
    public byte[] Serialize_Strings1K_ToArray()
    {
        return this.strings1K.Layout.Serialize("root", this.strings1KExpando);
    }

    [Benchmark]
    public byte[] Serialize_Strings64K_ToArray()
    {
        return this.strings64K.Layout.Serialize("root", this.strings64KExpando);
    }
}
