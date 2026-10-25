namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     <c>@magic</c> on the runtime layout: checked before the other conditions of its struct, failing as unsupported, and
///     independent of where the struct is read from (a fixed struct, a runtime-sized one, a nested one).
/// </summary>
[TestClass]
public class MagicConstraintTests
{
    // blocks (offset 0) and rev (offset 6) are ordinary conditions; the magic sits between them.
    private const string Fixed = "struct sb { uint32 blocks @expect(blocks > 0, \"blocks\"); uint16 magic @magic(magic == 0xEF53, \"magic\", \"Not an ext volume\"); uint16 rev @expect(rev < 2, \"rev\"); };";

    // The same members after a runtime-sized array, so the struct is read member by member.
    private const string Variable = "struct vb { uint8 n; uint8 pad[n]; uint32 blocks @expect(blocks > 0, \"blocks\"); uint16 magic @magic(magic == 0xEF53, \"magic\"); uint16 rev @expect(rev < 2, \"rev\"); };";

    private static byte[] Fixed16(uint blocks, ushort magic, ushort rev) => [.. BitConverter.GetBytes(blocks), .. BitConverter.GetBytes(magic), .. BitConverter.GetBytes(rev)];

    /// <summary>The definition prints the suffix, the constraint is unsupported-kind, and the label and message are kept.</summary>
    [TestMethod]
    public void Magic_RoundTripsThroughTheDefinition_AsAnUnsupportedConstraint()
    {
        var layout = new CStruct(Fixed);
        StringAssert.Contains(layout.ToDefinition(), "@magic(magic == 0xEF53, \"magic\", \"Not an ext volume\")");
        var failure = Assert.Throws<CStructConstraintException>(() => layout.Parse(Fixed16(1, 0x1234, 0), "sb"));
        Assert.AreEqual(ConstraintKind.Unsupported, failure.Kind);
        Assert.AreEqual("magic", failure.Label);
        Assert.AreEqual("Not an ext volume", failure.Description);
    }

    /// <summary>A non-magic failure before the magic does not outrank it, in a fixed struct and in a member-by-member one.</summary>
    [TestMethod]
    public void Magic_IsReportedBeforeAnEarlierMalformedField()
    {
        var fixedFailure = Assert.Throws<CStructConstraintException>(() => new CStruct(Fixed).Parse(Fixed16(0, 0x1234, 0), "sb"));
        Assert.AreEqual(ConstraintKind.Unsupported, fixedFailure.Kind);
        Assert.AreEqual("magic", fixedFailure.Label);

        byte[] variable = [2, 0, 0, 0, 0, 0, 0, 0x34, 0x12, 0, 0];
        var variableFailure = Assert.Throws<CStructConstraintException>(() => new CStruct(Variable).Parse(variable, "vb"));
        Assert.AreEqual(ConstraintKind.Unsupported, variableFailure.Kind);
        Assert.AreEqual("magic", variableFailure.Label);
    }

    /// <summary>When the magic holds, the held-back conditions run, then the later ones, in field order.</summary>
    [TestMethod]
    public void WhenTheMagicHolds_TheOtherConditionsRunInFieldOrder()
    {
        var layout = new CStruct(Fixed);
        Assert.AreEqual("blocks", Assert.Throws<CStructConstraintException>(() => layout.Parse(Fixed16(0, 0xEF53, 5), "sb")).Label);
        Assert.AreEqual("rev", Assert.Throws<CStructConstraintException>(() => layout.Parse(Fixed16(1, 0xEF53, 5), "sb")).Label);
        StructValue ok = layout.Parse(Fixed16(1, 0xEF53, 1), "sb");
        Assert.AreEqual(0xEF53, Convert.ToInt32(ok["magic"]));
    }

    /// <summary>A read that collects violations records the magic first, then the held-back condition, then the later one.</summary>
    [TestMethod]
    public void CollectedViolations_ListTheMagicFirst()
    {
        var found = new List<CStructConstraintException>();
        new CStruct(Fixed).Parse(Fixed16(0, 0x1234, 5), "sb", null, new ReadOptions { ConstraintViolations = found });
        CollectionAssert.AreEqual(new[] { "magic", "blocks", "rev" }, found.Select(violation => violation.Label).ToArray());
        Assert.AreEqual(ConstraintKind.Unsupported, found[0].Kind);
        Assert.AreEqual(ConstraintKind.Malformed, found[1].Kind);
    }

    /// <summary>Several magic conditions on one struct are all required, and the skipping policy evaluates none of them.</summary>
    [TestMethod]
    public void SeveralMagics_AreAllRequired_AndSkipEvaluatesNone()
    {
        var layout = new CStruct("struct h { uint8 a @magic(a == 1, \"a\"); uint8 b @magic(b == 2, \"b\"); uint8 c @expect(c == 3, \"c\"); };");
        Assert.AreEqual("a", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 9, 9, 9 }, "h")).Label);
        Assert.AreEqual("b", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 9, 9 }, "h")).Label);
        Assert.AreEqual("c", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 2, 9 }, "h")).Label);
        layout.Parse(new byte[] { 9, 9, 9 }, "h", null, new ReadOptions { Constraints = ConstraintPolicy.Skip });
    }

    /// <summary>Magic of a nested struct is checked when that struct is read, and orders only that struct's own conditions.</summary>
    [TestMethod]
    public void NestedStruct_OrdersItsOwnConditions()
    {
        var layout = new CStruct("struct inner { uint8 x @expect(x == 7, \"x\"); uint8 m @magic(m == 1, \"m\"); }; struct outer { uint8 first @expect(first == 5, \"first\"); inner i; };");
        Assert.AreEqual("first", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 0, 0, 0 }, "outer")).Label);
        Assert.AreEqual("m", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 5, 0, 0 }, "outer")).Label);
        Assert.AreEqual("x", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 5, 0, 1 }, "outer")).Label);
    }

    /// <summary>A magic on a member inside an <c>if</c> arm keeps the declaration order of its struct's conditions.</summary>
    [TestMethod]
    public void ConditionalMagic_KeepsDeclarationOrder()
    {
        var layout = new CStruct("struct h { uint8 kind; uint8 early @expect(early == 1, \"early\"); if (kind == 1) { uint8 m @magic(m == 2, \"m\"); } };");
        Assert.AreEqual("early", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 0, 0 }, "h")).Label);
        Assert.AreEqual("m", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 1, 0 }, "h")).Label);
    }

    /// <summary>The same restrictions as the other constraints: a union member, an unnamed member and a malformed suffix are layout errors.</summary>
    [TestMethod]
    public void Magic_FollowsTheRulesOfOtherConstraints()
    {
        StringAssert.Contains(Assert.Throws<CStructLayoutException>(() => new CStruct("union u { uint8 a @magic(a == 1); };")).Message, "@magic");
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @magic(a == 1, \"\"); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @magic; };"));
    }
}
