namespace CStructSharp.Tests;

using System;
using System.IO;

/// <summary>
///     Verifies the explicit per-struct/union alignment override (LANG-15, <c>struct/union @align(N)</c>): it
///     clamps every one of that composite's own fields' alignment to at most N (matching <c>#pragma pack(N)</c>
///     semantics, not <c>alignas</c>, which can also increase alignment), a field's own explicit override always
///     wins outright, and it has no effect in packed mode - the same scope decision the field-level override
///     already established.
/// </summary>
[TestClass]
public class CompositeAlignmentOverrideTests
{
    /// <summary>An override of 1 clamps every field to alignment 1, acting exactly as "packed for one composite".</summary>
    [TestMethod]
    public void FullClamp_ActsAsPackedForOneComposite()
    {
        var cstruct = new CStruct("struct root @align(1) { uint8 a; uint32 b; };", aligned: true);

        Assert.AreEqual(5, cstruct.GetStructSizeInBytes("root"));
        Assert.AreEqual(1, cstruct.GetStructAlignmentInBytes("root"));

        dynamic parsed = cstruct.ParseStream(new MemoryStream([1, 2, 3, 4, 5,]), "root");
        Assert.AreEqual((byte)1, (byte)parsed.a);
        Assert.AreEqual(0x05040302u, (uint)parsed.b);
    }

    /// <summary>A partial clamp (N above 1 but below a field's natural alignment) reduces padding without eliminating it.</summary>
    [TestMethod]
    public void PartialClamp_ReducesButDoesNotEliminatePadding()
    {
        var cstruct = new CStruct("struct root @align(2) { uint8 a; uint32 b; };", aligned: true);

        Assert.AreEqual(6, cstruct.GetStructSizeInBytes("root"));
        Assert.AreEqual(2, cstruct.GetStructAlignmentInBytes("root"));
    }

    /// <summary>A field's own explicit override always wins outright, unaffected by its composite's own clamp.</summary>
    [TestMethod]
    public void FieldLevelOverride_WinsOverTheCompositeClamp()
    {
        var cstruct = new CStruct("struct root @align(1) { uint8 a; uint32 b @align(4); };", aligned: true);

        // Without the field's own override, @align(1) would clamp b to offset 1; its own @align(4) wins instead.
        Assert.AreEqual(8, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>In packed mode the override has no effect, matching the field-level override's own scope decision.</summary>
    [TestMethod]
    public void PackedMode_HasNoEffect()
    {
        var cstruct = new CStruct("struct root @align(4) { uint8 a; uint32 b; };", aligned: false);

        Assert.AreEqual(5, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>A composite's own clamped alignment propagates to a containing composite's own field placement.</summary>
    [TestMethod]
    public void PropagatesToAContainingComposite()
    {
        var cstruct = new CStruct(
            "struct inner @align(1) { uint8 a; uint32 b; }; struct outer { uint8 x; inner y; };",
            aligned: true);

        Assert.AreEqual(1, cstruct.GetStructAlignmentInBytes("inner"));

        // y (inner, now alignment 1) needs no padding after x, unlike inner's natural alignment of 4.
        Assert.AreEqual(6, cstruct.GetStructSizeInBytes("outer"));
    }

    /// <summary>The override parses and applies identically on a union declaration.</summary>
    [TestMethod]
    public void AppliesToUnions()
    {
        var cstruct = new CStruct("union choice @align(1) { uint8 small; uint32 large; };", aligned: true);

        Assert.AreEqual(1, cstruct.GetStructAlignmentInBytes("choice"));
    }

    /// <summary>The override parses and applies on an inline/anonymous struct field.</summary>
    [TestMethod]
    public void AppliesToInlineStructFields()
    {
        var cstruct = new CStruct(
            "struct root { uint8 a; struct @align(1) { uint8 c; uint32 d; } inner; };",
            aligned: true);

        Assert.AreEqual(6, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>The override parses and applies on the anonymous typedef struct/union forms.</summary>
    [TestMethod]
    public void AppliesToAnonymousTypedefForms()
    {
        var structCstruct = new CStruct(
            "typedef struct @align(1) { uint8 a; uint32 b; } Point; struct root { Point value; };",
            aligned: true);
        Assert.AreEqual(5, structCstruct.GetStructSizeInBytes("root"));

        var unionCstruct = new CStruct(
            "typedef union @align(1) { uint8 a; uint32 b; } Choice; struct root { Choice value; };",
            aligned: true);
        Assert.AreEqual(4, unionCstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>A non-power-of-two composite-level override is rejected at construction, naming the composite.</summary>
    [TestMethod]
    public void NonPowerOfTwo_IsRejected()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root @align(3) { uint8 a; };"));

        StringAssert.Contains(exception.Message, "root");
        StringAssert.Contains(exception.Message, "3");
    }

    /// <summary>The full read/write/update surface behaves consistently with the clamped placement.</summary>
    [TestMethod]
    public void FullRoundTrip_WriteSerializeReadValueAndUpdate()
    {
        var cstruct = new CStruct("struct root @align(1) { uint8 a; uint32 b; };", aligned: true);

        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "root", new { a = (byte)1, b = 0x04030201u, });
        CollectionAssert.AreEqual(new byte[] { 1, 1, 2, 3, 4, }, writeStream.ToArray());

        byte[] bytes = cstruct.Serialize("root", new { a = (byte)1, b = 0x04030201u, });
        CollectionAssert.AreEqual(new byte[] { 1, 1, 2, 3, 4, }, bytes);

        using var readStream = new MemoryStream(bytes);
        Assert.AreEqual(0x04030201u, Convert.ToUInt32(cstruct.ReadValue(readStream, "root.b")));

        using var updateStream = new MemoryStream(bytes);
        cstruct.UpdateStream(updateStream, "root.b", 0xAABBCCDDu);
        CollectionAssert.AreEqual(new byte[] { 1, 0xDD, 0xCC, 0xBB, 0xAA, }, updateStream.ToArray());
    }
}
