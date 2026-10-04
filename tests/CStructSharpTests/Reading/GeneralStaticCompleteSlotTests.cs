namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CStructSharp.Compilation;
using CStructSharp.Values;

/// <summary>Checks complete nested static-plan values reached through the general engine, including publication and mutable ownership.</summary>
[TestClass]
public class GeneralStaticCompleteSlotTests
{
    private const string FixedLayout = """
        enum kind : uint8 { first = 1, second = 2 };
        struct leaf {
            uint8 _;
            struct { uint8 x; uint16 y; };
            uint8 none[0];
            char label[3];
            kind tag;
        };
        struct root { leaf child; leaf children[2]; };
        """;

    private static readonly IReadOnlyDictionary<string, int> Variables = new Dictionary<string, int>();

    /// <summary>Nested scalar and array values retain promoted presence, insertion order, text policy and independent ownership.</summary>
    /// <param name="streamed">Whether the general engine stages a non-exposable stream instead of reading memory directly.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompleteNestedSlots_PreserveMutationAndOwnership(bool streamed)
    {
        var layout = new CStruct(FixedLayout);
        byte[] bytes = [0, 1, 2, 0, 65, 0, 0, 1, 0, 3, 4, 0, 66, 0, 0, 2, 0, 5, 6, 0, 67, 0, 0, 1];
        var root = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        Assert.IsNotNull(root.StaticPlan, "The supplied variables force this fixed root through the general engine's static plan.");
        var options = new ReadOptions { TrimFixedText = true };
        StructValue planned = ParseGeneral(layout, bytes, streamed, options);
        StructValue ordinary = ParseGeneral(layout, bytes, streamed, ExecutionPaths.NoFastPaths(options));
        Assert.AreEqual(OperationOutcome.Render(ordinary), OperationOutcome.Render(planned));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", planned));

        foreach (StructValue value in new[] { planned, ordinary })
        {
            var child = (StructValue)value["child"]!;
            Assert.AreEqual(5, child.Count);
            CollectionAssert.AreEqual(new[] { "x", "y", "none", "label", "tag" }, child.Keys.ToArray());
            Assert.AreEqual("A", child["label"]);
            Assert.AreEqual(0, ((IList<object?>)child["none"]!).Count);

            // Reinsertions must use mutation order even though complete construction initially uses shape order.
            Assert.IsTrue(child.Remove("x"));
            child.Add("extra", null);
            child.Add("x", (byte)9);
            Assert.AreEqual(6, child.Count);
            CollectionAssert.AreEqual(new[] { "y", "none", "label", "tag", "extra", "x" }, child.Keys.ToArray());
            Assert.IsTrue(child.ContainsKey("extra"));
        }

        var children = (IList<object?>)planned["children"]!;
        ((StructValue)children[0]!)["x"] = (byte)42;
        Assert.AreEqual((byte)5, ((StructValue)children[1]!)["x"]);
        Assert.AreEqual((byte)3, ((StructValue)((IList<object?>)ordinary["children"]!)[0]!)["x"]);
        Assert.AreEqual((byte)3, bytes[9]);

        var cleared = (StructValue)children[0]!;
        cleared.Clear();
        Assert.AreEqual(0, cleared.Count);
        Assert.IsFalse(cleared.Any());
        cleared.Add("y", null);
        Assert.AreEqual(1, cleared.Count);
        Assert.IsTrue(cleared.ContainsKey("y"));
        CollectionAssert.AreEqual(new[] { "y" }, cleared.Keys.ToArray());
    }

    /// <summary>Qualified counts survive later array-element captures while a bare count retains the last captured value.</summary>
    /// <param name="streamed">Whether the fixed header is staged from a stream.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompleteNestedSlots_PreserveQualifiedAndSequentialCaptures(bool streamed)
    {
        var layout = new CStruct("struct count { uint8 n; }; struct header { count inner; count repeated[2]; }; struct root { header prefix; uint8 values[prefix.inner.n]; uint8 last[n]; uint8 tail; };");
        byte[] bytes = [2, 1, 3, 41, 42, 51, 52, 53, 99];
        StructValue planned = ParseGeneral(layout, bytes, streamed);
        StructValue ordinary = ParseGeneral(layout, bytes, streamed, ExecutionPaths.NoFastPaths());
        Assert.AreEqual(OperationOutcome.Render(ordinary), OperationOutcome.Render(planned));
        CollectionAssert.AreEqual(new byte[] { 41, 42 }, planned.Get<byte[]>("values"));
        CollectionAssert.AreEqual(new byte[] { 51, 52, 53 }, planned.Get<byte[]>("last"));
        Assert.AreEqual((byte)99, planned["tail"]);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", planned));
    }

