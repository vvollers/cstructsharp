namespace CStructSharp.Benchmarks;

using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
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

    /// <summary>
    ///     Loads each fixture and checks it against its generated layout, then prepares the generated and runtime values
    ///     the write cases serialize and the buffer the update cases change.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.LoadMatching("prim-le-record", typeof(PrimRecordLayout));
        this.nested = FixtureCase.LoadMatching("nested-x256", typeof(NestedLayout));
        this.arrayU32 = FixtureCase.LoadMatching("array-u32-le-262144", typeof(ArrayU32Layout));
        this.png = FixtureCase.LoadMatching("real-png", typeof(PngLayout));
        this.strings = FixtureCase.LoadMatching("strings-1024", typeof(StringsLayout));
        this.conditional = FixtureCase.LoadMatching("cond-if128", typeof(ConditionalLayout));
        this.pointerGraphLayout = FixtureCase.CompileLike(typeof(PointerGraphLayout));
        this.pointerGraphBytes = [0x01, 0x03, 0x11, 0x00, 0x22,];
        this.primRecordValue = PrimRecordLayout.Parse(this.primRecord.Bytes);
        this.nestedValue = NestedLayout.Parse(this.nested.Bytes);
        this.primRecordStructValue = this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");
        this.nestedStructValue = this.nested.Layout.Parse(this.nested.Bytes.AsSpan(), "root");
        this.updateTarget = (byte[])this.primRecord.Bytes.Clone();
    }

    // ---- prim-le-record: 28 bytes, seven scalars ------------------------------------------------------------------------

    /// <summary>Runtime <c>Parse</c> of the 28-byte record from memory: the group's reference.</summary>
    /// <returns>The parsed record.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_Parse() => this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");

    /// <summary>Generated <c>Parse</c> of the record into its typed class.</summary>
    /// <returns>The generated record.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecord", "Gate")]
    public PrimRecordLayout.Root Generated_PrimRecord_Parse() => PrimRecordLayout.Parse(this.primRecord.Bytes);

    /// <summary>Reads every member of the record through the generated view, which allocates nothing.</summary>
    /// <returns>The sum of the members, so the reads cannot be optimized away.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecord")]
    public double Generated_PrimRecord_View()
    {
        var view = new PrimRecordLayout.RootView(this.primRecord.Bytes);
        return view.A + view.B + view.C + view.D + view.E + view.F + (view.G ? 1 : 0);
    }

    /// <summary>Reads the same members with hand-written <see cref="BinaryPrimitives"/> calls: the lower bound for a reader.</summary>
    /// <returns>The sum of the members.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecord")]
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

    /// <summary>Runtime <c>Serialize</c> of the record from a <see cref="StructValue"/>.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecordWrite")]
    public byte[] Runtime_PrimRecord_Serialize() => this.primRecord.Layout.Serialize("root", this.primRecordStructValue);

    /// <summary>Generated <c>Serialize</c> of the record from its typed class.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecordWrite")]
    public byte[] Generated_PrimRecord_Serialize() => PrimRecordLayout.Serialize(this.primRecordValue);

    /// <summary>Runtime <c>Update</c> of <c>root.c</c> in a byte array, located by path.</summary>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecordUpdate")]
    public void Runtime_PrimRecord_Update() => this.primRecord.Layout.Update(this.updateTarget.AsSpan(), "root.c", 7u);

    /// <summary>The generated typed setter for the same member.</summary>
    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public void Generated_PrimRecord_Update() => PrimRecordLayout.Update.C(this.updateTarget, 7u);

    /// <summary>Runtime <c>ParseWithDebug</c> of the record.</summary>
    /// <returns>The byte range of every value.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecordDebug")]
    public IReadOnlyList<DebugData> Runtime_PrimRecord_ParseWithDebug() => this.primRecord.Layout.ParseWithDebug(this.primRecord.Bytes.AsSpan(), "root").Debug;

    /// <summary>Generated <c>ParseWithDebug</c>: the generated value plus the runtime's byte ranges.</summary>
    /// <returns>The byte range of every value.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecordDebug")]
    public IReadOnlyList<DebugData> Generated_PrimRecord_ParseWithDebug() => PrimRecordLayout.ParseWithDebug(this.primRecord.Bytes).Debug;

    // ---- nested-x256: 256 × (2 × (2 leaves + tail) + mark) ---------------------------------------------------------------

    /// <summary>Runtime <c>Parse</c> of 256 nested records (6,400 bytes): the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_Parse() => this.nested.Layout.Parse(this.nested.Bytes.AsSpan(), "root");

    /// <summary>Generated <c>Parse</c> of the 256 nested records, which creates 256 object trees.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256", "Gate")]
    public NestedLayout.Root Generated_Nested256_Parse() => NestedLayout.Parse(this.nested.Bytes);

    /// <summary>Reads three members of each record through one generated view per element.</summary>
    /// <returns>The sum of the members.</returns>
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

    /// <summary>Reads the same members at hand-computed offsets (25 bytes per record).</summary>
    /// <returns>The sum of the members.</returns>
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

    /// <summary>Runtime <c>Serialize</c> of the 256 nested records.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256Write")]
    public byte[] Runtime_Nested256_Serialize() => this.nested.Layout.Serialize("root", this.nestedStructValue);

    /// <summary>Generated <c>Serialize</c> of the 256 nested records.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256Write")]
    public byte[] Generated_Nested256_Serialize() => NestedLayout.Serialize(this.nestedValue);

    // ---- array-u32-le-262144: one bulk array ------------------------------------------------------------------------------

    /// <summary>Runtime <c>Parse</c> of one 262,144-element <c>uint32</c> array (1 MiB): the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ArrayU32")]
    public StructValue Runtime_ArrayU32_Parse() => this.arrayU32.Layout.Parse(this.arrayU32.Bytes.AsSpan(), "root", options: this.arrayU32.ReadOptions);

    /// <summary>Generated <c>Parse</c> of the same array.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("ArrayU32")]
    public ArrayU32Layout.Root Generated_ArrayU32_Parse() => ArrayU32Layout.Parse(this.arrayU32.Bytes, this.arrayU32.ReadOptions);

    /// <summary>Reads every element through the generated view.</summary>
    /// <returns>The sum of the elements.</returns>
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

    /// <summary>Runtime <c>Parse</c> of the big-endian PNG header: the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Png")]
    public StructValue Runtime_Png_Parse() => this.png.Layout.Parse(this.png.Bytes.AsSpan(), "root");

    /// <summary>Generated <c>Parse</c> of the PNG header.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Png")]
    public PngLayout.Root Generated_Png_Parse() => PngLayout.Parse(this.png.Bytes);

    /// <summary>Reads three header fields through the generated view.</summary>
    /// <returns>The sum of the fields.</returns>
    [Benchmark]
    [BenchmarkCategory("Png")]
    public uint Generated_Png_View()
    {
        var view = new PngLayout.RootView(this.png.Bytes);
        return view.Ihdr.Width + view.Ihdr.Height + view.Ihdr.BitDepth;
    }

    /// <summary>Runtime <c>Parse</c> of fixed, terminated, UTF-8 and UTF-16 strings: the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Strings")]
    public StructValue Runtime_Strings_Parse() => this.strings.Layout.Parse(this.strings.Bytes.AsSpan(), "root");

    /// <summary>Generated <c>Parse</c> of the same strings.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Strings")]
    public StringsLayout.Root Generated_Strings_Parse() => StringsLayout.Parse(this.strings.Bytes);

    /// <summary>Runtime <c>Parse</c> of 128 records whose members depend on a tag: the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Conditional")]
    public StructValue Runtime_Conditional_Parse() => this.conditional.Layout.Parse(this.conditional.Bytes.AsSpan(), "root");

    /// <summary>Generated <c>Parse</c> of the same conditional records.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Conditional")]
    public ConditionalLayout.Root Generated_Conditional_Parse() => ConditionalLayout.Parse(this.conditional.Bytes);

    /// <summary>Runtime <c>Parse</c> of a two-node linked list behind one-byte pointers: the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PointerGraph")]
    public StructValue Runtime_PointerGraph_Parse() => this.pointerGraphLayout.Parse(this.pointerGraphBytes.AsSpan(), "root");

    /// <summary>Generated <c>Parse</c> of the same linked list.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("PointerGraph")]
    public PointerGraphLayout.Root Generated_PointerGraph_Parse() => PointerGraphLayout.Parse(this.pointerGraphBytes);
}
