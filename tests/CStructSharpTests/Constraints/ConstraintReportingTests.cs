namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     What a constraint failure carries beyond its condition: the label a layout gives it, the offset base that moves its
///     offset into the larger input, and the collection that gathers every failure of a read instead of stopping at the
///     first, on every runtime path that checks constraints.
/// </summary>
[TestClass]
public class ConstraintReportingTests
{
    private const string Header = "struct hdr { uint16 magic @expect(magic == 0xEF53, \"hdr.magic\"); uint8 version @supports(version <= 2, \"hdr.version\") @expect(version != 0); uint8 n @expect(n <= 3); uint8 vals[n]; uint8 tail @expect(tail != 0, \"hdr.tail\"); };";

    private const string Fixed = "struct fx { uint32 a @expect(a != 0, \"fx.a\"); uint32 b @expect(b > a) @supports(b < 100, \"fx.b\"); uint32 c; };";

    /// <summary>A label is carried by the failure, quoted in its message and absent when the layout gives none.</summary>
    [TestMethod]
    public void Label_IsCarriedByTheFailure()
    {
        var layout = new CStruct(Header);
        CStructConstraintException labelled = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 2, 1, 0, 1 }, "hdr"));
        Assert.AreEqual("hdr.magic", labelled.Label);
        Assert.AreEqual("magic == 0xEF53", labelled.Constraint);
        StringAssert.Contains(labelled.Message, "[hdr.magic]");

        CStructConstraintException plain = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 0x53, 0xEF, 1, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "hdr"));
        Assert.IsNull(plain.Label);
        Assert.IsFalse(plain.Message.Contains('[', StringComparison.Ordinal) && plain.Message.Contains("[hdr", StringComparison.Ordinal));

        CStructConstraintException unsupported = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 0x53, 0xEF, 3, 0, 1 }, "hdr"));
        Assert.AreEqual(ConstraintKind.Unsupported, unsupported.Kind);
        Assert.AreEqual("hdr.version", unsupported.Label);
    }

    /// <summary>The label is part of the layout's text: a rebuilt layout checks and reports the same, and a label with a quote or a backslash survives.</summary>
    [TestMethod]
    public void Label_RoundTripsThroughTheDefinition()
    {
        var layout = new CStruct("struct s { uint8 a @expect(a == 1, \"say \\\"one\\\" \\\\ please\") @supports(a < 5); };");
        string definition = layout.ToDefinition();
        StringAssert.Contains(definition, "@expect(a == 1, \"say \\\"one\\\" \\\\ please\")");
        StringAssert.Contains(definition, "@supports(a < 5)");
        var rebuilt = new CStruct(definition);
        CStructConstraintException failure = Assert.Throws<CStructConstraintException>(() => rebuilt.Parse(new byte[] { 2 }, "s"));
        Assert.AreEqual("say \"one\" \\ please", failure.Label);
    }

    /// <summary>Two layouts that differ only in a label are different layouts.</summary>
    [TestMethod]
    public void Label_ChangesTheFingerprint()
    {
        var one = new CStruct("struct s { uint8 a @expect(a == 1, \"x\"); };");
        var two = new CStruct("struct s { uint8 a @expect(a == 1, \"y\"); };");
        var none = new CStruct("struct s { uint8 a @expect(a == 1); };");
        Assert.AreNotEqual(one.ToDefinition(), two.ToDefinition());
        Assert.AreNotEqual(one.ToDefinition(), none.ToDefinition());
    }

    /// <summary>A label that is empty, unterminated, escaped wrongly or not a string is a layout error.</summary>
    [TestMethod]
    public void MalformedLabels_AreLayoutErrors()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @expect(a == 1, \"\"); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @expect(a == 1, \"open); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @expect(a == 1, \"bad\\n\"); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @expect(a == 1, label); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @expect(a == 1, ); };"));
    }

    /// <summary>With an offset base a failure reports where the bytes are in the larger input, in its property and in its message.</summary>
    [TestMethod]
    public void OffsetBase_MovesEveryFailureIntoTheLargerInput()
    {
        var layout = new CStruct(Header);
        var options = new ReadOptions { OffsetBase = 4096 };

        CStructConstraintException constraint = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 2, 1, 0, 1 }, "hdr", null, options));
        CStructConstraintException local = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 2, 1, 0, 1 }, "hdr"));
        Assert.AreEqual(local.Offset + 4096, constraint.Offset);
        StringAssert.Contains(constraint.Message, "offset " + constraint.Offset);

        CStructReadException shortRead = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 0x53, 0xEF, 1 }, "hdr", null, options));
        CStructReadException shortLocal = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 0x53, 0xEF, 1 }, "hdr"));
        Assert.AreEqual(shortLocal.Offset + 4096, shortRead.Offset);

        // The fixed-root fast path reports the same shift.
        var fixedLayout = new CStruct(Fixed);
        CStructConstraintException fixedFailure = Assert.Throws<CStructConstraintException>(() => fixedLayout.Parse(new byte[12], "fx", null, options));
        CStructConstraintException fixedLocal = Assert.Throws<CStructConstraintException>(() => fixedLayout.Parse(new byte[12], "fx"));
        Assert.AreEqual(fixedLocal.Offset + 4096, fixedFailure.Offset);
    }

    /// <summary>The base changes nothing but the reported offset: a successful read gives the same value, and a negative base is refused.</summary>
    [TestMethod]
    public void OffsetBase_ChangesNothingElse_AndMustNotBeNegative()
    {
        var layout = new CStruct(Header);
        byte[] good = [0x53, 0xEF, 1, 1, 7, 9];
        StructValue plain = layout.Parse(good, "hdr");
        StructValue based = layout.Parse(good, "hdr", null, new ReadOptions { OffsetBase = 1 << 20 });
        Assert.AreEqual(Convert.ToInt32(plain["tail"]), Convert.ToInt32(based["tail"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Parse(good, "hdr", null, new ReadOptions { OffsetBase = -1 }));
        Assert.AreEqual(1 << 20, new ReadLimits { OffsetBase = 1 << 20 }.ToOptions().OffsetBase);
        Assert.AreEqual(5L, ReadLimits.From(new ReadOptions { OffsetBase = 5 }).OffsetBase);
    }

    /// <summary>The offset base takes every value up to 2^48 - 1 in both option types, refuses the rest when it is set, and equal options compare equal.</summary>
    [TestMethod]
    public void OffsetBase_HasTheDocumentedRange_AndOptionsStayComparable()
    {
        long largest = ReadLimits.MaxOffsetBase;
        Assert.AreEqual((1L << 48) - 1, largest);
        Assert.AreEqual(largest, new ReadLimits { OffsetBase = largest }.OffsetBase);
        Assert.AreEqual(largest, new ReadOptions { OffsetBase = largest }.OffsetBase);
        Assert.AreEqual((1L << 32) + 5, new ReadLimits { OffsetBase = (1L << 32) + 5 }.OffsetBase);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadLimits { OffsetBase = largest + 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadLimits { OffsetBase = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadOptions { OffsetBase = largest + 1 });

        var violations = new List<CStructConstraintException>();
        Assert.AreEqual(new ReadOptions { OffsetBase = 9, ConstraintViolations = violations }, new ReadOptions { OffsetBase = 9, ConstraintViolations = violations });
        Assert.AreNotEqual(new ReadOptions { OffsetBase = 9 }, new ReadOptions { OffsetBase = 10 });
        Assert.AreEqual(new ReadOptions(), new ReadOptions { OffsetBase = 0 });
        Assert.AreEqual(9L, (new ReadOptions { OffsetBase = 9 } with { TrimFixedText = true }).OffsetBase);
    }

    /// <summary>A read that collects violations reports every broken condition in field order, with the member, path, offset and label of a thrown failure, and returns the value.</summary>
    [TestMethod]
    public void CollectedViolations_ListEveryFailureInFieldOrder()
    {
        var layout = new CStruct(Header);
        var violations = new List<CStructConstraintException>();

        // magic wrong, version 3 (unsupported, not zero), n 2, tail 0.
        StructValue value = layout.Parse(new byte[] { 1, 2, 3, 2, 7, 8, 0 }, "hdr", null, new ReadOptions { ConstraintViolations = violations });
        Assert.AreEqual(2, Convert.ToInt32(value["n"]));
        Assert.AreEqual(3, violations.Count);
        CollectionAssert.AreEqual(new[] { "magic", "version", "tail" }, violations.Select(violation => violation.Member).ToArray());
        CollectionAssert.AreEqual(new[] { "hdr.magic", "hdr.version", "hdr.tail" }, violations.Select(violation => violation.Label).ToArray());
        CollectionAssert.AreEqual(new[] { ConstraintKind.Malformed, ConstraintKind.Unsupported, ConstraintKind.Malformed }, violations.Select(violation => violation.Kind).ToArray());
        CollectionAssert.AreEqual(new long[] { 2, 3, 7 }, violations.Select(violation => violation.Offset!.Value).ToArray());
        Assert.IsTrue(violations.All(violation => violation.Path == "hdr"));
        StringAssert.Contains(violations[0].Message, "Invalid value for 'magic'");
    }

    /// <summary>Several conditions on one field are each reported, and a satisfied read leaves the collection empty.</summary>
    [TestMethod]
    public void CollectedViolations_EveryConditionOfAFieldAndNoneWhenSatisfied()
    {
        var layout = new CStruct(Header);
        var violations = new List<CStructConstraintException>();

        // version 0 breaks @expect(version != 0), and 0 <= 2 satisfies @supports.
        layout.Parse(new byte[] { 0x53, 0xEF, 0, 0, 1 }, "hdr", null, new ReadOptions { ConstraintViolations = violations });
        CStructConstraintException only = violations.Single();
        Assert.AreEqual("version", only.Member);

        violations.Clear();
        layout.Parse(new byte[] { 0x53, 0xEF, 1, 1, 5, 1 }, "hdr", null, new ReadOptions { ConstraintViolations = violations });
        Assert.AreEqual(0, violations.Count);
    }

    /// <summary>The fixed-offset plan, the direct root read and the member-by-member reader collect the same failures with the same offsets.</summary>
    [TestMethod]
    public void CollectedViolations_AreTheSameOnEveryRuntimePath()
    {
        var layout = new CStruct(Fixed + " struct outer { fx first; fx second; };");
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7, 0, 0, 0, 5, 0, 0, 0, 3, 0, 0, 0, 9, 0, 0, 0];

        var root = new List<CStructConstraintException>();
        layout.Parse(bytes.AsSpan(0, 12), "fx", null, new ReadOptions { ConstraintViolations = root });
        Assert.AreEqual(1, root.Count);
        Assert.AreEqual("fx.a", root[0].Label);
        Assert.AreEqual(4L, root[0].Offset);

        var nested = new List<CStructConstraintException>();
        layout.Parse(bytes, "outer", null, new ReadOptions { ConstraintViolations = nested });
        CollectionAssert.AreEqual(new[] { "a", "b" }, nested.Select(violation => violation.Member).ToArray());
        CollectionAssert.AreEqual(new[] { "fx.a", null }, nested.Select(violation => violation.Label).ToArray());
        CollectionAssert.AreEqual(new long[] { 4, 20 }, nested.Select(violation => violation.Offset!.Value).ToArray());

        var withoutFastPaths = new List<CStructConstraintException>();
        layout.Parse(bytes, "outer", null, new ReadOptions { ConstraintViolations = withoutFastPaths, ExecutionPath = ExecutionPath.NoFastPaths });
        CollectionAssert.AreEqual(nested.Select(violation => violation.Message).ToArray(), withoutFastPaths.Select(violation => violation.Message).ToArray());
        CollectionAssert.AreEqual(nested.Select(violation => violation.Offset!.Value).ToArray(), withoutFastPaths.Select(violation => violation.Offset!.Value).ToArray());
    }

    /// <summary>A failure that is not a constraint still ends the read, and keeps the violations found before it.</summary>
    [TestMethod]
    public void CollectedViolations_DoNotHideOtherFailures()
    {
        var layout = new CStruct(Header);
        var violations = new List<CStructConstraintException>();

        // The count 9 breaks n <= 3 (collected); the 9 values then do not fit the source: a short read.
        Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 0x53, 0xEF, 1, 9, 1 }, "hdr", null, new ReadOptions { ConstraintViolations = violations }));
        Assert.AreEqual("n", violations.Single().Member);
    }

    /// <summary>Collected violations carry the offset base like a thrown failure does.</summary>
    [TestMethod]
    public void CollectedViolations_CarryTheOffsetBase()
    {
        var layout = new CStruct(Header);
        var violations = new List<CStructConstraintException>();
        layout.Parse(new byte[] { 1, 2, 1, 0, 1 }, "hdr", null, new ReadOptions { ConstraintViolations = violations, OffsetBase = 100 });
        Assert.AreEqual(102L, violations.Single().Offset);
    }

    /// <summary>The non-throwing read reports a violation the same way the throwing one does when nothing collects.</summary>
    [TestMethod]
    public void TryReadValue_ReportsTheLabelledFailure()
    {
        var layout = new CStruct(Header);
        Assert.IsFalse(layout.TryReadValue<StructValue>(new byte[] { 1, 2, 1, 0, 1 }, "hdr", out _));
    }
}
