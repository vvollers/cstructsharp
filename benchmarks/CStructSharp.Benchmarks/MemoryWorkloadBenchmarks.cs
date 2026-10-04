namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;
using CStructSharp.Memory;
using CStructSharp.Values;

/// <summary>Measures complete memory selections, owned arrays and schema preparation without a private image.</summary>
[BenchmarkCategory("Memory", "Impact")]
public class MemoryWorkloadBenchmarks
{
    private MemorySession session = null!;
    private MemoryRegion region = null!;
    private MemoryTypeDefinition[] repeated = null!;
    private MemoryTypeDefinition[] tiny = null!;

    /// <summary>Builds identical reusable inputs and checks the complete values and ownership outside timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        MemoryTypeDefinition scalar = new("u", "u", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        var fields = new MemoryField[273];
        for (int index = 0; index < fields.Length; index++)
        {
            fields[index] = new MemoryField("f" + index, "u", index);
        }

        this.tiny = [scalar,];
        this.repeated = new MemoryTypeDefinition[1000];
        for (int index = 0; index < this.repeated.Length; index++)
        {
            this.repeated[index] = new MemoryTypeDefinition("p" + index, "pointer", MemoryTypeKind.Pointer, 8);
        }

        this.session = new MemorySession(new MemorySchema([
            scalar,
            new("wide", "wide", MemoryTypeKind.Struct, fields.Length, fields),
            new("tiny", "tiny", MemoryTypeKind.Struct, 1, [new("f0", "u", 0),]),
            new("bytes16", "bytes16", MemoryTypeKind.Array, 16, elementTypeId: "u", count: 16),
            new("bytes4096", "bytes4096", MemoryTypeKind.Array, 4096, elementTypeId: "u", count: 4096),
        ]));
        byte[] input = new byte[4096];
        for (int index = 0; index < input.Length; index++)
        {
            input[index] = (byte)index;
        }

        this.region = new MemoryRegion(new ByteArrayMemorySource("image", input), 0, input.Length);
        if (!Equals(this.SelectWide(), (byte)16) || !Equals(this.SelectTiny(), (byte)0))
        {
            throw new InvalidOperationException("Incorrect member selection.");
        }

        var array = (PrimitiveArray<byte>)this.ReadBytes4096()!;
        if (!array.Span.SequenceEqual(input) || !this.session.Serialize("bytes4096", array).AsSpan().SequenceEqual(input))
        {
            throw new InvalidOperationException("Incomplete array result.");
        }

        array[0] = (byte)99;
        if (((PrimitiveArray<byte>)this.ReadBytes16()!).Span[0] != 0 || input[0] != 0)
        {
            throw new InvalidOperationException("Array result borrows storage.");
        }
    }

    /// <summary>Reads the last member of a 273-member struct by name.</summary>
    /// <returns>The owned scalar result.</returns>
    [Benchmark]
    public object? SelectWide() => this.session.Read(this.region, "wide", "f272");

    /// <summary>Reads the only member of a tiny struct by name.</summary>
    /// <returns>The owned scalar result.</returns>
    [Benchmark]
    public object? SelectTiny() => this.session.Read(this.region, "tiny", "f0");

    /// <summary>Reads a complete owned 16-byte array using individual source requests.</summary>
    /// <returns>The complete array.</returns>
    [Benchmark]
    public object? ReadBytes16() => this.session.Read(this.region, "bytes16");

    /// <summary>Reads a complete owned 4 KiB array using individual source requests.</summary>
    /// <returns>The complete array.</returns>
    [Benchmark]
    public object? ReadBytes4096() => this.session.Read(this.region, "bytes4096");

    /// <summary>Validates and prepares 1,000 distinct pointer definitions with identical encodings.</summary>
    /// <returns>The complete schema with every definition.</returns>
    [Benchmark]
    public MemorySchema CompileRepeatedScalars() => new(this.repeated);

    /// <summary>Prepares a one-scalar schema as a construction-cost control.</summary>
    /// <returns>The complete schema.</returns>
    [Benchmark]
    public MemorySchema CompileTinyScalar() => new(this.tiny);

    /// <summary>Serializes a scalar into a complete owned output as a writer control.</summary>
    /// <returns>The serialized byte.</returns>
    [Benchmark]
    public byte[] SerializeScalar() => this.session.Serialize("u", (byte)42);

    /// <summary>Plans a scalar replacement without applying it to the source.</summary>
    /// <returns>The patch with expected and replacement bytes.</returns>
    [Benchmark]
    public MemoryPatch PlanUpdateScalar() => this.session.PlanUpdate(this.region, "tiny", "f0", (byte)42);
}
