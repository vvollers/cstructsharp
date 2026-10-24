namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the static read plan: a fully fixed composite read from one span must be indistinguishable from the engine's
///     member-by-member reads - values, captured variables, limits, truncation failures and final positions. The plan must exist
///     exactly for the composites its conditions describe, and a completed plan keeps the nesting depth of the
///     runtime-sized records that follow. (That every fixed benchmark fixture root keeps its plan is checked per fixture
///     by <see cref="EngineCorpusTests.BenchmarkFixture_MatchesItsExpectationOnEverySourceAndPath"/>.)
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

    /// <summary>Complete direct-plan slots preserve presence, insertion order and independent nested ownership after mutation.</summary>
    [TestMethod]
    public void DirectPlan_CompleteSlots_MatchGeneralResultMutation()
    {
        var layout = new CStruct("struct leaf { uint8 x; uint16 y; }; struct root { uint8 _; struct { uint8 a; uint8 b; }; leaf children[2]; uint8 none[0]; };");
        byte[] bytes = [0, 1, 2, 3, 4, 0, 5, 6, 0];
        StructValue direct = layout.Parse(bytes, "root");
        StructValue general = layout.Parse(bytes, "root", options: ExecutionPaths.NoFastPaths());
        Assert.AreEqual(OperationOutcome.Render(general), OperationOutcome.Render(direct));
        CollectionAssert.AreEqual(new[] { "a", "b", "children", "none" }, direct.Keys.ToArray());
        Assert.AreEqual(4, direct.Count);

        // Removing and reinserting a shape slot must append it after an extra member on either path.
        foreach (StructValue value in new[] { direct, general })
        {
            Assert.IsTrue(value.Remove("a"));
            value["extra"] = null;
            value.Add("a", (byte)9);
            Assert.AreEqual(5, value.Count);
            CollectionAssert.AreEqual(new[] { "b", "children", "none", "extra", "a" }, value.Keys.ToArray());
            Assert.IsTrue(value.ContainsKey("extra"));
        }

        var children = (IList<object?>)direct["children"]!;
        ((StructValue)children[0]!)["x"] = (byte)42;
        Assert.AreEqual((byte)5, ((StructValue)children[1]!)["x"]);
        Assert.AreEqual((byte)3, ((StructValue)((IList<object?>)general["children"]!)[0]!)["x"]);
        direct.Clear();
        Assert.AreEqual(0, direct.Count);
        Assert.IsFalse(direct.Any());
        direct.Add("b", null);
        Assert.IsTrue(direct.ContainsKey("b"));
        Assert.AreEqual(1, direct.Count);
        CollectionAssert.AreEqual(new[] { "b" }, direct.Keys.ToArray());
    }

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
    ///     A condition on a member of an anonymous (promoted) struct, or of a nested struct, is checked by every runtime read:
    ///     the layout plan decodes such a struct for a write but <see cref="CompiledCompositeType.StaticPlan"/> hides it from reads.
    /// </summary>
    /// <param name="definition">A layout whose only constraint sits below the root.</param>
    [TestMethod]
    [DataRow("struct root { struct { uint8 v @expect(v < 4); }; uint8 b; };")]
    [DataRow("struct root { struct { struct { uint8 v @expect(v < 4); }; }; uint8 b; };")]
    [DataRow("struct inner { uint8 v @expect(v < 4); }; struct root { inner i; uint8 b; };")]
    [DataRow("struct inner { uint8 v @expect(v < 4); }; struct root { inner i[1]; uint8 b; };")]
    public void ConditionBelowTheRoot_IsCheckedByEveryRuntimeRead_AndKeepsTheWritePlan(string definition)
    {
        var layout = new CStruct(definition);
        var composite = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        Assert.IsNull(composite.StaticPlan, "a read must not use a plan that checks nothing");
        Assert.IsTrue(composite.LayoutPlan is { HasConstraints: true, SupportsWrite: true }, "a write keeps the fixed plan");

        byte[] good = [1, 9];
        byte[] bad = [200, 9];
        Assert.IsNotNull(layout.Parse(good, "root"));
        Assert.AreEqual("v < 4", Assert.Throws<CStructConstraintException>(() => layout.Parse(bad, "root")).Constraint);
        Assert.AreEqual("v < 4", Assert.Throws<CStructConstraintException>(() => layout.Parse(bad, "root", options: new ReadOptions { ExecutionPath = ExecutionPath.NoFastPaths })).Constraint);
        Assert.AreEqual("v < 4", Assert.Throws<CStructConstraintException>(() => layout.ReadValue<StructValue>(bad, "root")).Constraint);
        Assert.AreEqual("v < 4", Assert.Throws<CStructConstraintException>(() => layout.Parse(new MemoryStream(bad), "root")).Constraint);
    }

    /// <summary>A struct with its own conditions is written through the fixed plan, byte for byte as the member-by-member writer does, and conditions are not checked on a write.</summary>
    [TestMethod]
    public void ConstrainedStruct_WritesThroughThePlan_WithoutCheckingConditions()
    {
        var layout = new CStruct("struct root { uint16 a @expect(a != 0); uint8 b @supports(b < 4); };");
        var composite = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        Assert.IsNull(composite.StaticPlan);
        Assert.IsTrue(composite.LayoutPlan is { HasConstraints: true, SupportsWrite: true });

        var value = new Dictionary<string, object?> { ["a"] = (ushort)0, ["b"] = (byte)200 };
        byte[] expected = [0, 0, 200];
        CollectionAssert.AreEqual(expected, layout.Serialize("root", value));
    }

    /// <summary>
    ///     A fixed struct whose own fields carry conditions reads through the plan with the conditions checked right after
    ///     their field: for every byte changed and every truncation, the outcome (value, failure, member, offset, final stream
    ///     position) is the member-by-member reader's.
    /// </summary>
    /// <param name="definition">A layout whose root has conditions on its own fields.</param>
    /// <param name="goodHex">Bytes that satisfy every condition.</param>
    [TestMethod]
    [DataRow("struct root { uint32 magic @expect(magic == 0xCAFEBABE); uint16 version @supports(version <= 2); uint16 flags; uint32 length @expect(length < 4096); uint8 kind @expect(kind != 0); uint8 tag[3]; };", "BE BA FE CA 02 00 00 00 10 00 00 00 01 41 42 43")]
    [DataRow("struct root { uint8 a; uint8 b @expect(b > a) @supports(b < 100); uint16 c @expect(c == a + b); };", "01 05 06 00")]
    [DataRow("enum k : uint8 { x = 1, y = 2 }; struct root { k e @expect(e != 0); char name[4]; uint8 z @expect(z != 1); };", "01 61 62 63 64 02")]
    [DataRow("struct root { uint8 d; uint8 q @expect(100 / d > 1); uint8 r[3]; };", "04 00 01 02 03")]
    [DataRow("struct root { uint64 wide @expect(wide != 0) @expect(wide < 0x100000000); int16 signed @expect(signed >= -5); };", "05 00 00 00 00 00 00 00 FB FF")]
    public void FixedRootWithConditions_ReadsThroughThePlanLikeTheMemberByMemberReader(string definition, string goodHex)
    {
        var layout = new CStruct(definition);
        var composite = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        Assert.IsNull(composite.StaticPlan);
        Assert.IsTrue(composite.LayoutPlan is { HasConstraints: true, ConstraintsAreDirect: true });

        byte[] good = Convert.FromHexString(goodHex.Replace(" ", string.Empty, StringComparison.Ordinal));
        AssertSameOutcome(layout, good, null, "good");
        for (int length = 0; length < good.Length; length++)
        {
            AssertSameOutcome(layout, good[..length], null, "truncated to " + length);
        }

        AssertSameOutcome(layout, [.. good, 0xEE, 0xEE], null, "trailing bytes");
        foreach (ReadOptions options in new[]
        {
            new ReadOptions { MaxArrayElements = 2 },
            new ReadOptions { MaxTotalBytesRead = 3 },
            new ReadOptions { MaxNestingDepth = 1 },
            new ReadOptions { TrimFixedText = true },
        })
        {
            AssertSameOutcome(layout, good, options, "limits " + options.MaxArrayElements + "/" + options.MaxTotalBytesRead + "/" + options.MaxNestingDepth);
        }

        for (int index = 0; index < good.Length; index++)
        {
            foreach (byte changed in new byte[] { 0, 1, 2, 0x7F, 0x80, 0xFF })
            {
                byte[] bytes = (byte[])good.Clone();
                bytes[index] = changed;
                AssertSameOutcome(layout, bytes, null, "byte " + index + " = " + changed);
            }
        }
    }

    /// <summary>A promoted or nested struct with conditions keeps the whole struct on the member-by-member reader; a nested struct array of constrained elements too.</summary>
    [TestMethod]
    public void ForeignConditions_KeepTheStructOffTheCheckedPlan()
    {
        foreach (string definition in new[]
        {
            "struct root { struct { uint8 v @expect(v < 4); }; uint8 b @expect(b != 0); };",
            "struct inner { uint8 v @expect(v < 4); }; struct root { inner i; uint8 b @expect(b != 0); };",
        })
        {
            var composite = (CompiledCompositeType)new CStruct(definition).CompiledModel.Symbols["root"].Symbol.Definition!;
            Assert.IsTrue(composite.LayoutPlan is { HasConstraints: true, ConstraintsAreDirect: false }, definition);
        }
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

    /// <summary>Full parses, every truncation, every read budget and small limits behave identically with the plan and member by member, from a span and from streams.</summary>
    [TestMethod]
    public void StaticPlan_MatchesMemberByMemberReads_OnValuesFailuresAndPositions()
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
    public void StaticPlan_MatchesMemberByMemberReads_FromUnalignedStreamPositions()
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
            string memberByMember = OperationOutcome.Render(layout.Parse(withoutPlan, "root", options: ExecutionPaths.NoFastPaths()));
            Assert.AreEqual(memberByMember, fast, $"start {start}");
            Assert.AreEqual(withoutPlan.Position, withPlan.Position, $"start {start}: position");
        }
    }

    /// <summary>A dynamic array of a fully fixed struct (the element plan looped over one span) matches member-by-member reads on values, captured counts, truncation, budgets and limits.</summary>
    [TestMethod]
    public void DynamicArray_OfStaticStructs_MatchesMemberByMemberReads()
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

    /// <summary>The same input with and without the plan (<see cref="ExecutionPath.NoFastPaths"/>) for a span, a MemoryStream without an exposed buffer (block path) and a 5-byte chunked stream.</summary>
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
            OperationOutcome memberByMember = OperationOutcome.Of(() => withoutPlan is null ? layout.Parse(bytes, "root", options: ExecutionPaths.NoFastPaths(options)) : layout.Parse(withoutPlan, "root", options: ExecutionPaths.NoFastPaths(options)));
            string caseLabel = label + " / " + source;
            OperationOutcome.AssertSame(memberByMember, fast, caseLabel);
            Assert.AreEqual(withoutPlan?.Position, withPlan?.Position, caseLabel + ": final position");
        }
    }
}
