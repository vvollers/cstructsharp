namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     <c>@expect</c> and <c>@supports</c>: a condition written after a declarator is checked right after the field is
///     read, on every read form, and a value outside it fails with <see cref="CStructConstraintException"/> that says whether
///     the data is malformed or unsupported, names the field and quotes the condition.
/// </summary>
[TestClass]
public class FieldConstraintTests
{
    /// <summary>A value that satisfies every condition reads as usual.</summary>
    [TestMethod]
    public void SatisfiedConstraints_ReadNormally()
    {
        var layout = new CStruct("struct sb { uint16 magic @expect(magic == 0xEF53); uint8 version @supports(version <= 2); uint8 rest; };");
        StructValue value = layout.Parse(new byte[] { 0x53, 0xEF, 2, 9 }, "sb");
        Assert.AreEqual(0xEF53, Convert.ToInt32(value["magic"]));
        Assert.AreEqual(9, Convert.ToInt32(value["rest"]));
    }

    /// <summary>An <c>@expect</c> failure is malformed data and quotes the condition and the field.</summary>
    [TestMethod]
    public void Expect_Failure_IsMalformedData()
    {
        var layout = new CStruct("struct sb { uint16 magic @expect(magic == 0xEF53); uint8 rest; };");
        CStructConstraintException failure = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1, 2, 3 }, "sb"));
        Assert.AreEqual(ConstraintKind.Malformed, failure.Kind);
        Assert.AreEqual("magic == 0xEF53", failure.Constraint);
        StringAssert.Contains(failure.Message, "Invalid value for 'magic'");
        StringAssert.Contains(failure.Message, "magic == 0xEF53");
        Assert.AreEqual(CStructErrorCode.ReadFailed, failure.Code);
        Assert.IsInstanceOfType<CStructReadException>(failure);
        Assert.AreEqual("magic", failure.Member);
    }

    /// <summary>A <c>@supports</c> failure is unsupported data, which a caller can tell from malformed data.</summary>
    [TestMethod]
    public void Supports_Failure_IsUnsupportedData()
    {
        var layout = new CStruct("struct sb { uint8 version @supports(version <= 2); };");
        CStructConstraintException failure = Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 3 }, "sb"));
        Assert.AreEqual(ConstraintKind.Unsupported, failure.Kind);
        StringAssert.Contains(failure.Message, "Unsupported value for 'version'");
    }

    /// <summary>A condition may name earlier fields, defines, enum members and caller variables.</summary>
    [TestMethod]
    public void Constraint_CanNameEarlierFieldsDefinesEnumsAndVariables()
    {
        var layout = new CStruct("#define LIMIT 4\nenum kind : uint8 { Ok = 1, Bad = 2 };\nstruct s { uint8 n @expect(n <= LIMIT); uint8 m @expect(m <= n); kind k @expect(k != kind.Bad); uint8 v @expect(v == EXPECTED); };");
        var variables = new Dictionary<string, int> { ["EXPECTED"] = 7 };
        Assert.IsNotNull(layout.Parse(new byte[] { 4, 3, 1, 7 }, "s", variables));
        Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 5, 3, 1, 7 }, "s", variables));
        Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 4, 5, 1, 7 }, "s", variables));
        Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 4, 3, 2, 7 }, "s", variables));
        Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 4, 3, 1, 8 }, "s", variables));
    }

    /// <summary>A count is checked before the array it sizes is read, so a hostile count fails as a constraint, not as a short read.</summary>
    [TestMethod]
    public void CountConstraint_FailsBeforeTheArrayIsRead()
    {
        var layout = new CStruct("struct s { uint8 n @expect(n <= 2); uint8 v[n]; };");
        Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 9 }, "s"));
        StructValue ok = layout.Parse(new byte[] { 2, 5, 6 }, "s");
        Assert.AreEqual(2, ((System.Collections.IList)ok["v"]!).Count);
    }

    /// <summary>A bitfield, a nested struct in an array and a field in a selected arm each check their own condition.</summary>
    [TestMethod]
    public void Constraint_AppliesToBitfieldsArrayElementsAndSelectedArms()
    {
        var bits = new CStruct("struct b { uint8 lo:4 @expect(lo != 15); uint8 hi:4; };");
        Assert.IsNotNull(bits.Parse(new byte[] { 0x1E }, "b"));
        Assert.Throws<CStructConstraintException>(() => bits.Parse(new byte[] { 0x0F }, "b"));

        var elements = new CStruct("struct e { uint8 id @expect(id != 0); }; struct s { e items[3]; };");
        Assert.IsNotNull(elements.Parse(new byte[] { 1, 2, 3 }, "s"));
        CStructConstraintException third = Assert.Throws<CStructConstraintException>(() => elements.Parse(new byte[] { 1, 2, 0 }, "s"));
        StringAssert.Contains(third.Message, "'id'");

        var arms = new CStruct("struct s { uint8 tag; if (tag == 1) { uint8 a @expect(a > 0); } else { uint8 b; } };");
        Assert.IsNotNull(arms.Parse(new byte[] { 0, 0 }, "s"));
        Assert.Throws<CStructConstraintException>(() => arms.Parse(new byte[] { 1, 0 }, "s"));
    }

    /// <summary>Every read form checks: a span, a stream, a debug read, and the non-throwing form reports it as a failure.</summary>
    [TestMethod]
    public void EveryReadForm_ChecksTheCondition()
    {
        var layout = new CStruct("struct s { uint8 a @expect(a == 1); uint8 b; };");
        byte[] bad = [2, 0];
        Assert.Throws<CStructConstraintException>(() => layout.Parse(bad, "s"));
        using var stream = new MemoryStream(bad);
        Assert.Throws<CStructConstraintException>(() => layout.Parse(stream, "s"));
        Assert.Throws<CStructConstraintException>(() => layout.ParseWithDebug(bad, "s"));
        Assert.Throws<CStructConstraintException>(() => layout.ParseMany(bad, "s").ToList());
        Assert.IsFalse(layout.TryReadValue<StructValue>(bad, "s", out _));
    }

    /// <summary>A condition that cannot be evaluated (a division by zero) is an expression failure of the read, not a constraint failure.</summary>
    [TestMethod]
    public void UnevaluableCondition_IsAnExpressionFailure()
    {
        var layout = new CStruct("struct s { uint8 d; uint8 v @expect(100 / d > 1); };");
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 0, 1 }, "s"));
        Assert.IsNotInstanceOfType<CStructConstraintException>(failure);
        StringAssert.Contains(failure.Message, "constraint");
    }

    /// <summary>A condition describes what a read accepts: a write does not check it.</summary>
    [TestMethod]
    public void Writes_IgnoreTheCondition()
    {
        var layout = new CStruct("struct s { uint8 a @expect(a == 1); };");
        byte[] bytes = layout.Serialize("s", new Dictionary<string, object?> { ["a"] = (byte)9 });
        CollectionAssert.AreEqual(new byte[] { 9 }, bytes);
    }

    /// <summary>Several conditions on one field are checked in order and the first that fails is reported.</summary>
    [TestMethod]
    public void SeveralConditions_AreCheckedInOrder()
    {
        var layout = new CStruct("struct s { uint8 v @expect(v > 1) @supports(v < 5) @expect(v != 3); };");
        Assert.AreEqual(ConstraintKind.Malformed, Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 1 }, "s")).Kind);
        Assert.AreEqual(ConstraintKind.Unsupported, Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 9 }, "s")).Kind);
        Assert.AreEqual("v != 3", Assert.Throws<CStructConstraintException>(() => layout.Parse(new byte[] { 3 }, "s")).Constraint);
        Assert.IsNotNull(layout.Parse(new byte[] { 4 }, "s"));
    }

    /// <summary>A condition on a union member, on padding, or that names an unknown name is a layout error.</summary>
    [TestMethod]
    public void InvalidPlacements_AreLayoutErrors()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("union u { uint8 a @expect(a == 1); uint16 b; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 _ @expect(1); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a @expect(a == (); };"));
    }

    /// <summary>A layout with conditions keeps them in its text, so a rebuilt layout checks the same values.</summary>
    [TestMethod]
    public void LayoutText_KeepsTheConditions()
    {
        var layout = new CStruct("struct s { uint8 a @expect(a == 1) @supports(a < 3); };");
        StringAssert.Contains(layout.ToDefinition(), "@expect(a == 1)");
        StringAssert.Contains(layout.ToDefinition(), "@supports(a < 3)");
    }
}
