namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the typed read plan: <c>ReadValue&lt;T&gt;</c> of a fully fixed composite must produce exactly
///     the object, and exactly the failure, that parsing to a <see cref="StructValue"/> and converting it produces -
///     for every member shape a mapped class can ask for, every limit, and both root and nested path targets.
/// </summary>
[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "Mapped-class members are named after layout fields")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307:AccessibleFieldsMustBeginWithUpperCaseLetter", Justification = "Mapped-class fields are named after layout fields")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "public fields show that a mapper may fill fields as well as properties")]
public class TypedReadPlanTests
{
    private const string Layout = """
        enum kind : uint8 { a = 1, b = 2 };
        struct leaf { uint8 k; uint32 v; };
        struct inner { leaf first; leaf second; uint16 pad; };
        struct root {
            uint16 magic;
            char tag[4];
            kind which;
            inner nested;
            uint32 samples[3];
            int16 deltas[2];
            uint8 none[0];
            struct { uint8 p; uint8 q; };
            leaf leaves[2];
            uint8 tail;
        };
        """;

    private static readonly byte[] Bytes =
    [
        0x34, 0x12, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 2, 9, 1, 0, 0, 0, 8, 2, 0, 0, 0, 0xEE, 0xFF, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0,
        0xFE, 0xFF, 0x10, 0x00, 0xAA, 0xBB, 5, 6, 0, 0, 0, 7, 8, 0, 0, 0, 0x99,
    ];

    /// <summary>Every member shape - exact types, converted types, nested mapped classes, mapped-class arrays, typed and converted numeric arrays, untyped members - matches the general path.</summary>
    [TestMethod]
    public void TypedPlan_MatchesGeneralPath_ForEveryMemberShape()
    {
        var layout = new CStruct(Layout);
        AssertSameOutcome<RootExact>(layout, Bytes, "root", null, "exact");
        AssertSameOutcome<RootConverted>(layout, Bytes, "root", null, "converted");
        AssertSameOutcome<RootUntyped>(layout, Bytes, "root", null, "untyped");
        AssertSameOutcome<Leaf>(layout, Bytes, "root.nested.first", null, "nested path");
        AssertSameOutcome<Leaf>(layout, Bytes, "root.leaves[1]", null, "element path");
        AssertSameOutcome<Inner>(layout, Bytes, "root.nested", null, "nested composite path");

        RootExact exact = layout.ReadValue<RootExact>(Bytes, "root");
        Assert.AreEqual((ushort)0x1234, exact.Magic);
        Assert.AreEqual("IHDR", exact.Tag);
        Assert.AreEqual(2, exact.Which);
        Assert.AreEqual(0x00000001u, exact.Nested.First.V);
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, exact.Samples);
        CollectionAssert.AreEqual(new short[] { -2, 16 }, exact.Deltas);
        Assert.HasCount(0, exact.None);
        Assert.AreEqual((byte)0xBB, exact.Q);
        Assert.AreEqual((byte)7, exact.Leaves[1].K);
        Assert.AreEqual((byte)0x99, exact.Tail);

