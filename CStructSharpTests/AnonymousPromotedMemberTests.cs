namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    /// <summary>
    ///     A promoted member's fields land directly in the parent's own result object - no intermediate key, no
    ///     empty key - and sibling ordinary fields around it still read correctly in declaration order.
    /// </summary>
    [TestMethod]
    public void ParseStream_SplicesPromotedMemberFieldsDirectlyIntoTheParentContainer()
    {
        var cstruct = new CStruct("struct root { uint8 a; struct { uint8 x; uint8 y; }; uint8 b; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, });

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((byte)1, (byte)parsed.a);
        Assert.AreEqual((byte)2, (byte)parsed.x);
        Assert.AreEqual((byte)3, (byte)parsed.y);
        Assert.AreEqual((byte)4, (byte)parsed.b);
        var dictionary = (IDictionary<string, object?>)parsed;
        Assert.AreEqual(4, dictionary.Count);
        Assert.IsFalse(dictionary.ContainsKey(string.Empty));
    }

    /// <summary>Transitive promotion also splices all the way through into the outermost container.</summary>
    [TestMethod]
    public void ParseStream_TransitivePromotion_SplicesAllTheWayThrough()
    {
        var cstruct = new CStruct("struct root { struct { struct { uint8 x; }; }; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 7, });

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((byte)7, (byte)parsed.x);
        Assert.AreEqual(1, ((IDictionary<string, object?>)parsed).Count);
    }

    /// <summary>
    ///     An array-count expression can reference a promoted member's own field by its flat name, proving
    ///     expression-variable capture works through promotion.
    /// </summary>
    [TestMethod]
    public void ParseStream_ArrayCountExpression_CanReferenceAPromotedMembersField()
    {
        var cstruct = new CStruct(
            "struct root { struct { uint8 count; }; uint8 values[count]; };",
            pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 2, 10, 20, });

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((byte)2, (byte)parsed.count);
        List<object?> values = (List<object?>)parsed.values;
        Assert.AreEqual(2, values.Count);
        Assert.AreEqual((byte)10, (byte)values[0]!);
        Assert.AreEqual((byte)20, (byte)values[1]!);
    }

    /// <summary>
    ///     A descendant's debug path skips a promoted member's own (invisible) level, rendering `root.x` rather
    ///     than `root..x` - the single most important regression this feature must not introduce.
    /// </summary>
    [TestMethod]
    public void ParseStreamWithDebug_SkipsThePromotedMembersOwnLevelInTheDebugPath()
    {
        var cstruct = new CStruct("struct root { struct { uint8 x; }; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 9, });

        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");

        DebugData entry = debug.Single(item => item.DebugStackString == "root.x");
        Assert.AreEqual(0L, entry.CurPos);
        Assert.AreEqual(1L, entry.EndPos);
        Assert.IsFalse(debug.Exists(item => item.DebugStackString.Contains("..")));
    }

    /// <summary>
    ///     A promoted member's own fields are looked up directly on the same flat POCO/dynamic value the parent
    ///     struct uses, and the resulting bytes match the equivalent named-inline-struct fixture exactly.
    /// </summary>
    [TestMethod]
    public void Serialize_WritesAFlatValueThroughAPromotedMember()
    {
        var anon = new CStruct("struct root { uint8 a; struct { uint8 x; uint8 y; }; uint8 b; };", pointerSize: 1);
        var named = new CStruct(
            "struct root { uint8 a; struct { uint8 x; uint8 y; } inner; uint8 b; };",
            pointerSize: 1);

        byte[] anonBytes = anon.Serialize("root", new { a = (byte)1, x = (byte)2, y = (byte)3, b = (byte)4, });
        byte[] namedBytes = named.Serialize(
            "root",
            new { a = (byte)1, inner = new { x = (byte)2, y = (byte)3, }, b = (byte)4, });

        CollectionAssert.AreEqual(namedBytes, anonBytes);

        using var writeStream = new MemoryStream();
        anon.WriteStream(writeStream, "root", new { a = (byte)1, x = (byte)2, y = (byte)3, b = (byte)4, });
        CollectionAssert.AreEqual(namedBytes, writeStream.ToArray());
    }

    /// <summary>
    ///     A struct with both an anonymous bitfield (LANG-17) and a promoted member (LANG-14) as siblings writes
    ///     each correctly - the promoted-member check must run before the LANG-17 empty-name check, since both
    ///     test the same empty declared name.
    /// </summary>
    [TestMethod]
    public void Serialize_AnonymousBitfieldAndPromotedMemberSiblings_BothWriteCorrectly()
    {
        var cstruct = new CStruct("struct root { uint8 flag:1, :3, other:4; struct { uint8 x; }; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root", new { flag = 1, other = 0b1111, x = (byte)0xAB, });

        CollectionAssert.AreEqual(new byte[] { 0b1111_0001, 0xAB, }, bytes);
    }
}