    /// <summary>Padding-only nested values start empty and remain independently mutable when their slot arrays are empty.</summary>
    [TestMethod]
    public void CompleteNestedSlots_KeepEmptyShapesIndependent()
    {
        var layout = new CStruct("struct padding { uint8 _; }; struct root { padding first; padding rest[2]; uint8 tail; };");
        StructValue parsed = ParseGeneral(layout, [0, 0, 0, 7], false);
        var first = (StructValue)parsed["first"]!;
        var rest = (IList<object?>)parsed["rest"]!;
        Assert.AreEqual(0, first.Count);
        Assert.AreEqual(0, ((StructValue)rest[0]!).Count);
        Assert.AreEqual(0, ((StructValue)rest[1]!).Count);
        first.Add("extra", null);
        Assert.AreEqual(1, first.Count);
        Assert.IsFalse(((StructValue)rest[0]!).ContainsKey("extra"));
        Assert.AreEqual((byte)7, parsed["tail"]);
    }

    /// <summary>Truncations and limits retain the original fallback failures and stream positions around fixed nested construction.</summary>
    [TestMethod]
    public void CompleteNestedSlots_PreserveFallbackFailuresAndPositions()
    {
        var layout = new CStruct("struct leaf { uint8 x; uint16 y; }; struct branch { leaf first; leaf children[2]; }; struct root { branch header; uint8 count; uint8 tail[count]; };");
        byte[] bytes = [1, 2, 0, 3, 4, 0, 5, 6, 0, 2, 41, 42];
        for (int length = 0; length <= bytes.Length; length++)
        {
            AssertSameOutcome(layout, bytes[..length], null, "length " + length);
        }

        for (int budget = 1; budget <= bytes.Length; budget++)
        {
            AssertSameOutcome(layout, bytes, new ReadOptions { MaxTotalBytesRead = budget }, "budget " + budget);
        }

        AssertSameOutcome(layout, bytes, new ReadOptions { MaxArrayElements = 1 }, "array limit");
        AssertSameOutcome(layout, bytes, new ReadOptions { MaxNestingDepth = 2 }, "nesting limit");
        AssertSameOutcome(layout, bytes, new ReadOptions { MaxNestingDepth = 3 }, "exact nesting limit");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertSameOutcome(layout, bytes, new ReadOptions { CancellationToken = cancelled.Token }, "cancelled");
    }

    /// <summary>Parses with supplied variables so even a fixed root uses the general engine.</summary>
    /// <param name="layout">The prepared layout.</param>
    /// <param name="bytes">The input bytes, never modified.</param>
    /// <param name="streamed">Whether to use a non-exposable stream.</param>
    /// <param name="options">Optional text policy, limits or execution path.</param>
    /// <returns>The complete owned root.</returns>
    private static StructValue ParseGeneral(CStruct layout, byte[] bytes, bool streamed, ReadOptions? options = null)
    {
        if (!streamed)
        {
            return layout.Parse(bytes.AsSpan(), "root", Variables, options);
        }

        using var stream = new MemoryStream(bytes, writable: false);
        return layout.Parse(stream, "root", Variables, options);
    }

    /// <summary>Compares exact operation outcomes and final positions with member-by-member execution for memory and stream sources.</summary>
    /// <param name="layout">The prepared layout.</param>
    /// <param name="bytes">The complete or truncated input.</param>
    /// <param name="options">The limits or cancellation token under test.</param>
    /// <param name="label">The case description reported on failure.</param>
    private static void AssertSameOutcome(CStruct layout, byte[] bytes, ReadOptions? options, string label)
    {
        // Both callbacks run the same input with supplied variables; only static-plan eligibility differs.
        OperationOutcome fast = OperationOutcome.Of(() => ParseGeneral(layout, bytes, false, options), typeof(OperationCanceledException));

        // The reference retains every ordinary reader check and its complete error context.
        OperationOutcome ordinary = OperationOutcome.Of(() => ParseGeneral(layout, bytes, false, ExecutionPaths.NoFastPaths(options)), typeof(OperationCanceledException));
        OperationOutcome.AssertSame(ordinary, fast, label + " span");

        using var fastStream = new MemoryStream(bytes, writable: false);
        using var ordinaryStream = new MemoryStream(bytes, writable: false);

        // Compare staged fixed-block reads against ordinary reads from an equivalent non-exposable stream.
        fast = OperationOutcome.Of(() => layout.Parse(fastStream, "root", Variables, options), typeof(OperationCanceledException));

        // The reference must finish at the same stream position on success or failure.
        ordinary = OperationOutcome.Of(() => layout.Parse(ordinaryStream, "root", Variables, ExecutionPaths.NoFastPaths(options)), typeof(OperationCanceledException));
        OperationOutcome.AssertSame(ordinary, fast, label + " stream");
        Assert.AreEqual(ordinaryStream.Position, fastStream.Position, label + " stream position");
    }
}
