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
        RootExact general = layout.ReadValue<RootExact>(withoutPlan, "root", options: ExecutionPaths.NoFastPaths());
        Assert.AreEqual(OperationOutcome.Render(general), OperationOutcome.Render(fast));
        Assert.AreEqual(withoutPlan.Position, withPlan.Position);

        var aligned = new CStruct("struct root { uint8 a; uint32 b; uint8 c; };", aligned: true);
        byte[] bytes = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
        foreach (int start in new[] { 0, 1, 3, 4, 8 })
        {
            using var fastStream = new MemoryStream(bytes, writable: false);
            fastStream.Position = start;
            string fastText = OperationOutcome.Render(aligned.ReadValue<Small>(fastStream, "root"));
            using var generalStream = new MemoryStream(bytes, writable: false);
            generalStream.Position = start;
            string generalText = OperationOutcome.Render(aligned.ReadValue<Small>(generalStream, "root", options: ExecutionPaths.NoFastPaths()));
            Assert.AreEqual(generalText, fastText, $"start {start}");
            Assert.AreEqual(generalStream.Position, fastStream.Position, $"start {start}: position");
        }
    }

    /// <summary>Asserts that a typed read gives the same value or failure with the static plan and with only the general path.</summary>
    private static void AssertSameOutcome<T>(CStruct layout, byte[] bytes, string path, ReadOptions? options, string label)
    {
        OperationOutcome fast = OperationOutcome.Of(() => layout.ReadValue<T>(bytes, path, options: options));
        OperationOutcome general = OperationOutcome.Of(() => layout.ReadValue<T>(bytes, path, options: ExecutionPaths.NoFastPaths(options)));
        OperationOutcome.AssertSame(general, fast, label);
    }

    /// <summary>A mapped class for the tests' <c>Leaf</c> record, read and written through the runtime.</summary>
    public sealed class Leaf : ICStructMapped<Leaf>
    {
        /// <summary>Gets or sets the <c>k</c> byte of a <c>leaf</c>.</summary>
        public byte K { get; set; }

        /// <summary>Gets or sets the 32-bit <c>v</c> value of a <c>leaf</c>.</summary>
        public uint V { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static Leaf ReadFrom(StructValue source)
        {
            return new Leaf { K = source.Get<byte>("k"), V = source.Get<uint>("v"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(Leaf value, StructValue target)
        {
            target["k"] = value.K;
            target["v"] = value.V;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Leaf>();
        }
    }

    /// <summary>A mapped class for the tests' <c>Inner</c> record, read and written through the runtime.</summary>
    public sealed class Inner : ICStructMapped<Inner>
    {
        /// <summary>Gets or sets the first <c>leaf</c> of an <c>inner</c> record.</summary>
        public Leaf First { get; set; } = null!;

        /// <summary>Gets or sets the second <c>leaf</c> of an <c>inner</c> record.</summary>
        public Leaf Second { get; set; } = null!;

        /// <summary>Gets or sets the trailing 16-bit <c>pad</c> field of an <c>inner</c> record.</summary>
        public ushort Pad { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static Inner ReadFrom(StructValue source)
        {
            return new Inner { First = source.Get<Leaf>("first"), Second = source.Get<Leaf>("second"), Pad = source.Get<ushort>("pad"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(Inner value, StructValue target)
        {
            target["first"] = value.First;
            target["second"] = value.Second;
            target["pad"] = value.Pad;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Inner>();
        }
    }

    /// <summary>A mapped class for the tests' <c>RootExact</c> record, read and written through the runtime.</summary>
    public sealed class RootExact : ICStructMapped<RootExact>
    {
        /// <summary>Gets or sets the 16-bit <c>magic</c> field, read into its exact type.</summary>
        public ushort Magic { get; set; }

        /// <summary>Gets or sets the four-character <c>tag</c> array, decoded as a string.</summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>Gets or sets the <c>kind</c> enum member <c>which</c> as its numeric value.</summary>
        public int Which { get; set; }

        /// <summary>Gets or sets the nested <c>inner</c> record as a mapped class.</summary>
        public Inner Nested { get; set; } = null!;

        /// <summary>Gets or sets the three <c>uint32</c> <c>samples</c> as a typed array.</summary>
        public uint[] Samples { get; set; } = [];

        /// <summary>Gets or sets the two signed <c>deltas</c> as a typed array.</summary>
        public short[] Deltas { get; set; } = [];

        /// <summary>Gets or sets the zero-length <c>none</c> array, which maps to an empty array.</summary>
        public byte[] None { get; set; } = [];

        /// <summary>Gets or sets <c>p</c>, the first member promoted from the anonymous struct.</summary>
        public byte P { get; set; }

        /// <summary>Gets or sets <c>q</c>, the second member promoted from the anonymous struct.</summary>
        public byte Q { get; set; }

        /// <summary>Gets or sets the two <c>leaf</c> elements as an array of mapped classes.</summary>
        public Leaf[] Leaves { get; set; } = [];

        /// <summary>Gets or sets the final <c>tail</c> byte.</summary>
        public byte Tail { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
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

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
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

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootExact>();
        }
    }

    /// <summary>A mapper is free to widen, project to a CLR enum, take nullable or floating targets, and fill fields.</summary>
    public sealed class RootConverted : ICStructMapped<RootConverted>
    {
        /// <summary>The 16-bit <c>magic</c> field, widened to a <see cref="long"/>.</summary>
        public long magic;

        /// <summary>The four-character <c>tag</c> array as a nullable string.</summary>
        public string? tag;

        /// <summary>The <c>which</c> member projected to the CLR enum <see cref="KindEnum"/>.</summary>
        public KindEnum which;

        /// <summary>The nested <c>inner</c> record, mapped to a class that keeps some members untyped.</summary>
        public InnerFields nested = null!;

        /// <summary>The <c>samples</c> array with each element widened to a <see cref="long"/>.</summary>
        public long[] samples = [];

        /// <summary>The signed <c>deltas</c> array with each element widened to an <see cref="int"/>.</summary>
        public int[] deltas = [];

        /// <summary>The zero-length <c>none</c> array.</summary>
        public byte[] none = [];

        /// <summary>The promoted <c>p</c> byte as a nullable integer.</summary>
        public int? p;

        /// <summary>The promoted <c>q</c> byte converted to a <see cref="decimal"/>.</summary>
        public decimal q;

        /// <summary>The two <c>leaf</c> elements as mapped classes.</summary>
        public Leaf[] leaves = [];

        /// <summary>The final <c>tail</c> byte converted to a <see cref="double"/>.</summary>
        public double tail;

        /// <summary>
        ///     A CLR enum that mirrors the layout's <c>kind</c> enum with the same one-byte underlying type.
        /// </summary>
        public enum KindEnum : byte
        {
            /// <summary>Matches the layout's <c>a = 1</c>.</summary>
            A = 1,

            /// <summary>Matches the layout's <c>b = 2</c>.</summary>
            B = 2,
        }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
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

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
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

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootConverted>();
        }
    }

    /// <summary>A mapped class for the tests' <c>InnerFields</c> record, read and written through the runtime.</summary>
    public sealed class InnerFields : ICStructMapped<InnerFields>
    {
        /// <summary>The first <c>leaf</c> as a mapped class.</summary>
        public Leaf first = null!;

        /// <summary>
        ///     The second <c>leaf</c> kept as an untyped object, which holds the parsed <see cref="StructValue"/>.
        /// </summary>
        public object second = null!;

        /// <summary>
        ///     The whole parsed <c>inner</c> record viewed as a dictionary; the writer reads <c>pad</c> from it.
        /// </summary>
        public IReadOnlyDictionary<string, object?>? pad;

        /// <summary>The whole struct is a dictionary too, so a mapper may keep the parsed value itself.</summary>
        /// <param name="source">The parsed <c>inner</c> record.</param>
        /// <returns>The mapped value, whose <c>pad</c> member is <paramref name="source"/> itself.</returns>
        public static InnerFields ReadFrom(StructValue source)
        {
            return new InnerFields { first = source.Get<Leaf>("first"), second = source.Get<object>("second"), pad = source, };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(InnerFields value, StructValue target)
        {
            target["first"] = value.first;
            target["second"] = value.second;
            target["pad"] = value.pad!["pad"];
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<InnerFields>();
        }
    }

    /// <summary>A mapped class for the tests' <c>RootUntyped</c> record, read and written through the runtime.</summary>
    public sealed class RootUntyped : ICStructMapped<RootUntyped>
    {
        /// <summary>Gets or sets the <c>magic</c> field as an untyped object.</summary>
        public object? Magic { get; set; }

        /// <summary>Gets or sets the nested <c>inner</c> record as an untyped object.</summary>
        public object? Nested { get; set; }

        /// <summary>Gets or sets the <c>samples</c> array as a list of untyped elements.</summary>
        public IList<object?>? Samples { get; set; }

        /// <summary>Gets or sets the <c>leaves</c> array as an untyped object.</summary>
        public object? Leaves { get; set; }

        /// <summary>
        ///     Gets or sets the nested <c>inner</c> record as a <see cref="StructValue"/>, which the writer sends back.
        /// </summary>
        public StructValue? nested { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
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

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(RootUntyped value, StructValue target)
        {
            target["magic"] = value.Magic;
            target["nested"] = value.nested;
            target["samples"] = value.Samples;
            target["leaves"] = value.Leaves;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootUntyped>();
        }
    }

    /// <summary>A mapped class for the tests' <c>RootMissingMember</c> record, read and written through the runtime.</summary>
    public sealed class RootMissingMember : ICStructMapped<RootMissingMember>
    {
        /// <summary>Gets or sets the <c>magic</c> field, which the layout has.</summary>
        public ushort Magic { get; set; }

        /// <summary>
        ///     Gets or sets a value read from <c>missing</c>, a field the layout lacks, so the read fails with a path
        ///     error.
        /// </summary>
        public int Missing { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static RootMissingMember ReadFrom(StructValue source)
        {
            return new RootMissingMember { Magic = source.Get<ushort>("magic"), Missing = source.Get<int>("missing"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(RootMissingMember value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootMissingMember>();
        }
    }

    /// <summary>A mapped class for the tests' <c>RootOverflow</c> record, read and written through the runtime.</summary>
    public sealed class RootOverflow : ICStructMapped<RootOverflow>
    {
        /// <summary>Gets or sets a byte target for the 16-bit <c>magic</c> value 0x1234, which overflows it.</summary>
        public byte Magic { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static RootOverflow ReadFrom(StructValue source)
        {
            return new RootOverflow { Magic = source.Get<byte>("magic"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(RootOverflow value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootOverflow>();
        }
    }

    /// <summary>A mapped class for the tests' <c>RootNotNumeric</c> record, read and written through the runtime.</summary>
    public sealed class RootNotNumeric : ICStructMapped<RootNotNumeric>
    {
        /// <summary>
        ///     Gets or sets a <see cref="bool"/> target for the numeric <c>magic</c> field, which cannot convert to it.
        /// </summary>
        public bool Magic { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static RootNotNumeric ReadFrom(StructValue source)
        {
            return new RootNotNumeric { Magic = source.Get<bool>("magic"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(RootNotNumeric value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootNotNumeric>();
        }
    }

    /// <summary>A mapped class for the tests' <c>RootNestedFailure</c> record, read and written through the runtime.</summary>
    public sealed class RootNestedFailure : ICStructMapped<RootNestedFailure>
    {
        /// <summary>Gets or sets the <c>leaves</c> array, whose first element fails to map.</summary>
        public LeafOverflow[] Leaves { get; set; } = [];

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static RootNestedFailure ReadFrom(StructValue source)
        {
            return new RootNestedFailure { Leaves = source.Get<LeafOverflow[]>("leaves"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(RootNestedFailure value, StructValue target)
        {
            target["leaves"] = value.Leaves;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootNestedFailure>();
        }

        /// <summary>A mapped class for the tests' <c>LeafOverflow</c> record, read and written through the runtime.</summary>
        public sealed class LeafOverflow : ICStructMapped<LeafOverflow>
        {
            /// <summary>Gets or sets the <c>k</c> byte, which maps without failing.</summary>
            public byte K { get; set; }

            /// <summary>
            ///     Gets or sets a <see cref="bool"/> target for the 32-bit <c>v</c> field, which cannot convert to it.
            /// </summary>
            public bool V { get; set; }

            /// <summary>Builds the class from a parsed record.</summary>
            /// <param name="source">The parsed record.</param>
            /// <returns>The mapped value.</returns>
            public static LeafOverflow ReadFrom(StructValue source)
            {
                return new LeafOverflow { K = source.Get<byte>("k"), V = source.Get<bool>("v"), };
            }

            /// <summary>Copies the class into a record to write.</summary>
            /// <param name="value">The mapped value.</param>
            /// <param name="target">The record to fill.</param>
            public static void WriteTo(LeafOverflow value, StructValue target)
            {
                target["k"] = value.K;
                target["v"] = value.V;
            }

            /// <summary>Registers the mapping when the test assembly loads.</summary>
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
        /// <summary>Gets or sets the <c>magic</c> field; <c>ReadFrom</c> throws before any instance is built.</summary>
        public ushort Magic { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static RootThrowingMapper ReadFrom(StructValue source)
        {
            throw new InvalidOperationException("rejected " + source.Get<ushort>("magic"));
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(RootThrowingMapper value, StructValue target)
        {
            target["magic"] = value.Magic;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<RootThrowingMapper>();
        }
    }

    /// <summary>Implements nothing; a typed read into it is a plain conversion failure that names the type.</summary>
    public sealed class RootNotMapped
    {
        /// <summary>Gets or sets a <c>magic</c> value that no read fills, because the class has no mapping.</summary>
        public ushort Magic { get; set; }
    }

    /// <summary>A mapped class for the tests' <c>Small</c> record, read and written through the runtime.</summary>
    public sealed class Small : ICStructMapped<Small>
    {
        /// <summary>Gets or sets the leading <c>a</c> byte.</summary>
        public byte A { get; set; }

        /// <summary>Gets or sets the 32-bit <c>b</c> value, which the aligned layout places at offset 4.</summary>
        public uint B { get; set; }

        /// <summary>Gets or sets the trailing <c>c</c> byte.</summary>
        public byte C { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static Small ReadFrom(StructValue source)
        {
            return new Small { A = source.Get<byte>("a"), B = source.Get<uint>("b"), C = source.Get<byte>("c"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(Small value, StructValue target)
        {
            target["a"] = value.A;
            target["b"] = value.B;
            target["c"] = value.C;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Small>();
        }
    }
}
