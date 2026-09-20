namespace CStructSharp.Benchmarks;

using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.Baseline0;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The generated code against the runtime over the same fixtures: generated <c>Parse</c> (an object), the
///     generated view (a sum of fields, no allocation), the runtime <c>Parse</c>, and a hand-written reader where
///     one exists; the writers, a typed setter against the runtime's path update, and <c>ParseWithDebug</c>.
/// </summary>
[BenchmarkCategory("Generated")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class GeneratedBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private FixtureCase arrayU32 = null!;
    private FixtureCase png = null!;
    private FixtureCase strings = null!;
    private FixtureCase conditional = null!;
    private CStruct pointerGraphLayout = null!;
    private byte[] pointerGraphBytes = null!;
    private PrimRecordLayout.Root primRecordValue = null!;
    private NestedLayout.Root nestedValue = null!;
    private StructValue primRecordStructValue = null!;
    private StructValue nestedStructValue = null!;
    private byte[] updateTarget = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.arrayU32 = FixtureCase.Load("array-u32-le-262144");
        this.png = FixtureCase.Load("real-png");
        this.strings = FixtureCase.Load("strings-1024");
        this.conditional = FixtureCase.Load("cond-if128");
        this.pointerGraphLayout = new CStruct("struct node { node *next; uint8 value; }; struct root { node *head; };", pointerSize: 1);
        this.pointerGraphBytes = [0x01, 0x03, 0x11, 0x00, 0x22,];
        this.primRecordValue = PrimRecordLayout.Parse(this.primRecord.Bytes);
        this.nestedValue = NestedLayout.Parse(this.nested.Bytes);
        this.primRecordStructValue = this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");
        this.nestedStructValue = this.nested.Layout.Parse(this.nested.Bytes.AsSpan(), "root");
        this.updateTarget = (byte[])this.primRecord.Bytes.Clone();
    }

    // ---- prim-le-record: 28 bytes, seven scalars ------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_Parse() => this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("PrimRecord", "ReleaseGate")]
    public PrimRecordLayout.Root Generated_PrimRecord_Parse() => PrimRecordLayout.Parse(this.primRecord.Bytes);

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public double Generated_PrimRecord_View()
    {
        var view = new PrimRecordLayout.RootView(this.primRecord.Bytes);
        return view.A + view.B + view.C + view.D + view.E + view.F + (view.G ? 1 : 0);
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public double HandWritten_PrimRecord()
    {
        ReadOnlySpan<byte> bytes = this.primRecord.Bytes;
        return bytes[0]
               + BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(1))
               + BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(3))
               + BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(7))
               + BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(15))
               + BinaryPrimitives.ReadDoubleLittleEndian(bytes.Slice(19))
               + (bytes[27] != 0 ? 1 : 0);
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecordWrite")]
    public byte[] Runtime_PrimRecord_Serialize() => this.primRecord.Layout.Serialize("root", this.primRecordStructValue);

    [Benchmark]
    [BenchmarkCategory("PrimRecordWrite")]
    public byte[] Generated_PrimRecord_Serialize() => PrimRecordLayout.Serialize(this.primRecordValue);

    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public void Runtime_PrimRecord_Update() => this.primRecord.Layout.Update(this.updateTarget.AsSpan(), "root.c", 7u);

    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public void Generated_PrimRecord_Update() => PrimRecordLayout.Update.C(this.updateTarget, 7u);

    [Benchmark]
    [BenchmarkCategory("PrimRecordDebug")]
    public IReadOnlyList<DebugData> Runtime_PrimRecord_ParseWithDebug() => this.primRecord.Layout.ParseWithDebug(this.primRecord.Bytes.AsSpan(), "root").Debug;

    [Benchmark]
    [BenchmarkCategory("PrimRecordDebug")]
    public IReadOnlyList<DebugData> Generated_PrimRecord_ParseWithDebug() => PrimRecordLayout.ParseWithDebug(this.primRecord.Bytes).Debug;

    // ---- nested-x256: 256 × (2 × (2 leaves + tail) + mark) ---------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_Parse() => this.nested.Layout.Parse(this.nested.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("Nested256", "ReleaseGate")]
    public NestedLayout.Root Generated_Nested256_Parse() => NestedLayout.Parse(this.nested.Bytes);

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public long Generated_Nested256_View()
    {
        // A view exposes the statically placed members: the array's elements are reached by offset arithmetic here.
        ReadOnlySpan<byte> bytes = this.nested.Bytes;
        long sum = 0;
        for (int index = 0; index < 256; index++)
        {
            var top = new NestedLayout.TopView(bytes.Slice(index * NestedLayout.Sizes.Top));
            sum += top.Left.First.Value + top.Right.Second.Value + top.Mark;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public long HandWritten_Nested256()
    {
        ReadOnlySpan<byte> bytes = this.nested.Bytes;
        long sum = 0;
        for (int index = 0; index < 256; index++)
        {
            int offset = index * 25;
            sum += BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 1)) + BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 18)) + bytes[offset + 24];
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Nested256Write")]
    public byte[] Runtime_Nested256_Serialize() => this.nested.Layout.Serialize("root", this.nestedStructValue);

    [Benchmark]
    [BenchmarkCategory("Nested256Write")]
    public byte[] Generated_Nested256_Serialize() => NestedLayout.Serialize(this.nestedValue);

    // ---- array-u32-le-262144: one bulk array ------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ArrayU32")]
    public StructValue Runtime_ArrayU32_Parse() => this.arrayU32.Layout.Parse(this.arrayU32.Bytes.AsSpan(), "root", options: this.arrayU32.ReadOptions);

    [Benchmark]
    [BenchmarkCategory("ArrayU32")]
    public ArrayU32Layout.Root Generated_ArrayU32_Parse() => ArrayU32Layout.Parse(this.arrayU32.Bytes, this.arrayU32.ReadOptions);

    [Benchmark]
    [BenchmarkCategory("ArrayU32")]
    public ulong Generated_ArrayU32_View()
    {
        var view = new ArrayU32Layout.RootView(this.arrayU32.Bytes, this.arrayU32.ReadOptions);
        ulong sum = 0;
        for (int index = 0; index < 262144; index++)
        {
            sum += view.Values(index);
        }

        return sum;
    }

    // ---- real-png, strings-1024, cond-if128, the pointer graph ----------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Png")]
    public StructValue Runtime_Png_Parse() => this.png.Layout.Parse(this.png.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("Png")]
    public PngLayout.Root Generated_Png_Parse() => PngLayout.Parse(this.png.Bytes);

    [Benchmark]
    [BenchmarkCategory("Png")]
    public uint Generated_Png_View()
    {
        var view = new PngLayout.RootView(this.png.Bytes);
        return view.Ihdr.Width + view.Ihdr.Height + view.Ihdr.BitDepth;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Strings")]
    public StructValue Runtime_Strings_Parse() => this.strings.Layout.Parse(this.strings.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("Strings")]
    public StringsLayout.Root Generated_Strings_Parse() => StringsLayout.Parse(this.strings.Bytes);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Conditional")]
    public StructValue Runtime_Conditional_Parse() => this.conditional.Layout.Parse(this.conditional.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("Conditional")]
    public ConditionalLayout.Root Generated_Conditional_Parse() => ConditionalLayout.Parse(this.conditional.Bytes);

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PointerGraph")]
    public StructValue Runtime_PointerGraph_Parse() => this.pointerGraphLayout.Parse(this.pointerGraphBytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("PointerGraph")]
    public PointerGraphLayout.Root Generated_PointerGraph_Parse() => PointerGraphLayout.Parse(this.pointerGraphBytes);
}
