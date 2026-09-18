namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the typed read plan: <c>ReadValue&lt;T&gt;</c> of a fully fixed composite must produce exactly
///     the object, and exactly the failure, that parsing to a <see cref="StructValue"/> and converting it produces -
///     for every member shape a POCO can declare, every limit, and both root and nested path targets.
/// </summary>
[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "POCO members are named after layout fields")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307:AccessibleFieldsMustBeginWithUpperCaseLetter", Justification = "POCO fields are named after layout fields")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "public fields exercise the field-mapping path")]
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

    /// <summary>Every member shape - exact types, converted types, nested POCOs, POCO arrays and lists, typed and converted numeric arrays, untyped members - matches the general path, including case-insensitive member names.</summary>
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

        var aligned = new CStruct(Layout, aligned: true);
        byte[] alignedBytes = aligned.Serialize("root", layout.Parse(Bytes, "root"));
        AssertSameOutcome<RootExact>(aligned, alignedBytes, "root", null, "aligned exact");
        AssertSameOutcome<RootConverted>(aligned, alignedBytes, "root", null, "aligned converted");
    }

    /// <summary>Every failure the general path raises - missing, ambiguous, unconvertible, overflowing, unconstructible members - is raised with the same type, message and path.</summary>
    [TestMethod]
    public void TypedPlan_MatchesGeneralPath_OnFailures()
    {
        var layout = new CStruct(Layout);
        AssertSameOutcome<RootMissingMember>(layout, Bytes, "root", null, "missing member");
        AssertSameOutcome<RootOverflow>(layout, Bytes, "root", null, "overflow");
        AssertSameOutcome<RootNotNumeric>(layout, Bytes, "root", null, "not numeric");
        AssertSameOutcome<RootNestedFailure>(layout, Bytes, "root", null, "failure inside a nested element");
        AssertSameOutcome<RootNoConstructor>(layout, Bytes, "root", null, "nested type without a constructor");
        AssertSameOutcome<RootThrowingSetter>(layout, Bytes, "root", null, "throwing setter");
        AssertSameOutcome<RootAmbiguousMembers>(layout, Bytes, "root", null, "ambiguous target members");
        AssertSameOutcome<int>(layout, Bytes, "root", null, "non-POCO target");
        AssertSameOutcome<string>(layout, Bytes, "root", null, "string target");

        var ambiguousSource = new CStruct("struct root { uint8 value; uint8 Value; uint8 other; };");
        AssertSameOutcome<AmbiguousSourceTarget>(ambiguousSource, [1, 2, 3], "root", null, "ambiguous source members");

        CStructReadException nested = Assert.Throws<CStructReadException>(() => layout.ReadValue<RootNestedFailure>(Bytes, "root"));
        StringAssert.Contains(nested.Message, "'root.leaves[0].v'");

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

    public sealed class Leaf
    {
        public byte K { get; set; }

        public uint V { get; set; }
    }

    public sealed class Inner
    {
        public Leaf First { get; set; } = null!;

        public Leaf Second { get; set; } = null!;

        public ushort Pad { get; set; }
    }

    public sealed class RootExact
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
    }

    public sealed class RootConverted
    {
        public long magic;

        public string? tag;

        public KindEnum which;

        public InnerFields nested = null!;

        public List<long> samples = [];

        public IReadOnlyList<int> deltas = [];

        public IEnumerable<byte> none = [];

        public int? p;

        public decimal q;

        public List<Leaf> leaves = [];

        public double tail;

        public enum KindEnum : byte
        {
            A = 1,
            B = 2,
        }
    }

    public sealed class InnerFields
    {
        public Leaf first = null!;

        public object second = null!;

        public IReadOnlyDictionary<string, object?>? pad;
    }

    public sealed class RootUntyped
    {
        public object? Magic { get; set; }

        public object? Nested { get; set; }

        public IList<object?>? Samples { get; set; }

        public object? Leaves { get; set; }

        public StructValue? nested { get; set; }
    }

    public sealed class RootMissingMember
    {
        public ushort Magic { get; set; }

        public int Missing { get; set; }
    }

    public sealed class RootOverflow
    {
        public byte Magic { get; set; }
    }

    public sealed class RootNotNumeric
    {
        public bool Magic { get; set; }
    }

    public sealed class RootNestedFailure
    {
        public LeafOverflow[] Leaves { get; set; } = [];

        public sealed class LeafOverflow
        {
            public byte K { get; set; }

            public bool V { get; set; }
        }
    }

    public sealed class RootNoConstructor
    {
        public NoConstructor Nested { get; set; } = null!;

        public sealed class NoConstructor
        {
            public NoConstructor(int seed)
            {
                this.Pad = seed;
            }

            public int Pad { get; set; }
        }
    }

    public sealed class RootThrowingSetter
    {
        public ushort Magic
        {
            get => 0;
            set => throw new InvalidOperationException("rejected " + value);
        }
    }

    public sealed class RootAmbiguousMembers
    {
        public ushort Magic { get; set; }

        public ushort magic { get; set; }
    }

    public sealed class AmbiguousSourceTarget
    {
        public byte VALUE { get; set; }

        public byte Other { get; set; }
    }

    public sealed class Small
    {
        public byte A { get; set; }

        public uint B { get; set; }

        public byte C { get; set; }
    }
}
