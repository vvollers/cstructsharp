namespace CStructSharp.Comparison.Variable;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Values;

/// <summary>
///     The general reader and writer on a record whose layout is not fixed (<see cref="PacketLayout"/>): the runtime
///     routes (a parsed <see cref="StructValue"/> read through accessors, from memory and from a stream, and a mapped
///     class), the generated class, and hand-written code. They show what the general reader and writer cost, and they
///     form the README's data-dependent record table.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class VariableBenchmarks
{
    private byte[] bytes = [];
    private CStruct runtimeLayout = null!;
    private PacketAccessors accessors = null!;
    private MemoryStream stream = null!;
    private PacketDto dto = null!;
    private PacketLayout.Packet generated = null!;
    private StructValue structValue = null!;

    /// <summary>Gets the reused destination of the writers; verification reads the output from its start.</summary>
    public byte[] Destination { get; } = new byte[256];

    /// <summary>Gets the reference bytes of the sample packet, as the hand-written writer encodes it.</summary>
    public byte[] Bytes => this.bytes;

    /// <summary>Encodes the sample packet and prepares the runtime layout, the accessors and the inputs.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.dto = PacketSample.CreateDto();
        var encoded = new byte[256];
        this.bytes = encoded[..PacketSample.Write(this.dto, encoded)];
        this.runtimeLayout = PacketLayout.Layout;
        this.accessors = new PacketAccessors(this.runtimeLayout);
        this.stream = new MemoryStream(this.bytes, writable: false);
        this.generated = PacketSample.ToGenerated(this.dto);
        this.structValue = this.runtimeLayout.Parse(this.bytes, "packet");
    }

    /// <summary>Runtime <c>Parse</c> from memory, members read through accessors.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableRead")]
    public ulong Runtime_Parse() => PacketSample.Of(this.runtimeLayout.Parse(this.bytes, "packet"), this.accessors);

    /// <summary>Runtime <c>Parse</c> from a <c>MemoryStream</c>, members read through accessors.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableRead")]
    public ulong Runtime_ParseStream()
    {
        this.stream.Position = 0;
        return PacketSample.Of(this.runtimeLayout.Parse(this.stream, "packet"), this.accessors);
    }

    /// <summary>Runtime <c>ReadValue&lt;T&gt;</c> into the mapped class (by name: the layout is not fixed).</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableRead")]
    public ulong Runtime_ReadValueMapped() => PacketSample.Of(this.runtimeLayout.ReadValue<PacketDto>(this.bytes, "packet"));

    /// <summary>Generated <c>Parse</c> into the generated class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableRead")]
    public ulong Generated_Parse() => PacketSample.Of(PacketLayout.Parse(this.bytes));

    /// <summary>Hand-written reader into the shared class.</summary>
    /// <returns>The fingerprint.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableRead")]
    public ulong HandWritten_Read() => PacketSample.Of(PacketSample.Read(this.bytes));

    /// <summary>Runtime <c>Serialize</c> of a parsed <c>StructValue</c> into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableWrite")]
    public int Runtime_SerializeStructValue() => this.runtimeLayout.Serialize(this.Destination, "packet", this.structValue);

    /// <summary>Runtime <c>Serialize</c> of the mapped class into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableWrite")]
    public int Runtime_SerializeMapped() => this.runtimeLayout.Serialize(this.Destination, "packet", this.dto);

    /// <summary>Generated <c>Serialize</c> of the generated class into a span.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableWrite")]
    public int Generated_Serialize() => PacketLayout.Serialize(this.generated, this.Destination);

    /// <summary>Hand-written writer from the shared class.</summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark]
    [BenchmarkCategory("VariableWrite")]
    public int HandWritten_Write() => PacketSample.Write(this.dto, this.Destination);
}
