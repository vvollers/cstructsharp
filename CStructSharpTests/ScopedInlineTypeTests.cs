namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Verifies that anonymous inline structure declarations are identified by lexical scope, not field spelling.</summary>
[TestClass]
public class ScopedInlineTypeTests
{
    /// <summary>Reproduces the review case in which two unrelated inline fields named value collided globally.</summary>
    [TestMethod]
    public void Constructor_AllowsTheSameInlineFieldNameInUnrelatedScopes()
    {
        const string layout = """
                              struct first { struct { uint8 small; } value; };
                              struct second { struct { uint16 large; } value; };
                              """;

        var cstruct = new CStruct(layout);
        using var firstStream = new MemoryStream([0x2A,]);
        using var secondStream = new MemoryStream([0x34, 0x12,]);

        dynamic first = cstruct.ParseStream(firstStream, "first");
        dynamic second = cstruct.ParseStream(secondStream, "second");

        Assert.AreEqual((byte)0x2A, (byte)first.value.small);
        Assert.AreEqual((ushort)0x1234, (ushort)second.value.large);
        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("first"));
        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("second"));
        Struct firstDeclaration = cstruct.GetStruct("first");
        Assert.AreEqual((byte)1, firstDeclaration.GetAlignment(cstruct.FieldAlignments, cstruct.PointerSize));
        Assert.IsTrue(firstDeclaration.IsKnown(cstruct.FieldAlignments));
        CollectionAssert.AreEquivalent(
            new[] { "first", "second", },
            cstruct.CStructElements.Keys.ToArray());
        Assert.IsFalse(cstruct.FieldAlignments.ContainsKey("value"));
    }

    /// <summary>
    ///     Exercises deep repeated names, fixed arrays, alignment, byte order, selected reads, debug, address,
    ///     serialization, writing, updates, and pointer traversal against one scoped declaration graph.
    /// </summary>
    /// <param name="isLittleEndian">Whether multi-byte values use least-significant-byte-first order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RepeatedDeepInlineNames_AgreeAcrossEveryOperation(bool isLittleEndian)
    {
        const string layout = """
                              struct node { uint8 marker; };
                              struct root {
                                  uint8 prefix;
                                  struct {
                                      uint16 values[2];
                                      struct { uint32 payload; } value;
                                      node *pointer;
                                  } value;
                              };
                              struct peer { struct { uint16 payload; } value; };
                              """;
        var cstruct = new CStruct(
            layout,
            pointerSize: 1,
            aligned: true,
            isLittleEndian: isLittleEndian);
        byte[] rootBytes = new byte[16];
        rootBytes[0] = 0xEE;
        WriteUnsigned(rootBytes, 4, 2, 0x1122, isLittleEndian);
        WriteUnsigned(rootBytes, 6, 2, 0x3344, isLittleEndian);
        WriteUnsigned(rootBytes, 8, 4, 0x55667788, isLittleEndian);
        rootBytes[12] = 16;
        byte[] completeBytes = [.. rootBytes, 0xA5,];
        using var stream = new MemoryStream((byte[])completeBytes.Clone());

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((byte)0xEE, (byte)parsed.prefix);
        Assert.AreEqual((ushort)0x1122, (ushort)parsed.value.values[0]);
        Assert.AreEqual((ushort)0x3344, (ushort)parsed.value.values[1]);
        Assert.AreEqual(0x55667788U, (uint)parsed.value.value.payload);
        var pointer = (Pointer)parsed.value.pointer;
        Assert.AreEqual(16L, pointer.Address);
        Assert.AreEqual((byte)0xA5, (byte)((dynamic)pointer.Value!).marker);
        Assert.AreEqual(16L, stream.Position);
        Assert.AreEqual(16, cstruct.GetStructSizeInBytes("root"));

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");
        Assert.IsTrue(
            debug.Any(item =>
                item.DebugStackString == "root.value.value.payload" &&
                item.CurPos == 8 &&
                item.EndPos == 12));

        stream.Position = 0;
        Assert.AreEqual(6L, cstruct.ResolveAddress(stream, "root.value.values[1]"));
        Assert.AreEqual(0L, stream.Position);
        Assert.AreEqual(8L, cstruct.ResolveAddress(stream, "root.value.value.payload"));
        Assert.AreEqual(0L, stream.Position);
        Assert.AreEqual(16L, cstruct.ResolveAddress(stream, "root.value.pointer.value.marker"));
        Assert.AreEqual(0L, stream.Position);

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.value.value");
        Assert.AreEqual(0x55667788U, (uint)selected.payload);

        var data = new
        {
            prefix = (byte)0xEE,
            value = new
            {
                values = new ushort[] { 0x1122, 0x3344, },
                value = new { payload = 0x55667788U, },
                pointer = 16L,
            },
        };
        CollectionAssert.AreEqual(rootBytes, cstruct.Serialize("root", data));

        using var written = new MemoryStream();
        cstruct.WriteStream(written, "root", data);
        CollectionAssert.AreEqual(rootBytes, written.ToArray());

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.value.value.payload", 0xA1B2C3D4U);
        byte[] expected = (byte[])completeBytes.Clone();
        WriteUnsigned(expected, 8, 4, 0xA1B2C3D4, isLittleEndian);
        CollectionAssert.AreEqual(expected, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);

        cstruct.UpdateStream(stream, "root.value.pointer.value.marker", (byte)0x5A);
        expected[16] = 0x5A;
        CollectionAssert.AreEqual(expected, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Gives two typedef backing declarations independent identities even when their diagnostic tags match.</summary>
    [TestMethod]
    public void TypedefBackingNames_AreScopedToTheirAliases()
    {
        const string layout = """
                              typedef struct shared { uint8 small; } small_payload;
                              typedef struct shared { uint32 large; } large_payload;
                              struct root { small_payload first; large_payload second; };
                              """;
        var cstruct = new CStruct(layout, aligned: true);
        byte[] expected = [0x2A, 0x00, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12,];
        using var stream = new MemoryStream((byte[])expected.Clone());

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((byte)0x2A, (byte)parsed.first.small);
        Assert.AreEqual(0x12345678U, (uint)parsed.second.large);
        Assert.AreEqual(8, cstruct.GetStructSizeInBytes("root"));
        Assert.IsFalse(cstruct.CStructElements.ContainsKey("shared"));
        Assert.IsFalse(cstruct.FieldAlignments.ContainsKey("shared"));
        Assert.AreEqual((byte)1, cstruct.FieldAlignments["small_payload"]);
        Assert.AreEqual((byte)4, cstruct.FieldAlignments["large_payload"]);

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");
        Assert.IsTrue(
            debug.Any(item =>
                item.DebugStackString == "root.second.large" &&
                item.CurPos == 4 &&
                item.EndPos == 8));
        stream.Position = 0;
        Assert.AreEqual(4L, cstruct.ResolveAddress(stream, "root.second.large"));
        Assert.AreEqual(0L, stream.Position);
        dynamic selected = cstruct.ParseStream(stream, "root.second");
        Assert.AreEqual(0x12345678U, (uint)selected.large);

        var data = new
        {
            first = new { small = (byte)0x2A, },
            second = new { large = 0x12345678U, },
        };
        CollectionAssert.AreEqual(expected, cstruct.Serialize("root", data));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.second.large", 0xAABBCCDDU);
        CollectionAssert.AreEqual(
            new byte[] { 0x2A, 0x00, 0x00, 0x00, 0xDD, 0xCC, 0xBB, 0xAA, },
            stream.ToArray());
    }

    /// <summary>Allows lexical inline field spellings to match globals and codecs without changing type lookup.</summary>
    [TestMethod]
    public void InlineFieldNames_CanMatchGlobalDeclarationsAndBuiltInCodecs()
    {
        const string layout = """
                              struct item { uint8 named; };
                              struct root {
                                  struct { uint16 local; } item;
                                  struct { uint8 raw; } byte;
                              };
                              """;
        var cstruct = new CStruct(layout);
        using var stream = new MemoryStream([0x34, 0x12, 0xA5,]);

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((ushort)0x1234, (ushort)parsed.item.local);
        Assert.AreEqual((byte)0xA5, (byte)parsed.@byte.raw);
        CollectionAssert.AreEquivalent(
            new[] { "item", "root", },
            cstruct.CStructElements.Keys.ToArray());
    }

    /// <summary>Accepts private declaration identities that reuse spellings outside their own lexical member scope.</summary>
    [TestMethod]
    public void PrivateStructIdentities_DoNotCollideWithUnrelatedNames()
    {
        string[] layouts =
        [
            "typedef struct byte { uint8 value; } payload;",
            "struct root { struct { uint8 value; } byte; };",
            "struct payload { byte value; }; typedef struct payload { uint16 other; } payload_t;",
            "typedef struct shared { byte value; } first; typedef struct shared { uint16 value; } second;",
            "struct first { struct { byte value; } child; }; struct second { struct { uint16 value; } child; };",
            "union payload { byte value; }; typedef struct payload { uint16 other; } payload_t;",
            "enum state { Ready }; typedef struct state { uint16 value; } payload;",
            "typedef uint16 backing; typedef struct backing { uint16 value; } payload;",
            "#define child 1\nstruct root { struct { uint16 value; } child; };",
            "struct first { struct { struct { byte value; } leaf; } branch; }; " +
            "struct second { struct { struct { uint16 value; } leaf; } other; };",
        ];

        foreach (string layout in layouts)
        {
            _ = new CStruct(layout);
        }
    }

    /// <summary>Rejects attempts to use an anonymous inline field spelling as a declared type before touching bytes.</summary>
    [TestMethod]
    public void Constructor_RejectsReferencesToAnonymousInlineIdentitiesBeforeStreamAccess()
    {
        string[] layouts =
        [
            "struct first { struct { uint8 item; } local; }; struct second { local leaked; };",
            "struct root { struct { value *next; uint8 item; } value; };",
        ];

        foreach (string layout in layouts)
        {
            using var stream = new MemoryStream([0xEE, 0x11,]);
            stream.Position = 1;
            byte[] original = stream.ToArray();

            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
                () =>
                {
                    var cstruct = new CStruct(layout, pointerSize: 1);
                    _ = cstruct.ParseStream(stream, cstruct.CStructElements.Keys.First());
                },
                layout);

            StringAssert.Contains(exception.Message, "Unknown field type", layout);
            Assert.AreEqual(1L, stream.Position, layout);
            CollectionAssert.AreEqual(original, stream.ToArray(), layout);
        }
    }

    /// <summary>Validates every private backing and nested inline declaration even when no exported root uses it.</summary>
    [TestMethod]
    public void Constructor_ValidatesUnusedPrivateDeclarations()
    {
        string[] layouts =
        [
            "typedef struct backing { uint32 values[]; } payload;",
            "typedef struct backing { struct { uint32 values[]; } child; } payload;",
        ];

        foreach (string layout in layouts)
        {
            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(layout), layout);
            StringAssert.Contains(exception.Message, "Only character fields can use an unsized array", layout);
        }
    }

    /// <summary>Keeps recursion through an actual global declaration legal while anonymous identities remain private.</summary>
    [TestMethod]
    public void NamedPointerRecursion_RemainsLegal()
    {
        var cstruct = new CStruct("struct node { node *next; uint8 value; };", pointerSize: 1);
        var data = new { next = 0L, value = (byte)0x2A, };

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x2A, }, cstruct.Serialize("node", data));

        using var stream = new MemoryStream([0x00, 0x2A,]);
        dynamic parsed = cstruct.ParseStream(
            stream,
            "node",
            (IReadOnlyDictionary<string, int>?)null,
            options: new ReadOptions { DereferencePointers = false, });
        Assert.AreEqual(0L, ((Pointer)parsed.next).Address);
        Assert.AreEqual((byte)0x2A, (byte)parsed.value);

        var aliased = new CStruct(
            "typedef struct node_tag { node *next; uint8 value; } node; struct root { node item; };",
            pointerSize: 1);
        var aliasedData = new { item = new { next = 0L, value = (byte)0x5A, }, };
        CollectionAssert.AreEqual(
            new byte[] { 0x00, 0x5A, },
            aliased.Serialize("root", aliasedData));
    }

    /// <summary>Preserves pointer depth while resolving a pointer typedef used by another pointer declarator.</summary>
    [TestMethod]
    public void PointerTypedefDepth_RemainsCompositional()
    {
        const string layout = "typedef uint16* target_pointer; struct root { target_pointer *value; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream([0x02, 0xEE, 0x04, 0xEE, 0x34, 0x12,]);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        var outer = (Pointer)parsed.value;

        Assert.AreEqual(2L, outer.Address);
        Assert.AreEqual(4L, outer.Next!.Address);
        Assert.AreEqual((ushort)0x1234, (ushort)outer.Next.Value!);
        stream.Position = 0;
        Assert.AreEqual(4L, cstruct.ResolveAddress(stream, "root.value.value.value"));
        cstruct.UpdateStream(stream, "root.value.value.value", (ushort)0xBEEF);
        CollectionAssert.AreEqual(
            new byte[] { 0x02, 0xEE, 0x04, 0xEE, 0xEF, 0xBE, },
            stream.ToArray());
    }

    /// <summary>Writes one unsigned value at a fixed offset in the requested byte order.</summary>
    private static void WriteUnsigned(byte[] bytes, int offset, int width, ulong value, bool littleEndian)
    {
        for (int index = 0; index < width; index++)
        {
            int shift = littleEndian ? index * 8 : (width - index - 1) * 8;
            bytes[offset + index] = (byte)(value >> shift);
        }
    }
}
