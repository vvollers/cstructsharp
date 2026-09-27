namespace CStructSharp.Tests;

using System;
using System.IO;
using CStructSharp.Diagnostics;

/// <summary>
///     Verifies the explicit per-field byte-offset assertion (<c>@N</c>): a constant counted from the start of the
///     field's own struct, which validates the computed placement rather than ever repositioning it. It is checked at
///     construction when the offset is known then, and otherwise by every operation that places the field.
/// </summary>
[TestClass]
public class OffsetAssertionTests
{
    /// <summary>An assertion matching the field's naturally computed offset compiles and reads normally.</summary>
    [TestMethod]
    public void MatchingAssertion_Succeeds()
    {
        var cstruct = new CStruct("struct root { uint8 a; uint8 b; uint8 value @2; };");

        dynamic parsed = cstruct.Parse(new MemoryStream([1, 2, 3,]), "root");
        Assert.AreEqual((byte)3, (byte)parsed.value);
    }

    /// <summary>A mismatched assertion is rejected at construction, naming both the asserted and actual offset.</summary>
    [TestMethod]
    public void Mismatch_IsRejectedNamingBothValues()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 a; uint8 value @5; };"));

        StringAssert.Contains(exception.Message, "5");
        StringAssert.Contains(exception.Message, "1");
    }

    /// <summary>
    ///     The check compares against the actual mode-dependent computed offset, not a hardcoded expectation: the
    ///     same assertion is correct in one placement mode and wrong in the other.
    /// </summary>
    [TestMethod]
    public void ModeSensitivity_ChecksTheActualComputedOffsetForEachMode()
    {
        var aligned = new CStruct("struct root { uint8 a; uint32 b @4; };", aligned: true);
        Assert.AreEqual(8, aligned.GetStructSizeInBytes("root"));

        var packed = new CStruct("struct root { uint8 a; uint32 b @1; };", aligned: false);
        Assert.AreEqual(5, packed.GetStructSizeInBytes("root"));

        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 a; uint32 b @4; };", aligned: false));
    }

    /// <summary>
    ///     When a field follows a runtime-length sibling, its static offset is unknowable at construction time, so
    ///     construction always succeeds regardless of whether the assertion is right - but the gap is closed, not
    ///     left silently unchecked: the first operation that actually reaches the field (here, Parse) performs
    ///     the check instead, at the point the field's real position finally becomes known.
    /// </summary>
    [TestMethod]
    public void RuntimeDependentField_ConstructionSucceedsButTheFirstOperationChecksTheAssertion()
    {
        var wrong = new CStruct("struct root { uint8 count; uint8 items[count]; uint8 tail @999; };");
        var right = new CStruct("struct root { uint8 count; uint8 items[count]; uint8 tail @3; };");

        Assert.Throws<CStructLayoutException>(() => wrong.Parse(new MemoryStream([2, 10, 20, 42,]), "root"));

        dynamic parsed = right.Parse(new MemoryStream([2, 10, 20, 42,]), "root");
        Assert.AreEqual((byte)42, (byte)parsed.tail);
    }

    /// <summary>The runtime check fires via every operation, not just whole-struct reads - including a single-path
    /// resolution through ReadValue, which resolves its target via the address resolver before reading it.</summary>
    [TestMethod]
    public void RuntimeDependentField_CheckedViaSinglePathReadValueToo()
    {
        var cstruct = new CStruct("struct root { uint8 count; uint8 items[count]; uint8 tail @999; };");
        using var stream = new MemoryStream([2, 10, 20, 42,]);

        Assert.Throws<CStructLayoutException>(() => cstruct.ReadValue(stream, "root.tail"));
    }

    /// <summary>The runtime check also fires for a write operation reaching the field, not only reads.</summary>
    [TestMethod]
    public void RuntimeDependentField_CheckedOnSerializeToo()
    {
        var cstruct = new CStruct("struct root { uint8 count; uint8 items[count]; uint8 tail @999; };");

        Assert.Throws<CStructLayoutException>(
            () => cstruct.Serialize(
                "root",
                new Dictionary<string, object?> { ["count"] = (byte)2, ["items"] = new byte[] { 10, 20, }, ["tail"] = (byte)42, }));
    }

    /// <summary>A negative offset is rejected regardless of what the field's actual placement would be.</summary>
    [TestMethod]
    public void NegativeOffset_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 value @-1; };"));
    }

    /// <summary>
    ///     A negative constant is rejected when the layout is built, even for a field whose offset depends on the data,
    ///     naming the field and the value.
    /// </summary>
    [TestMethod]
    public void NegativeOffset_AfterARuntimeSizedField_IsRejectedAtConstruction()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("#define BACK 1\nstruct root { uint8 count; uint8 items[count]; uint8 tail @(1 - BACK - 1); };"));

        StringAssert.Contains(exception.Message, "Explicit offset assertion must be non-negative: tail = -1");
    }

    /// <summary>
    ///     N is a constant, like <c>@align(N)</c>: naming a field is a construction error, including after a
    ///     runtime-sized field, where the value could otherwise have come from the data.
    /// </summary>
    [TestMethod]
    public void AssertionNamingAField_IsRejectedAtConstruction()
    {
        foreach (string definition in new[]
                 {
                     "struct root { uint8 n; uint8 tail @n; };",
                     "struct root { uint8 n; uint8 items[n]; uint8 tail @(n + 1); };",
                 })
        {
            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(definition), definition);
            StringAssert.StartsWith(exception.Message, "Cannot evaluate offset assertion for tail:", definition);
        }
    }

    /// <summary>
    ///     A caller's variables do not feed the assertion either: an undefined name fails construction, not the first
    ///     operation that supplies it.
    /// </summary>
    [TestMethod]
    public void AssertionNamingAnOperationVariable_IsRejectedAtConstruction()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 values[count]; uint8 tail @(expected); };"));

        StringAssert.StartsWith(exception.Message, "Cannot evaluate offset assertion for tail:");
        StringAssert.Contains(exception.Message, "expected");
    }

    /// <summary>
    ///     The offset counts from the start of the field's own struct, as C's <c>offsetof</c> does, also when the check
    ///     runs during an operation: here <c>inner</c> starts at byte 1 and <c>x</c> sits at byte 2 of it (byte 3 of the
    ///     input). Reads, path resolution and writes agree.
    /// </summary>
    [TestMethod]
    public void RuntimeCheck_CountsFromTheFieldsOwnStruct()
    {
        const string Inner = "struct inner { uint8 n; uint8 d[n]; uint8 x @2; }; struct root { uint8 pad; inner i; };";
        byte[] bytes = [0, 1, 2, 3,];
        var layout = new CStruct(Inner);

        dynamic parsed = layout.Parse(bytes, "root");
        Assert.AreEqual((byte)3, (byte)parsed.i.x);
        Assert.AreEqual(3L, layout.ResolveAddress(bytes, "root.i.x"));
        var value = new Dictionary<string, object?>
        {
            ["pad"] = (byte)0,
            ["i"] = new Dictionary<string, object?> { ["n"] = (byte)1, ["d"] = new byte[] { 2, }, ["x"] = (byte)3, },
        };
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", value));

        // Counted from the input's start, the wrong value 3 would have passed.
        var wrong = new CStruct(Inner.Replace("@2", "@3", StringComparison.Ordinal));
        StringAssert.StartsWith(
            Assert.Throws<CStructLayoutException>(() => wrong.Parse(bytes, "root")).Message,
            "Field 'x' asserts offset 3 but computed offset is 2");
        Assert.Throws<CStructLayoutException>(() => wrong.ResolveAddress(bytes, "root.i.x"));
        Assert.Throws<CStructLayoutException>(() => wrong.Serialize("root", value));
    }

    /// <summary>
    ///     An offset assertion on a bitfield declarator is rejected outright at construction, rather than being
    ///     silently accepted-but-unchecked or applying an under-specified partial rule.
    /// </summary>
    [TestMethod]
    public void BitfieldCombination_IsRejected()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 flag : 1 @2; };"));

        StringAssert.Contains(exception.Message, "flag");
    }

    /// <summary>The assertion argument is a full expression, so a #define'd constant works, not only a literal.</summary>
    [TestMethod]
    public void DefineDrivenAssertion_Works()
    {
        var cstruct = new CStruct("#define OFF 2\nstruct root { uint8 a; uint8 b; uint8 value @OFF; };");

        Assert.AreEqual(3, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>A malformed double suffix on one declarator is rejected rather than silently accepted.</summary>
    [TestMethod]
    public void AlignAndOffsetTogether_OnOneDeclaratorIsRejected()
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 value @align(4)@2; };"));
    }

    /// <summary>
    ///     A purely validating feature must not interfere with normal operation - the full read/write/update
    ///     surface behaves identically to the unannotated field.
    /// </summary>
    [TestMethod]
    public void FullRoundTrip_WriteSerializeReadValueAndUpdate()
    {
        var cstruct = new CStruct("struct root { uint8 a; uint8 b; uint8 value @2; };");

        using var writeStream = new MemoryStream();
        cstruct.Write(writeStream, "root", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = (byte)2, ["value"] = (byte)3, });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, writeStream.ToArray());

        byte[] bytes = cstruct.Serialize("root", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = (byte)2, ["value"] = (byte)3, });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, bytes);

        using var readStream = new MemoryStream(bytes);
        Assert.AreEqual(3, Convert.ToInt32(cstruct.ReadValue(readStream, "root.value")));

        using var updateStream = new MemoryStream(bytes);
        cstruct.Update(updateStream, "root.value", (byte)9);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 9, }, updateStream.ToArray());
    }

    /// <summary>An overflowing asserted offset identifies its field rather than reporting an anonymous expression.</summary>
    [TestMethod]
    public void OverflowingOffsetAssertion_NamesItsField()
    {
        const string definition = "struct root { uint8 first; uint8 next @ (2147483647+1); };";

        // Static placement rejects the offset assertion before a binary source is required.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, "Cannot evaluate offset assertion for next:");
        StringAssert.Contains(failure.Message, "outside the 32-bit range");
    }
}
