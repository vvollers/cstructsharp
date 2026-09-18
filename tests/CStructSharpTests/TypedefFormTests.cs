namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The typedef spellings real headers use: declarator lists with pointer aliases, a tag with no alias, a tag
///     reused as a type, an alias of an existing tag, and fixed-array typedefs.
/// </summary>
[TestClass]
public class TypedefFormTests
{
    /// <summary><c>typedef struct _X { } X, *PX;</c> declares the tag, the alias, and a pointer alias.</summary>
    [TestMethod]
    public void DeclaratorList_DeclaresEveryAlias()
    {
        var layout = new CStruct("typedef struct _X { uint8 a; uint16 b; } X, *PX, **PPX; struct root { X x; PX p; _X y; PPX pp; };", pointerSize: 4);
        Assert.AreEqual(3 + 4 + 3 + 4, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(3, layout.GetStructSizeInBytes("_X"));
        Assert.AreEqual(3, layout.GetStructSizeInBytes("X"));

        byte[] bytes = [1, 2, 0, 14, 0, 0, 0, 3, 4, 0, 0, 0, 0, 0, 9, 5, 0,];
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)1, (byte)value.x.a);
        Assert.AreEqual((byte)3, (byte)value.y.a);
        Pointer pointer = value.p;
        Assert.AreEqual(14, pointer.Address);
        Assert.AreEqual((byte)9, (byte)((dynamic)pointer.Value!).a);
    }

    /// <summary><c>typedef struct NAME { };</c> with no alias declares the tag, exactly like <c>struct NAME { };</c>.</summary>
    [TestMethod]
    public void TagOnlyTypedef_DeclaresTheTag()
    {
        var layout = new CStruct("typedef struct NAME { uint8 a; }; typedef union CHOICE { uint8 a; uint16 b; }; struct root { NAME n; CHOICE c; };");
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 7, 1, 2, }.AsSpan(), "NAME");
        Assert.AreEqual((byte)7, (byte)value.a);
    }

    /// <summary>The tag of <c>typedef struct _X { } X;</c> is a global type; declaring it twice is an error, as in C.</summary>
    [TestMethod]
    public void Tag_IsGlobalAndUnique()
    {
        var layout = new CStruct("typedef struct _X { uint8 a; } X; struct root { _X a; X b; struct _X c; };");
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        Assert.IsTrue(layout.CStructElements.ContainsKey("_X"));

        Assert.Throws<CStructLayoutException>(
            () => new CStruct("typedef struct shared { uint8 a; } first; typedef struct shared { uint32 b; } second;"));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct tag { uint8 a; }; typedef struct tag { uint8 b; } alias;"));

        // The anonymous form's alias doubles as the tag and never collides with itself.
        Assert.AreEqual(1, new CStruct("typedef struct X { uint8 a; } X; struct root { X x; };").GetStructSizeInBytes("root"));
    }

    /// <summary><c>typedef struct tag alias;</c> aliases an already declared tag and checks the keyword against its kind.</summary>
    [TestMethod]
    public void TagAlias_ResolvesExistingTag()
    {
        var layout = new CStruct("struct tag { uint8 a; uint8 b; }; typedef struct tag alias; typedef alias other; struct root { alias v; other w; };");
        Assert.AreEqual(4, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 1, 2, 3, 4, }.AsSpan(), "root");
        Assert.AreEqual((byte)4, (byte)value.w.b);

        Assert.Throws<CStructLayoutException>(() => new CStruct("struct tag { uint8 a; }; typedef union tag alias; struct root { alias v; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("typedef struct missing alias; struct root { alias v; };"));
    }

    /// <summary><c>typedef T name[N];</c> gives every field of that type the fixed inner dimensions, and works as a root.</summary>
    [TestMethod]
    public void ArrayTypedef_AddsInnerDimensions()
    {
        var layout = new CStruct("#define N 2\ntypedef uint16 pair[N]; typedef pair grid[2]; struct root { pair p; grid g; uint8 tail; };");
        Assert.AreEqual(4 + 8 + 1, layout.GetStructSizeInBytes("root"));

        byte[] bytes = [1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 9,];
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((ushort)2, (ushort)value.p[1]);
        Assert.AreEqual((ushort)6, (ushort)value.g[1][1]);
        Assert.AreEqual((byte)9, (byte)value.tail);

        dynamic root = layout.Parse(bytes.AsSpan(), "pair");
        Assert.AreEqual((ushort)1, (ushort)root[0]);

        Assert.Throws<CStructLayoutException>(() => new CStruct("typedef uint16 pair[2]; struct root { pair *p; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("typedef uint16 open[]; struct root { open p; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("typedef uint16 pair[-1]; struct root { pair p; };"));
    }

    /// <summary>An array typedef field takes part in every operation: debug ranges, addresses, serialize, write, update, and typed reads.</summary>
    [TestMethod]
    public void ArrayTypedef_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct("typedef uint16 pair[2]; struct root { uint8 head; pair p; uint8 tail; };");
        byte[] bytes = [7, 1, 0, 2, 0, 9,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        stream.Position = 0;
        dynamic parsed = layout.ParseStream(stream, "root");
        stream.Position = 0;
        Assert.IsTrue(debug.Any(item => item.Path == "root.p" && item.Start == 3 && item.End == 5));
        Assert.AreEqual(1, layout.ResolveAddress(stream, "root.p"));
        Assert.AreEqual(3, layout.ResolveAddress(stream, "root.p[1]"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        using var written = new MemoryStream();
        layout.WriteStream(written, "root", new Dictionary<string, object?> { ["head"] = (byte)7, ["p"] = new ushort[] { 1, 2, }, ["tail"] = (byte)9, });
        CollectionAssert.AreEqual(bytes, written.ToArray());

        layout.UpdateStream(stream, "root.p[1]", (ushort)0x1234);
        CollectionAssert.AreEqual(new byte[] { 7, 1, 0, 0x34, 0x12, 9, }, stream.ToArray());
        Assert.AreEqual((ushort)0x1234, layout.ReadValue<ushort>(stream.ToArray().AsSpan(), "root.p[1]"));
    }

    /// <summary>Multi-word plain typedefs and typedef declarator lists of primitives compile.</summary>
    [TestMethod]
    public void PlainTypedef_AcceptsMultiWordTypesAndLists()
    {
        var layout = new CStruct("typedef unsigned long long u64_t; typedef uint8 byte_t, *pbyte_t; struct root { u64_t a; byte_t b; pbyte_t p; };", pointerSize: 2);
        Assert.AreEqual(8 + 1 + 2, layout.GetStructSizeInBytes("root"));
    }
}
