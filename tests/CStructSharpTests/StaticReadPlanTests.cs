namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the static read plan: a fully fixed composite read from one span must be indistinguishable from
///     the general reader - values, captured variables, limits, truncation failures and final positions - and the
///     plan must exist exactly for the composites the plan's conditions describe.
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

    /// <summary>Composites get a plan exactly when every member is statically placed and decodable.</summary>
    [TestMethod]
    public void StaticPlan_ExistsForFixedComposites_Only()
    {
        Assert.IsTrue(HasPlan("struct root { uint16 a; uint32 b; char name[4]; uint8 raw[8]; };", "root"));
        Assert.IsTrue(HasPlan("struct leaf { uint8 k; }; struct root { leaf x; leaf y; struct { uint8 p; }; };", "root"));
        Assert.IsTrue(HasPlan("enum e : uint16 { one = 1 }; struct root { e value; };", "root"));
        Assert.IsTrue(HasPlan("struct root { uint16 a; uint32 b; };", "root", aligned: true));
        Assert.IsFalse(HasPlan("struct root { uint8 n; uint8 items[n]; };", "root"), "dynamic array");
        Assert.IsFalse(HasPlan("struct root { uint8 flag; if (flag == 1) { uint8 yes; } };", "root"), "conditional");
        Assert.IsFalse(HasPlan("struct root { uint8 *p; };", "root"), "pointer");
        Assert.IsFalse(HasPlan("struct root { uint8 low : 4; uint8 high : 4; };", "root"), "bitfield");
        Assert.IsFalse(HasPlan("union u { uint8 a; uint16 b; }; struct root { u value; };", "root"), "union member");
        Assert.IsFalse(HasPlan("struct root { uint8 grid[2][2]; };", "root"), "multidimensional array");
        Assert.IsFalse(HasPlan("struct root { wchar name[4]; };", "root"), "wide characters");
        Assert.IsFalse(HasPlan("struct root { uint8 a; uint8 b @1; };", "root"), "offset assertion");
        Assert.IsFalse(HasPlan("struct root { utf8 text[4]; };", "root"), "bounded text");
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
            string fast = Render(layout.Parse(withPlan, "root"));
            using var withoutPlan = new MemoryStream(bytes, writable: false);
            withoutPlan.Position = start;
            StaticReadPlan.DisabledForTesting = true;
            string general = Render(layout.Parse(withoutPlan, "root"));
            StaticReadPlan.DisabledForTesting = false;
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

    private static bool HasPlan(string definition, string root, bool aligned = false)
    {
        var layout = new CStruct(definition, aligned: aligned);
        CompiledLayoutModel model = layout.CompiledModel;
        CompiledCompositeType composite = (CompiledCompositeType)model.Symbols[root].Symbol.Definition!;
        return composite.StaticPlan is not null;
    }

    /// <summary>The same input with and without the plan (thread-static test hook) for a span, a MemoryStream without an exposed buffer (block path) and a 5-byte chunked stream.</summary>
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
            (object? fast, Exception? fastError) = Try(() => withPlan is null ? layout.Parse(bytes, "root", options: options) : layout.Parse(withPlan, "root", options: options));
            StaticReadPlan.DisabledForTesting = true;
            (object? general, Exception? generalError) = Try(() => withoutPlan is null ? layout.Parse(bytes, "root", options: options) : layout.Parse(withoutPlan, "root", options: options));
            StaticReadPlan.DisabledForTesting = false;
            string caseLabel = label + " / " + source;
            Assert.AreEqual(generalError?.GetType(), fastError?.GetType(), caseLabel);
            Assert.AreEqual((generalError as CStructException)?.Offset, (fastError as CStructException)?.Offset, caseLabel + ": failure offset");
            Assert.AreEqual(withoutPlan?.Position, withPlan?.Position, caseLabel + ": final position");
            if (fastError is null)
            {
                Assert.AreEqual(Render(general), Render(fast), caseLabel);
            }
        }
    }

    private static (object? Result, Exception? Error) Try(Func<object> parse)
    {
        try
        {
            return (parse(), null);
        }
        catch (CStructException exception)
        {
            return (null, exception);
        }
    }

    private static string Render(object? value)
    {
        return value switch
        {
            null => "null",
            StructValue s => "{" + string.Join(",", s.Select(pair => pair.Key + ":" + Render(pair.Value))) + "}",
            EnumValueResult e => e.Enum + "." + (e.Name ?? "?") + "=" + e.Value,
            string text => "\"" + text + "\"",
            System.Collections.IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(Render)) + "]",
            _ => value.GetType().Name + ":" + value,
        };
    }
}
