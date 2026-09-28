namespace CStructSharp.Benchmarks.Scenarios;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using CStructSharp.Values;

/// <summary>S-PATH and typed reads: selected-value reads, address resolution by index, and mapped-class reads.</summary>
[BenchmarkCategory("Scenario", "Path")]
public class PathAndTypedBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private FixtureCase structArray = null!;
    private MemoryStream structArrayStream = null!;
    private string indexedPath = null!;

    /// <summary>Gets or sets the element index the indexed cases select: the first, a middle and the last element.</summary>
    [Params(0, 127, 9999)]
    public int Index { get; set; }

    /// <summary>Loads the record, nested and struct-array fixtures and builds the indexed path.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.structArray = FixtureCase.Load("array-struct-10000");
        this.structArrayStream = new MemoryStream(this.structArray.Bytes, writable: false);
        this.indexedPath = $"root.items[{this.Index}].id";
    }

    /// <summary>Disposes the struct-array stream.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.structArrayStream.Dispose();
    }

    /// <summary>Reads <c>root.c</c> of the 28-byte record as its natural type, boxed.</summary>
    /// <returns>The value.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "ScalarRead")]
    public object? ReadValue_Scalar_Natural()
    {
        return this.primRecord.Layout.ReadValue(this.primRecord.Bytes.AsSpan(), "root.c");
    }

    /// <summary>Reads <c>root.c</c> as a <see cref="uint"/>, without boxing.</summary>
    /// <returns>The value.</returns>
    [Benchmark]
    [BenchmarkCategory("ScalarRead")]
    public uint ReadValue_Scalar_Typed()
    {
        return this.primRecord.Layout.ReadValue<uint>(this.primRecord.Bytes.AsSpan(), "root.c");
    }

    /// <summary>Resolves the byte offset of the selected element's <c>id</c> in a 10,000-element array.</summary>
    /// <returns>The byte offset.</returns>
    [BenchmarkCategory("Impact")]
    [Benchmark]
    public long ResolveAddress_Index()
    {
        this.structArrayStream.Position = 0;
        return this.structArray.Layout.ResolveAddress(this.structArrayStream, this.indexedPath);
    }

    /// <summary>Reads the selected element's <c>id</c> from memory.</summary>
    /// <returns>The value.</returns>
    [Benchmark]
    public uint ReadValue_Indexed_Typed()
    {
        return this.structArray.Layout.ReadValue<uint>(this.structArray.Bytes.AsSpan(), this.indexedPath);
    }

    /// <summary>Reads the 28-byte record into <see cref="PrimRecord"/>.</summary>
    /// <returns>The mapped record.</returns>
    [Benchmark]
    public PrimRecord ReadTyped_PrimRecord()
    {
        return this.primRecord.Layout.ReadValue<PrimRecord>(this.primRecord.Bytes.AsSpan(), "root");
    }

    /// <summary>Reads the 256 nested records into <see cref="NestedRoot"/>.</summary>
    /// <returns>The mapped root.</returns>
    [Benchmark]
    public NestedRoot ReadTyped_Nested256()
    {
        return this.nested.Layout.ReadValue<NestedRoot>(this.nested.Bytes.AsSpan(), "root");
    }

    /// <summary>The mapped class of the <c>prim-le-record</c> fixture's seven scalars.</summary>
    public sealed class PrimRecord : ICStructMapped<PrimRecord>
    {
        /// <summary>Gets or sets the <c>uint8 a</c> field at byte offset 0.</summary>
        public byte A { get; set; }

        /// <summary>Gets or sets the <c>int16 b</c> field at byte offset 1.</summary>
        public short B { get; set; }

        /// <summary>Gets or sets the <c>uint32 c</c> field at byte offset 3.</summary>
        public uint C { get; set; }

        /// <summary>Gets or sets the <c>int64 d</c> field at byte offset 7.</summary>
        public long D { get; set; }

        /// <summary>Gets or sets the <c>float32 e</c> field at byte offset 15.</summary>
        public float E { get; set; }

        /// <summary>Gets or sets the <c>float64 f</c> field at byte offset 19.</summary>
        public double F { get; set; }

        /// <summary>Gets or sets the <c>bool g</c> field at byte offset 27.</summary>
        public bool G { get; set; }

        /// <summary>Creates the class from its parsed value.</summary>
        /// <param name="source">The parsed value.</param>
        /// <returns>The mapped value.</returns>
        public static PrimRecord ReadFrom(StructValue source)
        {
            return new PrimRecord
            {
                A = source.Get<byte>("a"),
                B = source.Get<short>("b"),
                C = source.Get<uint>("c"),
                D = source.Get<long>("d"),
                E = source.Get<float>("e"),
                F = source.Get<double>("f"),
                G = source.Get<bool>("g"),
            };
        }

        /// <summary>Copies the class into a value for writing.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The value to fill.</param>
        public static void WriteTo(PrimRecord value, StructValue target)
        {
            target["a"] = value.A;
            target["b"] = value.B;
            target["c"] = value.C;
            target["d"] = value.D;
            target["e"] = value.E;
            target["f"] = value.F;
            target["g"] = value.G;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<PrimRecord>();
        }
    }

    /// <summary>The mapped class of <c>struct leaf</c>: a kind byte and a value.</summary>
    public sealed class NestedLeaf : ICStructMapped<NestedLeaf>
    {
        /// <summary>Gets or sets the <c>uint8 kind</c> field.</summary>
        public byte Kind { get; set; }

        /// <summary>Gets or sets the <c>uint32 value</c> field.</summary>
        public uint Value { get; set; }

        /// <summary>Creates the class from its parsed value.</summary>
        /// <param name="source">The parsed value.</param>
        /// <returns>The mapped value.</returns>
        public static NestedLeaf ReadFrom(StructValue source)
        {
            return new NestedLeaf { Kind = source.Get<byte>("kind"), Value = source.Get<uint>("value"), };
        }

        /// <summary>Copies the class into a value for writing.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The value to fill.</param>
        public static void WriteTo(NestedLeaf value, StructValue target)
        {
            target["kind"] = value.Kind;
            target["value"] = value.Value;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedLeaf>();
        }
    }

    /// <summary>The mapped class of <c>struct mid</c>: two leaves and a tail.</summary>
    public sealed class NestedMid : ICStructMapped<NestedMid>
    {
        /// <summary>Gets or sets the <c>leaf first</c> member.</summary>
        public NestedLeaf First { get; set; } = new();

        /// <summary>Gets or sets the <c>leaf second</c> member.</summary>
        public NestedLeaf Second { get; set; } = new();

        /// <summary>Gets or sets the <c>uint16 tail</c> field that follows the two leaves.</summary>
        public ushort Tail { get; set; }

        /// <summary>Creates the class from its parsed value.</summary>
        /// <param name="source">The parsed value.</param>
        /// <returns>The mapped value.</returns>
        public static NestedMid ReadFrom(StructValue source)
        {
            return new NestedMid
            {
                First = source.Get<NestedLeaf>("first"),
                Second = source.Get<NestedLeaf>("second"),
                Tail = source.Get<ushort>("tail"),
            };
        }

        /// <summary>Copies the class into a value for writing.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The value to fill.</param>
        public static void WriteTo(NestedMid value, StructValue target)
        {
            target["first"] = value.First;
            target["second"] = value.Second;
            target["tail"] = value.Tail;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedMid>();
        }
    }

    /// <summary>The mapped class of <c>struct top</c>: two mids and a mark.</summary>
    public sealed class NestedTop : ICStructMapped<NestedTop>
    {
        /// <summary>Gets or sets the <c>mid left</c> member.</summary>
        public NestedMid Left { get; set; } = new();

        /// <summary>Gets or sets the <c>mid right</c> member.</summary>
        public NestedMid Right { get; set; } = new();

        /// <summary>Gets or sets the <c>uint8 mark</c> field that follows the two mids.</summary>
        public byte Mark { get; set; }

        /// <summary>Creates the class from its parsed value.</summary>
        /// <param name="source">The parsed value.</param>
        /// <returns>The mapped value.</returns>
        public static NestedTop ReadFrom(StructValue source)
        {
            return new NestedTop
            {
                Left = source.Get<NestedMid>("left"),
                Right = source.Get<NestedMid>("right"),
                Mark = source.Get<byte>("mark"),
            };
        }

        /// <summary>Copies the class into a value for writing.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The value to fill.</param>
        public static void WriteTo(NestedTop value, StructValue target)
        {
            target["left"] = value.Left;
            target["right"] = value.Right;
            target["mark"] = value.Mark;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedTop>();
        }
    }

    /// <summary>The mapped class of the <c>nested-x256</c> root: 256 tops.</summary>
    public sealed class NestedRoot : ICStructMapped<NestedRoot>
    {
        /// <summary>Gets or sets the <c>top items[256]</c> array, one element per record.</summary>
        public NestedTop[] Items { get; set; } = [];

        /// <summary>Creates the class from its parsed value.</summary>
        /// <param name="source">The parsed value.</param>
        /// <returns>The mapped value.</returns>
        public static NestedRoot ReadFrom(StructValue source)
        {
            return new NestedRoot { Items = source.Get<NestedTop[]>("items"), };
        }

        /// <summary>Copies the class into a value for writing.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The value to fill.</param>
        public static void WriteTo(NestedRoot value, StructValue target)
        {
            target["items"] = value.Items;
        }

        /// <summary>Registers the mapping when the module loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedRoot>();
        }
    }
}
