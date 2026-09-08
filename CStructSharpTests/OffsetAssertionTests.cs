namespace CStructSharp.Tests;

using System;
using System.IO;

/// <summary>
///     Verifies the explicit per-field byte-offset assertion (LANG-15 field-level slice, <c>@N</c>): it validates a
///     field's already-computed placement rather than ever repositioning it, is checked eagerly at construction
///     when the offset is statically knowable, and is scoped away from bitfields and runtime-dependent placement.
/// </summary>
[TestClass]
public class OffsetAssertionTests
{
    /// <summary>An assertion matching the field's naturally computed offset compiles and reads normally.</summary>
    [TestMethod]
    public void MatchingAssertion_Succeeds()
    {
        var cstruct = new CStruct("struct root { uint8 a; uint8 b; uint8 value @2; };");

        dynamic parsed = cstruct.ParseStream(new MemoryStream([1, 2, 3,]), "root");
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
    ///     the assertion is accepted without being checked - a documented limitation, not silent success dressed up
    ///     as validation. Normal reading still works regardless.
    /// </summary>
    [TestMethod]
    public void RuntimeDependentField_ConstructionSucceedsWithTheAssertionUnchecked()
    {
        var cstruct = new CStruct("struct root { uint8 count; uint8 items[count]; uint8 tail @999; };");

        dynamic parsed = cstruct.ParseStream(new MemoryStream([2, 10, 20, 42,]), "root");
        Assert.AreEqual((byte)42, (byte)parsed.tail);
    }

    /// <summary>A negative offset is rejected regardless of what the field's actual placement would be.</summary>
    [TestMethod]
    public void NegativeOffset_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 value @-1; };"));
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
        cstruct.WriteStream(writeStream, "root", new { a = (byte)1, b = (byte)2, value = (byte)3, });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, writeStream.ToArray());

        byte[] bytes = cstruct.Serialize("root", new { a = (byte)1, b = (byte)2, value = (byte)3, });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, bytes);

        using var readStream = new MemoryStream(bytes);
        Assert.AreEqual(3, Convert.ToInt32(cstruct.ReadValue(readStream, "root.value")));

        using var updateStream = new MemoryStream(bytes);
        cstruct.UpdateStream(updateStream, "root.value", (byte)9);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 9, }, updateStream.ToArray());
    }
}