        RootConverted converted = layout.ReadValue<RootConverted>(Bytes, "root");
        Assert.AreEqual(0x1234L, converted.magic);
        Assert.AreEqual(RootConverted.KindEnum.B, converted.which);
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, converted.samples);
        Assert.AreEqual(0xAA, converted.p);
        Assert.AreEqual(187M, converted.q);
        Assert.AreEqual(153D, converted.tail);
        Assert.IsInstanceOfType<StructValue>(converted.nested.second);
        Assert.AreEqual((ushort)0xFFEE, converted.nested.pad!["pad"]);

        var aligned = new CStruct(Layout, aligned: true);
        byte[] alignedBytes = aligned.Serialize("root", layout.Parse(Bytes, "root"));
        AssertSameOutcome<RootExact>(aligned, alignedBytes, "root", null, "aligned exact");
        AssertSameOutcome<RootConverted>(aligned, alignedBytes, "root", null, "aligned converted");
    }

    /// <summary>Every failure the general path raises - a missing member, an overflowing or unconvertible member, a failure inside a nested element, a throwing mapper, an unmapped target - is raised with the same type, message and path.</summary>
    [TestMethod]
    public void TypedPlan_MatchesGeneralPath_OnFailures()
    {
        var layout = new CStruct(Layout);
        AssertSameOutcome<RootMissingMember>(layout, Bytes, "root", null, "missing member");
        AssertSameOutcome<RootOverflow>(layout, Bytes, "root", null, "overflow");
        AssertSameOutcome<RootNotNumeric>(layout, Bytes, "root", null, "not numeric");
        AssertSameOutcome<RootNestedFailure>(layout, Bytes, "root", null, "failure inside a nested element");
        AssertSameOutcome<RootThrowingMapper>(layout, Bytes, "root", null, "throwing mapper");
        AssertSameOutcome<RootNotMapped>(layout, Bytes, "root", null, "unmapped target");
        AssertSameOutcome<int>(layout, Bytes, "root", null, "scalar target");
        AssertSameOutcome<string>(layout, Bytes, "root", null, "string target");

        CStructReadException nested = Assert.Throws<CStructReadException>(() => layout.ReadValue<RootNestedFailure>(Bytes, "root"));
        Assert.AreEqual("root.leaves[0].v", nested.Path);
        CStructReadException overflow = Assert.Throws<CStructReadException>(() => layout.ReadValue<RootOverflow>(Bytes, "root"));
        Assert.AreEqual("root.magic", overflow.Path);
        CStructPathException missing = Assert.Throws<CStructPathException>(() => layout.ReadValue<RootMissingMember>(Bytes, "root"));
        Assert.AreEqual("root", missing.Path);
        StringAssert.Contains(missing.Message, "'missing'");
        CStructReadException thrown = Assert.Throws<CStructReadException>(() => layout.ReadValue<RootThrowingMapper>(Bytes, "root"));
        Assert.AreEqual("root", thrown.Path);
        StringAssert.Contains(thrown.InnerException!.Message, "rejected 4660");
        CStructReadException unmapped = Assert.Throws<CStructReadException>(() => layout.ReadValue<RootNotMapped>(Bytes, "root"));
        StringAssert.Contains(unmapped.Message, nameof(RootNotMapped));

        for (long budget = 0; budget <= Bytes.Length; budget++)
        {
            AssertSameOutcome<RootExact>(layout, Bytes, "root", new ReadOptions { MaxTotalBytesRead = budget }, $"budget {budget}");
        }

        for (int length = 0; length < Bytes.Length; length++)
        {
            AssertSameOutcome<RootExact>(layout, Bytes[..length], "root", null, $"truncated to {length}");
        }

        AssertSameOutcome<RootExact>(layout, Bytes, "root", new ReadOptions { MaxArrayElements = 2 }, "array limit");
        AssertSameOutcome<RootExact>(layout, Bytes, "root", new ReadOptions { MaxNestingDepth = 1 }, "nesting limit 1");
        AssertSameOutcome<RootExact>(layout, Bytes, "root", new ReadOptions { MaxNestingDepth = 2 }, "nesting limit 2");
        AssertSameOutcome<RootExact>(layout, Bytes, "root", new ReadOptions { MaxNestingDepth = 3 }, "nesting limit 3");
    }

    /// <summary>The stream overload ends at the same position with and without the plan, and a stream positioned off the alignment boundary still matches.</summary>
    [TestMethod]
    public void TypedPlan_MatchesGeneralPath_OnStreams()
    {
        var layout = new CStruct(Layout);
        using var withPlan = new MemoryStream(Bytes, writable: false);
        RootExact fast = layout.ReadValue<RootExact>(withPlan, "root");
        using var withoutPlan = new MemoryStream(Bytes, writable: false);
        StaticReadPlan.DisabledForTesting = true;
        RootExact general = layout.ReadValue<RootExact>(withoutPlan, "root");
        StaticReadPlan.DisabledForTesting = false;
        Assert.AreEqual(Render(general), Render(fast));
        Assert.AreEqual(withoutPlan.Position, withPlan.Position);

        var aligned = new CStruct("struct root { uint8 a; uint32 b; uint8 c; };", aligned: true);
        byte[] bytes = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
        foreach (int start in new[] { 0, 1, 3, 4, 8 })
        {
            using var fastStream = new MemoryStream(bytes, writable: false);
            fastStream.Position = start;
            string fastText = Render(aligned.ReadValue<Small>(fastStream, "root"));
            using var generalStream = new MemoryStream(bytes, writable: false);
            generalStream.Position = start;
            StaticReadPlan.DisabledForTesting = true;
            string generalText = Render(aligned.ReadValue<Small>(generalStream, "root"));
            StaticReadPlan.DisabledForTesting = false;
            Assert.AreEqual(generalText, fastText, $"start {start}");
            Assert.AreEqual(generalStream.Position, fastStream.Position, $"start {start}: position");
        }
    }

    private static void AssertSameOutcome<T>(CStruct layout, byte[] bytes, string path, ReadOptions? options, string label)
    {
        (string? fast, Exception? fastError) = Try(() => Render(layout.ReadValue<T>(bytes, path, options: options)));
        StaticReadPlan.DisabledForTesting = true;
        (string? general, Exception? generalError) = Try(() => Render(layout.ReadValue<T>(bytes, path, options: options)));
        StaticReadPlan.DisabledForTesting = false;
        Assert.AreEqual(generalError?.GetType(), fastError?.GetType(), label);
        Assert.AreEqual(generalError?.Message, fastError?.Message, label);
        Assert.AreEqual((generalError as CStructException)?.Path, (fastError as CStructException)?.Path, label + ": failure path");
        Assert.AreEqual(general, fast, label);
    }

    private static (string? Result, Exception? Error) Try(Func<string> read)
    {
        try
        {
            return (read(), null);
        }
        catch (CStructException exception)
        {
            return (null, exception);
        }
    }

    private static string Render(object? value)
    {
        return JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), new JsonSerializerOptions { IncludeFields = true, });
    }

    public sealed class Leaf : ICStructMapped<Leaf>
    {
        public byte K { get; set; }

        public uint V { get; set; }

        public static Leaf ReadFrom(StructValue source)
        {
            return new Leaf { K = source.Get<byte>("k"), V = source.Get<uint>("v"), };
        }

        public static void WriteTo(Leaf value, StructValue target)
        {
            target["k"] = value.K;
            target["v"] = value.V;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Leaf>();
        }
    }

    public sealed class Inner : ICStructMapped<Inner>
    {
        public Leaf First { get; set; } = null!;

        public Leaf Second { get; set; } = null!;

        public ushort Pad { get; set; }

        public static Inner ReadFrom(StructValue source)
        {
            return new Inner { First = source.Get<Leaf>("first"), Second = source.Get<Leaf>("second"), Pad = source.Get<ushort>("pad"), };
        }

        public static void WriteTo(Inner value, StructValue target)
        {
            target["first"] = value.First;
            target["second"] = value.Second;
            target["pad"] = value.Pad;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Inner>();
        }
    }

    public sealed class RootExact : ICStructMapped<RootExact>
    {
        public ushort Magic { get; set; }

        public string Tag { get; set; } = string.Empty;

        public int Which { get; set; }

        public Inner Nested { get; set; } = null!;

        public uint[] Samples { get; set; } = [];

        public short[] Deltas { get; set; } = [];

        public byte[] None { get; set; } = [];

        public byte P { get; set; }

        public byte Q { get; set; }

        public Leaf[] Leaves { get; set; } = [];

        public byte Tail { get; set; }

        public static RootExact ReadFrom(StructValue source)
        {
            return new RootExact
            {
                Magic = source.Get<ushort>("magic"),
                Tag = source.Get<string>("tag"),
                Which = source.Get<int>("which"),
                Nested = source.Get<Inner>("nested"),
                Samples = source.Get<uint[]>("samples"),
                Deltas = source.Get<short[]>("deltas"),
                None = source.Get<byte[]>("none"),
                P = source.Get<byte>("p"),
                Q = source.Get<byte>("q"),
                Leaves = source.Get<Leaf[]>("leaves"),
                Tail = source.Get<byte>("tail"),
            };
        }

        public static void WriteTo(RootExact value, StructValue target)
        {
            target["magic"] = value.Magic;
            target["tag"] = value.Tag;
            target["which"] = value.Which;
            target["nested"] = value.Nested;
            target["samples"] = value.Samples;
            target["deltas"] = value.Deltas;
            target["none"] = value.None;
            target["p"] = value.P;
            target["q"] = value.Q;
            target["leaves"] = value.Leaves;
            target["tail"] = value.Tail;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootExact>();
        }
    }

    /// <summary>A mapper is free to widen, project to a CLR enum, take nullable or floating targets, and fill fields.</summary>
    public sealed class RootConverted : ICStructMapped<RootConverted>
    {
        public long magic;

        public string? tag;

        public KindEnum which;

        public InnerFields nested = null!;

        public long[] samples = [];

        public int[] deltas = [];

        public byte[] none = [];

        public int? p;

        public decimal q;

        public Leaf[] leaves = [];

        public double tail;

        public enum KindEnum : byte
        {
            A = 1,
            B = 2,
        }

        public static RootConverted ReadFrom(StructValue source)
        {
            return new RootConverted
            {
                magic = source.Get<long>("magic"),
                tag = source.Get<string?>("tag"),
                which = source.Get<KindEnum>("which"),
                nested = source.Get<InnerFields>("nested"),
                samples = source.Get<long[]>("samples"),
                deltas = source.Get<int[]>("deltas"),
                none = source.Get<byte[]>("none"),
                p = source.Get<int?>("p"),
                q = source.Get<decimal>("q"),
                leaves = source.Get<Leaf[]>("leaves"),
                tail = source.Get<double>("tail"),
            };
        }

        public static void WriteTo(RootConverted value, StructValue target)
        {
            target["magic"] = value.magic;
            target["tag"] = value.tag;
            target["which"] = value.which;
            target["nested"] = value.nested;
            target["samples"] = value.samples;
            target["deltas"] = value.deltas;
            target["none"] = value.none;
            target["p"] = value.p;
            target["q"] = value.q;
            target["leaves"] = value.leaves;
            target["tail"] = value.tail;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootConverted>();
        }
    }

    public sealed class InnerFields : ICStructMapped<InnerFields>
    {
        public Leaf first = null!;

        public object second = null!;

        public IReadOnlyDictionary<string, object?>? pad;

        /// <summary>The whole struct is a dictionary too, so a mapper may keep the parsed value itself.</summary>
        public static InnerFields ReadFrom(StructValue source)
        {
            return new InnerFields { first = source.Get<Leaf>("first"), second = source.Get<object>("second"), pad = source, };
        }

        public static void WriteTo(InnerFields value, StructValue target)
        {
            target["first"] = value.first;
            target["second"] = value.second;
            target["pad"] = value.pad!["pad"];
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<InnerFields>();
        }
    }

    public sealed class RootUntyped : ICStructMapped<RootUntyped>
    {
        public object? Magic { get; set; }

        public object? Nested { get; set; }

        public IList<object?>? Samples { get; set; }

        public object? Leaves { get; set; }

        public StructValue? nested { get; set; }

        public static RootUntyped ReadFrom(StructValue source)
        {
            return new RootUntyped
            {
                Magic = source.Get<object>("magic"),
                Nested = source.Get<object>("nested"),
                Samples = source.Get<IList<object?>>("samples"),
                Leaves = source.Get<object>("leaves"),
                nested = source.Get<StructValue>("nested"),
            };
        }

        public static void WriteTo(RootUntyped value, StructValue target)
        {
            target["magic"] = value.Magic;
            target["nested"] = value.nested;
            target["samples"] = value.Samples;
            target["leaves"] = value.Leaves;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootUntyped>();
        }
    }

    public sealed class RootMissingMember : ICStructMapped<RootMissingMember>
    {
        public ushort Magic { get; set; }

        public int Missing { get; set; }

        public static RootMissingMember ReadFrom(StructValue source)
        {
            return new RootMissingMember { Magic = source.Get<ushort>("magic"), Missing = source.Get<int>("missing"), };
        }

        public static void WriteTo(RootMissingMember value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootMissingMember>();
        }
    }

    public sealed class RootOverflow : ICStructMapped<RootOverflow>
    {
        public byte Magic { get; set; }

        public static RootOverflow ReadFrom(StructValue source)
        {
            return new RootOverflow { Magic = source.Get<byte>("magic"), };
        }

        public static void WriteTo(RootOverflow value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootOverflow>();
        }
    }

    public sealed class RootNotNumeric : ICStructMapped<RootNotNumeric>
    {
        public bool Magic { get; set; }

        public static RootNotNumeric ReadFrom(StructValue source)
        {
            return new RootNotNumeric { Magic = source.Get<bool>("magic"), };
        }

        public static void WriteTo(RootNotNumeric value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootNotNumeric>();
        }
    }

    public sealed class RootNestedFailure : ICStructMapped<RootNestedFailure>
    {
        public LeafOverflow[] Leaves { get; set; } = [];

        public static RootNestedFailure ReadFrom(StructValue source)
        {
            return new RootNestedFailure { Leaves = source.Get<LeafOverflow[]>("leaves"), };
        }

        public static void WriteTo(RootNestedFailure value, StructValue target)
        {
            target["leaves"] = value.Leaves;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootNestedFailure>();
        }

        public sealed class LeafOverflow : ICStructMapped<LeafOverflow>
        {
            public byte K { get; set; }

            public bool V { get; set; }

            public static LeafOverflow ReadFrom(StructValue source)
            {
                return new LeafOverflow { K = source.Get<byte>("k"), V = source.Get<bool>("v"), };
            }

            public static void WriteTo(LeafOverflow value, StructValue target)
            {
                target["k"] = value.K;
                target["v"] = value.V;
            }

            [ModuleInitializer]
            internal static void Register()
            {
                MappedTypes.Register<LeafOverflow>();
            }
        }
    }

    /// <summary>A mapper that throws an ordinary exception; the read reports it as a conversion failure at the mapped path.</summary>
    public sealed class RootThrowingMapper : ICStructMapped<RootThrowingMapper>
    {
        public ushort Magic { get; set; }

        public static RootThrowingMapper ReadFrom(StructValue source)
        {
            throw new InvalidOperationException("rejected " + source.Get<ushort>("magic"));
        }

        public static void WriteTo(RootThrowingMapper value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootThrowingMapper>();
        }
    }

    /// <summary>Implements nothing; a typed read into it is a plain conversion failure that names the type.</summary>
    public sealed class RootNotMapped
    {
        public ushort Magic { get; set; }
    }

    public sealed class Small : ICStructMapped<Small>
    {
        public byte A { get; set; }

        public uint B { get; set; }

        public byte C { get; set; }

        public static Small ReadFrom(StructValue source)
        {
            return new Small { A = source.Get<byte>("a"), B = source.Get<uint>("b"), C = source.Get<byte>("c"), };
        }

        public static void WriteTo(Small value, StructValue target)
        {
            target["a"] = value.A;
            target["b"] = value.B;
            target["c"] = value.C;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Small>();
        }
    }
}
