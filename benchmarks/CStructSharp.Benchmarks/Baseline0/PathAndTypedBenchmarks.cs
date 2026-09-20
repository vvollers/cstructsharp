namespace CStructSharp.Benchmarks.Baseline0;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using CStructSharp.Values;

/// <summary>S-PATH and typed reads: selected-value reads, address resolution by index, and mapped-class reads.</summary>
[BenchmarkCategory("Baseline0", "Path")]
public class PathAndTypedBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private FixtureCase structArray = null!;
    private MemoryStream structArrayStream = null!;
    private string indexedPath = null!;

    [Params(0, 127, 9999)]
    public int Index { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.structArray = FixtureCase.Load("array-struct-10000");
        this.structArrayStream = new MemoryStream(this.structArray.Bytes, writable: false);
        this.indexedPath = $"root.items[{this.Index}].id";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.structArrayStream.Dispose();
    }

    [Benchmark]
    public object? ReadValue_Scalar_Natural()
    {
        return this.primRecord.Layout.ReadValue(this.primRecord.Bytes.AsSpan(), "root.c");
    }

    [Benchmark]
    public uint ReadValue_Scalar_Typed()
    {
        return this.primRecord.Layout.ReadValue<uint>(this.primRecord.Bytes.AsSpan(), "root.c");
    }

    [Benchmark]
    public long ResolveAddress_Index()
    {
        this.structArrayStream.Position = 0;
        return this.structArray.Layout.ResolveAddress(this.structArrayStream, this.indexedPath);
    }

    [Benchmark]
    public uint ReadValue_Indexed_Typed()
    {
        return this.structArray.Layout.ReadValue<uint>(this.structArray.Bytes.AsSpan(), this.indexedPath);
    }

    [Benchmark]
    public PrimRecord ReadTyped_PrimRecord()
    {
        return this.primRecord.Layout.ReadValue<PrimRecord>(this.primRecord.Bytes.AsSpan(), "root");
    }

    [Benchmark]
    public NestedRoot ReadTyped_Nested256()
    {
        return this.nested.Layout.ReadValue<NestedRoot>(this.nested.Bytes.AsSpan(), "root");
    }

    public sealed class PrimRecord : ICStructMapped<PrimRecord>
    {
        public byte A { get; set; }

        public short B { get; set; }

        public uint C { get; set; }

        public long D { get; set; }

        public float E { get; set; }

        public double F { get; set; }

        public bool G { get; set; }

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

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<PrimRecord>();
        }
    }

    public sealed class NestedLeaf : ICStructMapped<NestedLeaf>
    {
        public byte Kind { get; set; }

        public uint Value { get; set; }

        public static NestedLeaf ReadFrom(StructValue source)
        {
            return new NestedLeaf { Kind = source.Get<byte>("kind"), Value = source.Get<uint>("value"), };
        }

        public static void WriteTo(NestedLeaf value, StructValue target)
        {
            target["kind"] = value.Kind;
            target["value"] = value.Value;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedLeaf>();
        }
    }

    public sealed class NestedMid : ICStructMapped<NestedMid>
    {
        public NestedLeaf First { get; set; } = new();

        public NestedLeaf Second { get; set; } = new();

        public ushort Tail { get; set; }

        public static NestedMid ReadFrom(StructValue source)
        {
            return new NestedMid
            {
                First = source.Get<NestedLeaf>("first"),
                Second = source.Get<NestedLeaf>("second"),
                Tail = source.Get<ushort>("tail"),
            };
        }

        public static void WriteTo(NestedMid value, StructValue target)
        {
            target["first"] = value.First;
            target["second"] = value.Second;
            target["tail"] = value.Tail;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedMid>();
        }
    }

    public sealed class NestedTop : ICStructMapped<NestedTop>
    {
        public NestedMid Left { get; set; } = new();

        public NestedMid Right { get; set; } = new();

        public byte Mark { get; set; }

        public static NestedTop ReadFrom(StructValue source)
        {
            return new NestedTop
            {
                Left = source.Get<NestedMid>("left"),
                Right = source.Get<NestedMid>("right"),
                Mark = source.Get<byte>("mark"),
            };
        }

        public static void WriteTo(NestedTop value, StructValue target)
        {
            target["left"] = value.Left;
            target["right"] = value.Right;
            target["mark"] = value.Mark;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedTop>();
        }
    }

    public sealed class NestedRoot : ICStructMapped<NestedRoot>
    {
        public NestedTop[] Items { get; set; } = [];

        public static NestedRoot ReadFrom(StructValue source)
        {
            return new NestedRoot { Items = source.Get<NestedTop[]>("items"), };
        }

        public static void WriteTo(NestedRoot value, StructValue target)
        {
            target["items"] = value.Items;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NestedRoot>();
        }
    }
}
