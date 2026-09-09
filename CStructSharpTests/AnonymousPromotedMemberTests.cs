namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;

/// <summary>
///     Verifies anonymous promoted struct members (LANG-14, <c>struct { ... };</c> with no trailing name): its own
///     fields are spliced directly into the containing struct's addressable path/POCO/JSON namespace instead of
///     nesting under a name of their own, following <c>docs/adr/0014-anonymous-promoted-struct-union-members.md</c>.
/// </summary>
[TestClass]
public class AnonymousPromotedMemberTests
{
    /// <summary>A promoted member's presence never changes the containing struct's own size or alignment.</summary>
    [TestMethod]
    public void SizeAndAlignment_MatchTheEquivalentNamedInlineStruct()
    {
        var anon = new CStruct("struct root { uint8 a; struct { uint8 x; uint8 y; }; uint8 b; };", pointerSize: 1);
        var named = new CStruct(
            "struct root { uint8 a; struct { uint8 x; uint8 y; } inner; uint8 b; };",
            pointerSize: 1);

        Assert.AreEqual(named.GetStructSizeInBytes("root"), anon.GetStructSizeInBytes("root"));
        Assert.AreEqual(named.GetStructAlignmentInBytes("root"), anon.GetStructAlignmentInBytes("root"));

        var anonAligned = new CStruct(
            "struct root { uint8 a; struct { uint8 x; uint32 y; }; uint8 b; };",
            pointerSize: 1,
            aligned: true);
        var namedAligned = new CStruct(
            "struct root { uint8 a; struct { uint8 x; uint32 y; } inner; uint8 b; };",
            pointerSize: 1,
            aligned: true);
        Assert.AreEqual(namedAligned.GetStructSizeInBytes("root"), anonAligned.GetStructSizeInBytes("root"));
        Assert.AreEqual(namedAligned.GetStructAlignmentInBytes("root"), anonAligned.GetStructAlignmentInBytes("root"));
    }

    /// <summary>A named inline struct member is completely unaffected by the optional-name grammar change.</summary>
    [TestMethod]
    public void NamedInlineStruct_StillWorks()
    {
        var cstruct = new CStruct("struct root { struct { uint8 x; } inner; };", pointerSize: 1);
        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>Transitive promotion (an anonymous member inside an anonymous member) parses and sizes correctly.</summary>
    [TestMethod]
    public void TransitivePromotion_ParsesAndSizesCorrectly()
    {
        var cstruct = new CStruct("struct root { struct { struct { uint8 x; }; }; };", pointerSize: 1);
        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("root"));
    }
}
