namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the static read plan: a fully fixed composite read from one span must be indistinguishable from the general
///     reader - values, captured variables, limits, truncation failures and final positions. The plan must exist
///     exactly for the composites its conditions describe - every fixed benchmark fixture root among them, since a
///     composite that looks dynamic to the planner loses the plan speed-up - and a completed plan keeps the nesting
///     depth of the runtime-sized records that follow.
/// </summary>
[TestClass]
public class StaticReadPlanTests
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
            uint8 none[0];
            struct { uint8 p; uint8 q; };
            uint8 n;
            uint8 items[n];
            uint8 tail;
        };
        """;

    /// <summary>The benchmark fixture ids whose roots are fixed composites and so must have a static read plan.</summary>
    private static readonly string[] PlannedFixtureRoots =
    [
        "aligned-x256", "array-struct-100", "array-struct-10000", "array-u32-be-16384", "array-u32-be-256",
        "array-u32-be-262144", "array-u32-le-16384", "array-u32-le-256", "array-u32-le-262144",
        "array-u32-neutral-262144", "array-u64-le-1000000", "array-u8-1024", "array-u8-1048576",
        "array-u8-16m-stream", "array-u8-65536", "compile-large-512", "compile-medium-128", "compile-nested",
        "compile-small", "cond-plain128", "enum-x1k", "malformed-budget-exceeded", "malformed-truncated",
        "mixed-endian-record", "nested-x1", "nested-x256", "prim-be-record", "prim-be-x1k", "prim-le-record",
        "prim-le-x1k", "real-bmp", "real-jpg", "real-png", "real-tar", "real-wav",
    ];

    /// <summary>The parity fixtures whose roots are fixed and must be planned too (aliases resolve at construction; a promoted union keeps the general reader).</summary>
    private static readonly string[] EligibleParityFixtures = ["parity-alias-x1k",];

    /// <summary>Composites get a plan exactly when every member is statically placed and decodable.</summary>
    [TestMethod]
    public void StaticPlan_ExistsForFixedComposites_Only()
    {
        Assert.IsTrue(HasPlan("struct root { uint16 a; uint32 b; char name[4]; uint8 raw[8]; };", "root"));
        Assert.IsTrue(HasPlan("struct leaf { uint8 k; }; struct root { leaf x; leaf y; struct { uint8 p; }; };", "root"));
        Assert.IsTrue(HasPlan("enum e : uint16 { one = 1 }; struct root { e value; };", "root"));
        Assert.IsTrue(HasPlan("struct root { uint16 a; uint32 b; };", "root", aligned: true));
        Assert.IsTrue(HasPlan("struct root { uint8 a; uint8 b @1; };", "root"), "offset assertion checked at construction");
        Assert.IsFalse(HasPlan("struct root { uint8 n; uint8 items[n]; };", "root"), "dynamic array");
        Assert.IsFalse(HasPlan("struct root { uint8 flag; if (flag == 1) { uint8 yes; } };", "root"), "conditional");
        Assert.IsFalse(HasPlan("struct root { uint8 *p; };", "root"), "pointer");
        Assert.IsFalse(HasPlan("struct root { uint8 low : 4; uint8 high : 4; };", "root"), "bitfield");
        Assert.IsFalse(HasPlan("union u { uint8 a; uint16 b; }; struct root { u value; };", "root"), "union member");
        Assert.IsFalse(HasPlan("struct root { uint8 grid[2][2]; };", "root"), "multidimensional array");
        Assert.IsFalse(HasPlan("struct root { wchar name[4]; };", "root"), "wide characters");
        Assert.IsFalse(HasPlan("struct root { utf8 text[4]; };", "root"), "bounded text");
    }

    /// <summary>
    ///     A struct's plan reuses the cached plan of each nested struct, and a chain of nested structs has a plan down to
    ///     nesting level 64 (the outermost struct is level 0) and none beyond it.
    /// </summary>
    [TestMethod]
    public void StaticPlan_ReusesNestedPlans_AndStopsAtTheNestingLimit()
    {
        var layout = new CStruct("struct leaf { uint8 k; }; struct root { leaf x; leaf y; };");
        StaticReadPlan root = Composite(layout, "root").StaticPlan!;
        StaticReadPlan leaf = Composite(layout, "leaf").StaticPlan!;
        Assert.IsTrue(root.Operations.All(operation => ReferenceEquals(leaf, operation.NestedPlan)));

        Assert.IsTrue(HasPlan(NestedChain(FixedLayoutRule.MaximumNestingDepth), "s0"), "levels 0 to 64");
        Assert.IsFalse(HasPlan(NestedChain(FixedLayoutRule.MaximumNestingDepth + 1), "s0"), "level 65");
    }

    /// <summary>Full parses, every truncation, every read budget and small limits behave identically through the span (plan) and chunked-stream (general) paths.</summary>
    [TestMethod]
    public void StaticPlan_MatchesGeneralReader_OnValuesFailuresAndPositions()
    {
        var layout = new CStruct(Layout);
        byte[] bytes = [0x34, 0x12, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 2, 9, 1, 0, 0, 0, 8, 2, 0, 0, 0, 0xEE, 0xFF, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0xAA, 0xBB, 2, 0x51, 0x52, 0x99];
        dynamic parsed = layout.Parse(bytes, "root");
        Assert.AreEqual((ushort)0x1234, parsed.magic);
        Assert.AreEqual("IHDR", parsed.tag);
        Assert.AreEqual("b", ((EnumValueResult)parsed.which).Name);
        Assert.AreEqual(0x00000001u, parsed.nested.first.v);
        Assert.AreEqual((byte)8, parsed.nested.second.k);
        Assert.AreEqual((ushort)0xFFEE, parsed.nested.pad);
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, ((PrimitiveArray<uint>)parsed.samples).ToArray());
        Assert.AreEqual(0, ((IList<object?>)parsed.none).Count);
        Assert.AreEqual((byte)0xBB, parsed.q);
        Assert.AreEqual(2, ((IList<object?>)parsed.items).Count, "n was captured by the static path for the dynamic sibling");
        Assert.AreEqual((byte)0x99, parsed.tail);

        for (int length = 0; length < bytes.Length; length++)
        {
            AssertSameOutcome(layout, bytes[..length], null, $"truncated to {length}");
        }

        for (long budget = 1; budget <= bytes.Length; budget++)
        {
            AssertSameOutcome(layout, bytes, new ReadOptions { MaxTotalBytesRead = budget }, $"budget {budget}");
        }

        AssertSameOutcome(layout, bytes, new ReadOptions { MaxArrayElements = 2 }, "array limit");
        AssertSameOutcome(layout, bytes, new ReadOptions { MaxNestingDepth = 2 }, "nesting limit");
        AssertSameOutcome(layout, bytes, new ReadOptions { MaxNestingDepth = 3 }, "nesting limit 3");
    }

    /// <summary>A stream positioned off the struct's alignment boundary reads identically with and without the plan (members align to absolute positions).</summary>
    [TestMethod]
    public void StaticPlan_MatchesGeneralReader_FromUnalignedStreamPositions()
    {
        var layout = new CStruct("struct root { uint8 a; uint32 b; uint8 c; };", aligned: true);
        byte[] bytes = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
        foreach (int start in new[] { 0, 1, 3, 4, 6, 8 })
        {
            using var withPlan = new MemoryStream(bytes, writable: false);
            withPlan.Position = start;
            string fast = OperationOutcome.Render(layout.Parse(withPlan, "root"));
            using var withoutPlan = new MemoryStream(bytes, writable: false);
            withoutPlan.Position = start;
            string general = OperationOutcome.Render(layout.Parse(withoutPlan, "root", options: ExecutionPaths.GeneralOnly()));
            Assert.AreEqual(general, fast, $"start {start}");
            Assert.AreEqual(withoutPlan.Position, withPlan.Position, $"start {start}: position");
        }
    }

    /// <summary>A dynamic array of a fully fixed struct (the element plan looped over one span) matches the general reader on values, captured counts, truncation, budgets and limits.</summary>
    [TestMethod]
    public void DynamicArray_OfStaticStructs_MatchesGeneralReader()
    {
        const string definition = "enum e : uint8 { a = 1 }; struct child { uint8 k; e w; uint16 v; }; struct root { uint8 count; child items[count]; uint8 last; uint8 tail[last]; };";
        byte[] packed = [3, 1, 1, 0x34, 0x12, 2, 9, 0x78, 0x56, 3, 1, 0xFF, 0xFF, 2, 0xAA, 0xBB];
        object value = new CStruct(definition).Parse(packed, "root");
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(definition, aligned: aligned);
            byte[] bytes = layout.Serialize("root", value);
            dynamic parsed = layout.Parse(bytes, "root");
            Assert.AreEqual(3, ((IList<object?>)parsed.items).Count);
            Assert.AreEqual((ushort)0x5678, parsed.items[1].v);
            Assert.AreEqual(2, ((IList<object?>)parsed.tail).Count, "last was captured after the planned array");

            for (int length = 0; length < bytes.Length; length++)
            {
                AssertSameOutcome(layout, bytes[..length], null, $"aligned={aligned} truncated to {length}");
            }

            for (long budget = 1; budget <= bytes.Length; budget++)
            {
                AssertSameOutcome(layout, bytes, new ReadOptions { MaxTotalBytesRead = budget }, $"aligned={aligned} budget {budget}");
            }

            AssertSameOutcome(layout, bytes, new ReadOptions { MaxArrayElements = 2 }, $"aligned={aligned} array limit");
            AssertSameOutcome(layout, bytes, new ReadOptions { MaxNestingDepth = 1 }, $"aligned={aligned} nesting limit 1");
            AssertSameOutcome(layout, bytes, new ReadOptions { MaxNestingDepth = 2 }, $"aligned={aligned} nesting limit 2");
        }

        var zero = new CStruct("struct child { uint8 k; }; struct root { uint8 count; child items[count]; uint8 tail; };");
        AssertSameOutcome(zero, [0, 7], null, "zero elements");
        AssertSameOutcome(zero, [2, 5, 6, 7], null, "two elements");
    }

    /// <summary>A fixed header cannot release its parent's nesting level before a deeper runtime-sized sibling.</summary>
    [TestMethod]
    public void FixedHeader_PreservesDepthForFollowingRuntimeRecord()
    {
        var layout = new CStruct("struct header { uint8 value; }; struct leaf { uint8 count; uint8 data[count]; }; struct branch { leaf child; }; struct root { header prefix; branch body; };");
        byte[] bytes = [9, 1, 42,];

        // The root, branch and leaf require three levels even after the fixed header has completed.
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxNestingDepth = 2, }));

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxNestingDepth = 3, });

        Assert.AreEqual((byte)9, (byte)parsed.prefix.value);
        Assert.AreEqual((byte)42, (byte)parsed.body.child.data[0]);
    }

    /// <summary>Plan eligibility of every recorded fixture root is unchanged, and the fixed parity roots are planned.</summary>
    [TestMethod]
    public void FixtureRoots_KeepTheirStaticPlans()
    {
        string directory = TestFixtures.BenchmarkFixtures;
        var eligible = new List<string>();
        var unplanned = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 4096, });
            JsonElement root = document.RootElement;
            string id = root.GetProperty("id").GetString()!;
            JsonElement options = root.GetProperty("options");
            var layout = new CStruct(
                root.GetProperty("definition").GetString()!,
                options.GetProperty("pointerSize").GetByte(),
                options.GetProperty("aligned").GetBoolean(),
                options.GetProperty("littleEndian").GetBoolean());
            string rootName = root.GetProperty("root").GetString()!;
            bool planned = HasPlan(layout, rootName);
            (planned ? eligible : unplanned).Add(id);
        }

        string[] lost = PlannedFixtureRoots.Where(id => !eligible.Contains(id)).ToArray();
        Assert.IsEmpty(lost, "fixture roots that lost their static read plan: " + string.Join(", ", lost));
        string[] missingParity = EligibleParityFixtures.Where(id => !eligible.Contains(id)).ToArray();
        Assert.IsEmpty(missingParity, "parity fixture roots without a static read plan: " + string.Join(", ", missingParity));
        Console.WriteLine($"{eligible.Count} planned, {unplanned.Count} general-reader fixtures");
    }

    /// <summary>Returns a compiled struct of a layout by name.</summary>
    private static CompiledCompositeType Composite(CStruct layout, string name) => (CompiledCompositeType)layout.CompiledModel.Symbols[name].Symbol.Definition!;

    /// <summary>A chain of structs <c>s0</c> to <c>s{levels}</c>, each holding the next, so <c>s0</c> nests <paramref name="levels"/> levels deep.</summary>
    private static string NestedChain(int levels)
    {
        var text = new System.Text.StringBuilder($"struct s{levels} {{ uint8 v; }};");
        for (int level = levels - 1; level >= 0; level--)
        {
            text.Append($" struct s{level} {{ s{level + 1} next; }};");
        }

        return text.ToString();
    }

    /// <summary>Returns whether a struct of a freshly compiled layout gets a static read plan.</summary>
    private static bool HasPlan(string definition, string root, bool aligned = false)
    {
        var layout = new CStruct(definition, aligned: aligned);
        CompiledLayoutModel model = layout.CompiledModel;
        CompiledCompositeType composite = (CompiledCompositeType)model.Symbols[root].Symbol.Definition!;
        return composite.StaticPlan is not null;
    }

    /// <summary>The same input with and without the plan (the general-only execution path) for a span, a MemoryStream without an exposed buffer (block path) and a 5-byte chunked stream.</summary>
    private static void AssertSameOutcome(CStruct layout, byte[] bytes, ReadOptions? options, string label)
    {
        foreach ((string source, Func<Stream?> create) in new (string, Func<Stream?>)[]
        {
            ("span", () => null),
            ("memory stream", () => new MemoryStream(bytes, writable: false)),
            ("chunked stream", () => new ChunkedMemoryStream(bytes, 5, writable: false)),
        })
        {
            using Stream? withPlan = create();
            using Stream? withoutPlan = create();
            OperationOutcome fast = OperationOutcome.Of(() => withPlan is null ? layout.Parse(bytes, "root", options: options) : layout.Parse(withPlan, "root", options: options));
            OperationOutcome general = OperationOutcome.Of(() => withoutPlan is null ? layout.Parse(bytes, "root", options: ExecutionPaths.GeneralOnly(options)) : layout.Parse(withoutPlan, "root", options: ExecutionPaths.GeneralOnly(options)));
            string caseLabel = label + " / " + source;
            OperationOutcome.AssertSame(general, fast, caseLabel);
            Assert.AreEqual(withoutPlan?.Position, withPlan?.Position, caseLabel + ": final position");
        }
    }

    /// <summary>A composite root with a static read plan; a synthetic root (<c>uint32[256]</c>) has no composite and reads through the bulk primitive path instead.</summary>
    private static bool HasPlan(CStruct layout, string rootName)
    {
        return layout.CompiledModel.Symbols.TryGetValue(rootName, out CompiledTypeReference reference) &&
               reference.Symbol.Definition is CompiledCompositeType { StaticPlan: not null, };
    }
}
