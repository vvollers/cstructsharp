namespace CStructSharp.Tests;

using System;
using System.IO;

/// <summary>
///     Verifies the explicit per-field alignment override (LANG-15 field-level slice, <c>@align(N)</c>): it
///     replaces a field's natural alignment wherever that value is consulted, has no effect in packed mode (natural
///     alignment already has none there), and is validated as a positive power of two.
/// </summary>
[TestClass]
public class AlignmentOverrideTests
{
    /// <summary>An override pads the field to the requested boundary and lengthens the struct's own tail padding.</summary>
    [TestMethod]
    public void BasicOverride_PadsToTheOverriddenAlignment()
    {
        var cstruct = new CStruct("struct root { uint8 a; uint8 value @align(4); };", aligned: true);

        Assert.AreEqual(8, cstruct.GetStructSizeInBytes("root"));
        Assert.AreEqual(4, cstruct.GetStructAlignmentInBytes("root"));

        byte[] bytes = cstruct.Serialize("root", new { a = (byte)1, value = (byte)2, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, }, bytes);
    }

    /// <summary>
    ///     In packed mode the override has no effect - consistent with a field's own natural alignment already
    ///     having none there, since every AlignUp call in the codebase is gated behind the global aligned flag.
    /// </summary>
    [TestMethod]
    public void PackedMode_HasNoEffect()
    {
        var cstruct = new CStruct("struct root { uint8 a; uint8 value @align(4); };", aligned: false);

        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("root"));

        byte[] bytes = cstruct.Serialize("root", new { a = (byte)1, value = (byte)2, });
        CollectionAssert.AreEqual(new byte[] { 1, 2, }, bytes);
    }

    /// <summary>The alignment argument is a full expression, so a #define'd constant works, not only a literal.</summary>
    [TestMethod]
    public void DefineDrivenOverride_Works()
    {
        var cstruct = new CStruct(
            "#define ALIGN8 8\nstruct root { uint8 a; uint8 value @align(ALIGN8); };",
            aligned: true);

        Assert.AreEqual(16, cstruct.GetStructSizeInBytes("root"));
        Assert.AreEqual(8, cstruct.GetStructAlignmentInBytes("root"));
    }

    /// <summary>An override on one comma-separated declarator does not affect its sibling declarators.</summary>
    [TestMethod]
    public void PerDeclaratorScoping_OnlyAffectsItsOwnName()
    {
        var cstruct = new CStruct("struct root { uint8 a @align(4), b; };", aligned: true);

        // a's override raises the struct's own alignment to 4, so the tail pads to 4, but b still sits immediately
        // after a (offset 1) since only a's own placement decision - not b's - consults the override.
        dynamic parsed = cstruct.ParseStream(new MemoryStream([1, 2, 0, 0,]), "root");
        Assert.AreEqual((byte)1, (byte)parsed.a);
        Assert.AreEqual((byte)2, (byte)parsed.b);
        Assert.AreEqual(4, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>An override on a pointer field replaces its normal PointerSize-derived alignment.</summary>
    [TestMethod]
    public void PointerFieldOverride_Works()
    {
        var cstruct = new CStruct("struct root { uint8 *p @align(8); };", aligned: true, pointerSize: 2);

        Assert.AreEqual(8, cstruct.GetStructAlignmentInBytes("root"));
    }

    /// <summary>A non-power-of-two override is rejected at construction, naming the field and the rejected value.</summary>
    [TestMethod]
    public void NonPowerOfTwo_IsRejected()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 value @align(3); };"));

        StringAssert.Contains(exception.Message, "value");
        StringAssert.Contains(exception.Message, "3");
    }

    /// <summary>A zero override is rejected the same way as any other non-power-of-two value.</summary>
    [TestMethod]
    public void Zero_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 value @align(0); };"));
    }

    /// <summary>
    ///     The override applies identically across every placement-consuming operation, since every one of them
    ///     reads the same compiled alignment value - this is the test that actually proves the "one shared value"
    ///     consolidation argument, not just parse-time acceptance.
    /// </summary>
    [TestMethod]
    public void FullRoundTrip_WriteSerializeReadValueAndUpdate()
    {
        var cstruct = new CStruct("struct root { uint8 a; uint8 value @align(4); };", aligned: true);

        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "root", new { a = (byte)1, value = (byte)2, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, }, writeStream.ToArray());

        byte[] bytes = cstruct.Serialize("root", new { a = (byte)1, value = (byte)2, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, }, bytes);

        using var readStream = new MemoryStream(bytes);
        Assert.AreEqual(2, Convert.ToInt32(cstruct.ReadValue(readStream, "root.value")));

        using var updateStream = new MemoryStream(bytes);
        cstruct.UpdateStream(updateStream, "root.value", (byte)9);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 9, 0, 0, 0, }, updateStream.ToArray());
    }
}
