namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>Pins the parsed-struct value contract that replaced <see cref="ExpandoObject"/>.</summary>
[TestClass]
public class StructValueTests
{
    private const string Layout = """
        struct inner { uint8 a; uint8 b; };
        struct root { uint16 kind; inner nested; uint8 tail[2]; };
        """;

    /// <summary>A parse result is a <see cref="StructValue"/> readable dynamically, by key, and by enumeration in declaration order.</summary>
    [TestMethod]
    public void Parse_ReturnsStructValue_WithDeclarationOrderMembers()
    {
        var cstruct = new CStruct(Layout);
        dynamic parsed = cstruct.Parse([0x03, 0x00, 0x11, 0x22, 0x07, 0x08], "root");

        Assert.IsInstanceOfType<StructValue>(parsed);
        Assert.AreEqual((ushort)3, parsed.kind);
        Assert.AreEqual((byte)0x22, parsed.nested.b);

        var values = (IReadOnlyDictionary<string, object?>)parsed;
        CollectionAssert.AreEqual(new[] { "kind", "nested", "tail" }, values.Keys.ToArray());
        Assert.AreEqual(3, values.Count);
        Assert.IsTrue(values.ContainsKey("nested"));
        Assert.IsFalse(values.ContainsKey("missing"));
        Assert.ThrowsExactly<KeyNotFoundException>(() => _ = values["missing"]);
    }

    /// <summary>Members can be edited, added, and removed after parsing, and the edited value writes back.</summary>
    [TestMethod]
    public void StructValue_IsMutable_AndWritesBack()
    {
        var cstruct = new CStruct(Layout);
        dynamic parsed = cstruct.Parse([0x03, 0x00, 0x11, 0x22, 0x07, 0x08], "root");
        parsed.kind = (ushort)9;
        parsed.nested.a = (byte)0xAA;

        byte[] written = cstruct.Serialize("root", (object)parsed);
        CollectionAssert.AreEqual(new byte[] { 0x09, 0x00, 0xAA, 0x22, 0x07, 0x08 }, written);

        IDictionary<string, object?> values = parsed;
        values["extra"] = 1;
        Assert.AreEqual(4, values.Count);
        CollectionAssert.AreEqual(new[] { "kind", "nested", "tail", "extra" }, values.Keys.ToArray());
        Assert.IsTrue(values.Remove("nested"));
        Assert.IsFalse(values.Remove("nested"));
        CollectionAssert.AreEqual(new[] { "kind", "tail", "extra" }, values.Keys.ToArray());
        Assert.ThrowsExactly<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(() => _ = parsed.nested);

        values.Clear();
        Assert.AreEqual(0, values.Count);
        Assert.IsFalse(values.Any());
    }

    /// <summary>Insertion order is preserved even when members are set out of shape order or added ad hoc.</summary>
    [TestMethod]
    public void StructValue_PreservesInsertionOrder_OutOfShape()
    {
        var cstruct = new CStruct(Layout);
        IDictionary<string, object?> parsed = cstruct.Parse([0x03, 0x00, 0x11, 0x22, 0x07, 0x08], "root");
        parsed.Remove("kind");
        parsed["late"] = 1;
        parsed["kind"] = (ushort)5;
        CollectionAssert.AreEqual(new[] { "nested", "tail", "late", "kind" }, parsed.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "nested", "tail", "late", "kind" }, ((IDynamicMetaObjectProvider)parsed).GetMetaObject(Expression.Parameter(typeof(object))).GetDynamicMemberNames().ToArray());

        var empty = new StructValue();
        empty.Add("z", 1);
        empty.Add("a", 2);
        Assert.ThrowsExactly<System.ArgumentException>(() => empty.Add("a", 3));
        CollectionAssert.AreEqual(new[] { "z", "a" }, empty.Keys.ToArray());
    }

    /// <summary>Unselected conditional members are absent (not null), and promoted anonymous members are spliced in.</summary>
    [TestMethod]
    public void StructValue_OmitsUnselectedConditionalMembers_AndSplicesPromotedMembers()
    {
        var cstruct = new CStruct("""
            struct root {
                uint8 flag;
                if (flag == 1) { uint8 yes; } else { uint8 no; }
                struct { uint8 p; uint8 q; };
            };
            """);
        IDictionary<string, object?> chosenNo = cstruct.Parse([0x00, 0x05, 0x06, 0x07], "root");
        CollectionAssert.AreEqual(new[] { "flag", "no", "p", "q" }, chosenNo.Keys.ToArray());
        Assert.IsFalse(chosenNo.ContainsKey("yes"));

        IDictionary<string, object?> chosenYes = cstruct.Parse([0x01, 0x05, 0x06, 0x07], "root");
        CollectionAssert.AreEqual(new[] { "flag", "yes", "p", "q" }, chosenYes.Keys.ToArray());
    }

    /// <summary>One dynamic call site serves values of different shapes, absent members throw, and dynamic writes land in the slot.</summary>
    [TestMethod]
    public void DynamicCallSite_HandlesShapesAbsentMembersAndWrites()
    {
        var first = new CStruct(Layout);
        var second = new CStruct("struct root { uint8 pad; uint16 kind; };");
        dynamic a = first.Parse([0x03, 0x00, 0x11, 0x22, 0x07, 0x08], "root");
        dynamic b = second.Parse([0xFF, 0x05, 0x00], "root");
        dynamic c = second.Parse([0xFF, 0x06, 0x00], "root");

        static ushort ReadKind(dynamic value) => (ushort)value.kind;
        Assert.AreEqual((ushort)3, ReadKind(a));
        Assert.AreEqual((ushort)5, ReadKind(b));
        Assert.AreEqual((ushort)6, ReadKind(c));
        Assert.AreEqual((ushort)3, ReadKind(a));

        static void WriteKind(dynamic value, ushort kind) => value.kind = kind;
        WriteKind(a, 30);
        WriteKind(b, 50);
        Assert.AreEqual((ushort)30, ReadKind(a));
        Assert.AreEqual((ushort)50, ReadKind(b));
        Assert.AreEqual((ushort)6, ReadKind(c));

        ((IDictionary<string, object?>)a).Remove("kind");
        Assert.ThrowsExactly<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(() => ReadKind(a));
        WriteKind(a, 31);
        Assert.AreEqual((ushort)31, ReadKind(a));
        CollectionAssert.AreEqual(new[] { "nested", "tail", "kind" }, ((IDictionary<string, object?>)a).Keys.ToArray());

        a.custom = "x";
        Assert.AreEqual("x", (string)a.custom);
        Assert.AreEqual(4, ((IDictionary<string, object?>)a).Count);
        Assert.ThrowsExactly<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(() => _ = b.custom);
    }
}
