namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>S-UPDATE: in-place field updates on small and large targets through the staging update stream.</summary>
[BenchmarkCategory("Baseline0", "Update")]
public class UpdateBenchmarks
{
    private FixtureCase prim = null!;
    private FixtureCase bitfield = null!;
    private FixtureCase array1K = null!;
    private FixtureCase array1M = null!;
    private FixtureCase pointer = null!;
    private FixtureCase union = null!;
    private MemoryStream primStream = null!;
    private MemoryStream bitfieldStream = null!;
    private MemoryStream array1KStream = null!;
    private MemoryStream array1MStream = null!;
    private MemoryStream pointerStream = null!;
    private MemoryStream unionStream = null!;
    private UpdateOptions largeArrayOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.prim = FixtureCase.Load("prim-le-record");
        this.bitfield = FixtureCase.Load("bitfield-x1k");
        this.array1K = FixtureCase.Load("array-u8-1024");
        this.array1M = FixtureCase.Load("array-u8-1048576");
        this.pointer = FixtureCase.Load("pointer-depth-1");
        this.union = FixtureCase.Load("union-x1k");
        this.primStream = Writable(this.prim);
        this.bitfieldStream = Writable(this.bitfield);
        this.array1KStream = Writable(this.array1K);
        this.array1MStream = Writable(this.array1M);
        this.pointerStream = Writable(this.pointer);
        this.unionStream = Writable(this.union);
        this.largeArrayOptions = new UpdateOptions
        {
            MaxTraversalArrayElements = 2 * this.array1M.Bytes.Length,
            MaxTraversalBytesRead = 4L * this.array1M.Bytes.Length,
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (MemoryStream stream in new[] { this.primStream, this.bitfieldStream, this.array1KStream, this.array1MStream, this.pointerStream, this.unionStream })
        {
            stream.Dispose();
        }
    }

    [Benchmark]
    public long Update_Scalar_4B()
    {
        this.primStream.Position = 0;
        this.prim.Layout.Update(this.primStream, "root.c", 0x12345678U);
        return this.primStream.Position;
    }

    [Benchmark]
    public long Update_Bitfield()
    {
        this.bitfieldStream.Position = 0;
        this.bitfield.Layout.Update(this.bitfieldStream, "root.items[3].b", 5);
        return this.bitfieldStream.Position;
    }

    [Benchmark]
    public long Update_ArrayElement_1K()
    {
        this.array1KStream.Position = 0;
        this.array1K.Layout.Update(this.array1KStream, "root.values[512]", (byte)0x5A);
        return this.array1KStream.Position;
    }

    [Benchmark]
    public long Update_ArrayElement_1M()
    {
        this.array1MStream.Position = 0;
        this.array1M.Layout.Update(this.array1MStream, "root.values[1048575]", (byte)0x5A, options: this.largeArrayOptions);
        return this.array1MStream.Position;
    }

    /// <summary>Updates the scalar inside the pointer target (the pointer-extent bug that blocked this path is fixed).</summary>
    [Benchmark]
    public long Update_PointerTarget()
    {
        this.pointerStream.Position = 0;
        this.pointer.Layout.Update(this.pointerStream, "root.head.value.value", 0xBEEFU);
        return this.pointerStream.Position;
    }

    [Benchmark]
    public long Update_UnionMember()
    {
        this.unionStream.Position = 0;
        this.union.Layout.Update(this.unionStream, "root.items[7].value.large", 0x7E7E7E7EU);
        return this.unionStream.Position;
    }

    private static MemoryStream Writable(FixtureCase fixture)
    {
        return new MemoryStream(fixture.Bytes.ToArray(), writable: true);
    }
}
