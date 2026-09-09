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

    /// <summary>A promoted member's own field name colliding with the parent's own field name is rejected.</summary>
    [TestMethod]
    public void Collision_PromotedVsParent_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 x; struct { uint8 x; }; };", pointerSize: 1));
    }

    /// <summary>Two sibling promoted members declaring the same field name are rejected.</summary>
    [TestMethod]
    public void Collision_SiblingPromotedVsSiblingPromoted_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { struct { uint8 x; }; struct { uint8 x; }; };", pointerSize: 1));
    }

    /// <summary>A transitively-deep promoted member colliding with a shallower one is rejected.</summary>
    [TestMethod]
    public void Collision_TransitiveDeepVsShallow_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct(
                "struct root { struct { struct { uint8 x; }; }; struct { uint8 x; }; };",
                pointerSize: 1));
    }

    /// <summary>Two fields declared by the same promoted member colliding with each other is rejected.</summary>
    [TestMethod]
    public void Collision_WithinOnePromotedMember_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { struct { uint8 x; uint8 x; }; };", pointerSize: 1));
    }

    /// <summary>A named nested struct's own namespace stays independent - reusing a promoted name is not a collision.</summary>
    [TestMethod]
    public void NamedNestedStruct_ReusingAPromotedName_IsNotACollision()
    {
        var cstruct = new CStruct(
            "struct root { struct { uint8 x; }; struct { uint8 x; } inner; };",
            pointerSize: 1);

        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>Multiple sibling promoted members with distinct names all coexist without error.</summary>
    [TestMethod]
    public void MultipleSiblingPromotedMembers_WithDistinctNames_AllCoexist()
    {
        var cstruct = new CStruct(
            "struct root { struct { uint8 x; }; struct { uint8 y; }; };",
            pointerSize: 1);

        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("root"));
    }
}
