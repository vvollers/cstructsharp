namespace CStructSharp.Tests;

using System.Numerics;

/// <summary>
///     The API surface added for dissect parity: roots named by a type spelling, custom codecs, preludes, sibling
///     layouts, and the read-only layout description with its rendering back to source.
/// </summary>
[TestClass]
public class LayoutApiParityTests
{
    /// <summary>A primitive, enum, typedef, or struct spelling with optional dimensions is a root, like <c>cs.uint32(fh)</c>.</summary>
    [TestMethod]
    public void TypeSpellingRoots_ReadAndWrite()
    {
        var layout = new CStruct("enum kind : uint8 { A = 1 }; struct entry { uint8 a; uint8 b; };");
        Assert.AreEqual(0x04030201U, (uint)layout.Parse(new byte[] { 1, 2, 3, 4, }.AsSpan(), "uint32"));
        Assert.AreEqual(0x0102, (int)layout.Parse(new byte[] { 1, 2, }.AsSpan(), "uint16>"));
        Assert.AreEqual(3U, (uint)layout.Parse(new byte[] { 3, 0, 0, 0, }.AsSpan(), "DWORD"));
        Assert.AreEqual("A", ((EnumValueResult)layout.Parse(new byte[] { 1, }.AsSpan(), "kind")).Name);

        dynamic pair = layout.Parse(new byte[] { 1, 0, 2, 0, }.AsSpan(), "uint16[2]");
        Assert.AreEqual((ushort)2, (ushort)pair[1]);
        dynamic counted = layout.Parse(new byte[] { 1, 2, 3, }.AsSpan(), "uint8[N]", new Dictionary<string, int> { ["N"] = 3, });
        Assert.AreEqual(3, ((IEnumerable<object?>)counted).Count());
        dynamic rest = layout.Parse(new byte[] { 5, 0, 6, 0, }.AsSpan(), "uint16[EOF]");
        Assert.AreEqual((ushort)6, (ushort)rest[1]);
        Assert.AreEqual("hi", (string)layout.Parse("hi\0zz"u8.ToArray().AsSpan(), "char[]"));
        Assert.AreEqual("hi", (string)layout.Parse("hi\0"u8.ToArray().AsSpan(), "cstring"));
        dynamic entries = layout.Parse(new byte[] { 1, 2, 3, 4, }.AsSpan(), "entry[2]");
        Assert.AreEqual((byte)4, (byte)entries[1].b);
        Assert.AreEqual((byte)9, (byte)layout.Parse(new byte[] { 9, 0, 0, 0, }.AsSpan(), "unsigned char"));

        using var stream = new MemoryStream(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, });
        Assert.AreEqual(1U, (uint)layout.ParseStream(stream, "uint32"));
        Assert.AreEqual(2U, (uint)layout.ParseStream(stream, "uint32"));
        Assert.AreEqual(8, stream.Position);

        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, layout.Serialize("uint16", (ushort)0x1234));
        CollectionAssert.AreEqual(new byte[] { 1, 0, 2, 0, }, layout.Serialize("uint16[2]", new ushort[] { 1, 2, }));
        Assert.AreEqual(0x1234, layout.ReadValue<ushort>(new byte[] { 0x34, 0x12, }.AsSpan(), "uint16"));
        Assert.AreEqual(2, layout.GetDynamicArrayLength(new MemoryStream(new byte[] { 1, 0, 2, 0, }), "uint16[EOF]"));

        Assert.Throws<CStructPathException>(() => layout.Parse(new byte[4].AsSpan(), "missing"));
        Assert.Throws<CStructPathException>(() => layout.Parse(new byte[4].AsSpan(), "void"));
        Assert.AreEqual(2, layout.GetStructSizeInBytes("entry"));
    }

    /// <summary>A declared name always wins over a spelling, and a spelling root is compiled once per layout.</summary>
    [TestMethod]
    public void TypeSpellingRoots_DeclarationsTakePrecedence()
    {
        var layout = new CStruct("struct DWORD { uint8 a; };");
        Assert.AreEqual(1, layout.GetStructSizeInBytes("DWORD"));
        Assert.AreEqual((byte)7, (byte)layout.Parse(new byte[] { 7, }.AsSpan(), "DWORD").a);
        var layout2 = new CStruct("struct root { uint8 a; };");
        Assert.AreEqual(1, layout2.GetStructSizeInBytes("root"));
        Assert.AreEqual((byte)7, (byte)layout2.Parse(new byte[] { 7, }.AsSpan(), "root").a);
        Assert.AreEqual(3U, (uint)layout2.Parse(new byte[] { 3, 0, 0, 0, }.AsSpan(), "uint32"));
        Assert.AreEqual(4U, (uint)layout2.Parse(new byte[] { 4, 0, 0, 0, }.AsSpan(), "uint32"));
    }

    /// <summary>A custom codec is a first-class primitive: fields, arrays, pointer targets, addresses, updates, and roots.</summary>
    [TestMethod]
    public void CustomCodecs_AreFirstClassPrimitives()
    {
        var options = new CStructCompilationOptions { Codecs = [Varint.Instance, Sid.Instance,], };
        var layout = new CStruct("struct root { varint count; varint values[count]; sid owner; uint8 tail; };", compilationOptions: options);
        byte[] bytes = [2, 0x80, 0x01, 0x05, 1, 2, 3, 4, 9,];

        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(2UL, (ulong)value.count);
        Assert.AreEqual(128UL, (ulong)value.values[0]);
        Assert.AreEqual(5UL, (ulong)value.values[1]);
        Assert.AreEqual("1-2-3-4", (string)value.owner);
        Assert.AreEqual((byte)9, (byte)value.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", value));

        using var stream = new MemoryStream((byte[])bytes.Clone());
        Assert.AreEqual(4, layout.ResolveAddress(stream, "root.owner"));
        Assert.AreEqual(3, layout.ResolveAddress(stream, "root.values[1]"));
        Assert.AreEqual(2, layout.GetDynamicArrayLength(stream, "root.values"));
        layout.UpdateStream(stream, "root.values[1]", 6UL);
        Assert.AreEqual(6, stream.ToArray()[3]);
        Assert.Throws<CStructWriteException>(() => layout.UpdateStream(stream, "root.values[1]", 300UL));
        Assert.AreEqual(8, layout.ResolveAddress(stream, "root.tail"));
        Assert.AreEqual(300UL, (ulong)layout.Parse(new byte[] { 0xAC, 0x02, }.AsSpan(), "varint"));

        (List<DebugData> debug, dynamic _) = layout.ParseStreamWithDebug(new MemoryStream(bytes), "root");
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.owner" && item.CurPos == 4 && item.EndPos == 8));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { varint v; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { varint v : 3; };", compilationOptions: options));
        Assert.Throws<ArgumentException>(() => new CStruct("struct root { uint8 v; };", compilationOptions: new CStructCompilationOptions { Codecs = [new NamedCodec("uint8"),], }));
        Assert.Throws<ArgumentException>(() => new CStruct("struct root { uint8 v; };", compilationOptions: new CStructCompilationOptions { Codecs = [new NamedCodec("not valid"),], }));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct varint { uint8 v; };", compilationOptions: options));

        CStruct cached = CStruct.GetOrCompile("struct root { varint v; };", compilationOptions: options);
        Assert.AreSame(cached, CStruct.GetOrCompile("struct root { varint v; };", compilationOptions: options));
    }

    /// <summary>A prelude is earlier source: its declarations are the layout's, and it is part of the cache key.</summary>
    [TestMethod]
    public void Prelude_IsSharedSource()
    {
        var options = new CStructCompilationOptions { Prelude = "typedef uint32 Elf32_Addr; #define EI_NIDENT 16", };
        var layout = new CStruct("struct header { uint8 ident[EI_NIDENT]; Elf32_Addr entry; };", compilationOptions: options);
        Assert.AreEqual(20, layout.GetStructSizeInBytes("header"));
        Assert.IsTrue(layout.Layout.Declarations.Any(item => item.Name == "Elf32_Addr"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct header { Elf32_Addr entry; };"));

        CStruct first = CStruct.GetOrCompile("struct header { Elf32_Addr entry; };", compilationOptions: options);
        CStruct second = CStruct.GetOrCompile("struct header { Elf32_Addr entry; };", compilationOptions: new CStructCompilationOptions { Prelude = "typedef uint16 Elf32_Addr;", });
        Assert.AreEqual(4, first.GetStructSizeInBytes("header"));
        Assert.AreEqual(2, second.GetStructSizeInBytes("header"));
    }

    /// <summary>Sibling layouts share source and options and differ in one constructor choice.</summary>
    [TestMethod]
    public void Siblings_ChangeOneOption()
    {
        var layout = new CStruct("struct root { uint16 value; uint8 *p; };", pointerSize: 4);
        CStruct big = layout.WithEndianness(false);
        Assert.AreSame(layout, layout.WithEndianness(true));
        Assert.AreSame(big, layout.WithEndianness(false));
        Assert.AreEqual((ushort)0x0102, (ushort)big.Parse(new byte[] { 1, 2, 0, 0, 0, 0, }.AsSpan(), "root").value);
        Assert.AreEqual(10, layout.WithPointerSize(8).GetStructSizeInBytes("root"));
        Assert.AreEqual(8, layout.WithAlignment(true).GetStructSizeInBytes("root"));
        Assert.AreSame(layout.CompilationOptions, big.CompilationOptions);
    }

    /// <summary>The layout description reports every declaration with sizes, offsets, members, and promoted fields.</summary>
    [TestMethod]
    public void Layout_DescribesDeclarations()
    {
        var layout = new CStruct(
            "#include <stdint.h>\n#define N 2\nenum kind : uint8 { A = 1, B }; flag f : uint16 { X, Y }; typedef uint32 word, *pword; typedef uint8 pair[N];\n" +
            "struct root { word w; kind k : 3; kind k2 : 2; pair p; uint8 dyn[N]; struct { uint16 x; }; union { uint8 u8; uint32 u32; } u; char name[]; uint8 tail; };",
            pointerSize: 4,
            aligned: true);
        LayoutInfo info = layout.Layout;
        Assert.AreSame(info, layout.Layout);
        CollectionAssert.AreEqual(new[] { "stdint.h", }, info.Includes.ToArray());
        Assert.AreEqual(new BigInteger(2), info.Constants["N"].Value);

        LayoutDeclarationInfo kind = info.Declarations.Single(item => item.Name == "kind");
        Assert.AreEqual(LayoutDeclarationKind.Enum, kind.Kind);
        Assert.AreEqual("uint8", kind.UnderlyingType);
        Assert.AreEqual(new BigInteger(2), kind.Members[1].Value);
        Assert.AreEqual(LayoutDeclarationKind.Flag, info.Declarations.Single(item => item.Name == "f").Kind);

        LayoutDeclarationInfo pword = info.Declarations.Single(item => item.Name == "pword");
        Assert.AreEqual(LayoutDeclarationKind.Typedef, pword.Kind);
        Assert.AreEqual(1, pword.PointerDepth);
        Assert.AreEqual(4, pword.Size);
        LayoutDeclarationInfo pair = info.Declarations.Single(item => item.Name == "pair");
        CollectionAssert.AreEqual(new[] { 2, }, pair.ArrayShape.ToArray());
        Assert.AreEqual(2, pair.Size);

        LayoutDeclarationInfo root = info.Declarations.Single(item => item.Name == "root");
        Assert.AreEqual(LayoutDeclarationKind.Struct, root.Kind);
        Assert.IsNull(root.Size);
        Assert.AreEqual(4, root.Alignment);
        LayoutFieldInfo w = root.Fields[0];
        Assert.AreEqual(("w", "uint32", 0, 4), (w.Name, w.TypeName, w.Offset, w.Size));
        LayoutFieldInfo k2 = root.Fields[2];
        Assert.AreEqual((2, 3, 4), (k2.BitWidth, k2.BitOffset, k2.Offset));
        LayoutFieldInfo p = root.Fields[3];
        Assert.AreEqual(LayoutArrayKind.Fixed, p.ArrayKind);
        CollectionAssert.AreEqual(new int?[] { 2, }, p.Dimensions.ToArray());
        Assert.AreEqual(5, p.Offset);
        LayoutFieldInfo dyn = root.Fields[4];
        Assert.AreEqual(LayoutArrayKind.Runtime, dyn.ArrayKind);
        Assert.IsNull(dyn.Dimensions[0]);
        LayoutFieldInfo anonymous = root.Fields[5];
        Assert.IsTrue(anonymous.IsAnonymous);
        Assert.AreEqual("x", anonymous.PromotedFields[0].Name);
        Assert.AreEqual("u", root.Fields[6].Name);
        Assert.AreEqual(LayoutArrayKind.String, root.Fields[7].ArrayKind);
        Assert.IsNull(root.Fields[8].Offset);
    }

    /// <summary>The rendered definition compiles to a layout with the same sizes, offsets, and values.</summary>
    [TestMethod]
    public void ToDefinition_RoundTrips()
    {
        const string source = """
                              #include "types.h"
                              #define MAGIC "CD001"
                              #define N 2 + 1
                              #define FLAG
                              enum kind : uint16 { A = 1, B, C = A << 4 };
                              flag bits : uint8 { X, Y };
                              typedef struct _pt { int16 x; int16 y; } pt, *ppt;
                              typedef uint8 quad[4];
                              struct root @align(2) {
                                  pt origin;
                                  kind k : 3;
                                  uint8 count;
                                  uint8 values[count * N];
                                  struct { uint8 a; uint8 b; };
                                  union { uint16 w; uint8 lo; } u;
                                  char name[];
                                  uint8 tag;
                                  if (tag == kind.B) { uint16 extra; } else { uint8 small; }
                                  switch (tag) { case 1: { uint8 one; } default: { uint8 other; } }
                                  quad q;
                                  ppt link;
                                  uint16 last @align(4);
                              };
                              """;
        var original = new CStruct(source, pointerSize: 4, aligned: true);
        string rendered = original.ToDefinition();
        var roundTrip = new CStruct(rendered, pointerSize: 4, aligned: true);

        byte[] bytes = [1, 0, 2, 0, 0x05, 0, 2, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0x10, 0x11, (byte)'n', 0, 2, 0x20, 0x21, 0x22, 1, 2, 3, 4, 8, 0, 0, 0, 0, 0, 0, 0, 0x30, 0x31,];
        (List<DebugData> before, dynamic _) = original.ParseStreamWithDebug(new MemoryStream(bytes), "root");
        (List<DebugData> after, dynamic _) = roundTrip.ParseStreamWithDebug(new MemoryStream(bytes), "root");
        CollectionAssert.AreEqual(
            before.Select(item => (item.DebugStackString, item.CurPos, item.EndPos)).ToArray(),
            after.Select(item => (item.DebugStackString, item.CurPos, item.EndPos)).ToArray());
        Assert.AreEqual(original.GetStructSizeInBytes("pt"), roundTrip.GetStructSizeInBytes("pt"));
        Assert.AreEqual(original.GetStructSizeInBytes("_pt"), roundTrip.GetStructSizeInBytes("_pt"));
        Assert.AreEqual("CD001", roundTrip.Constants["MAGIC"].Value);
        Assert.AreEqual(LayoutConstantKind.Empty, roundTrip.Constants["FLAG"].Kind);
        CollectionAssert.AreEqual(new[] { "types.h", }, roundTrip.Includes.ToArray());
        Assert.AreEqual(new BigInteger(16), roundTrip.Layout.Declarations.Single(item => item.Name == "kind").Members[2].Value);
        Assert.AreEqual(LayoutDeclarationKind.Flag, roundTrip.Layout.Declarations.Single(item => item.Name == "bits").Kind);
    }

    /// <summary>An unsigned LEB128 codec, as protobuf and the Windows "Everything" index use.</summary>
    private sealed class Varint : ICustomCodec
    {
        public static readonly Varint Instance = new();

        public string Name => "varint";

        public int? FixedSize => null;

        public int Alignment => 1;

        public object Read(Stream stream)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                int next = stream.ReadByte();
                if (next < 0)
                {
                    throw new EndOfStreamException();
                }

                result |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }
        }

        public void Write(Stream stream, object value)
        {
            ulong remaining = Convert.ToUInt64(value);
            do
            {
                byte chunk = (byte)(remaining & 0x7F);
                remaining >>= 7;
                stream.WriteByte(remaining == 0 ? chunk : (byte)(chunk | 0x80));
            }
            while (remaining != 0);
        }
    }

    /// <summary>A fixed four-byte identifier rendered as dotted text.</summary>
    private sealed class Sid : ICustomCodec
    {
        public static readonly Sid Instance = new();

        public string Name => "sid";

        public int? FixedSize => 4;

        public int Alignment => 1;

        public object Read(Stream stream)
        {
            Span<byte> bytes = stackalloc byte[4];
            stream.ReadExactly(bytes);
            return string.Join('-', bytes.ToArray());
        }

        public void Write(Stream stream, object value)
        {
            foreach (string part in ((string)value).Split('-'))
            {
                stream.WriteByte(byte.Parse(part, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    private sealed class NamedCodec(string name) : ICustomCodec
    {
        public string Name => name;

        public int? FixedSize => 1;

        public int Alignment => 1;

        public object Read(Stream stream) => (byte)stream.ReadByte();

        public void Write(Stream stream, object value) => stream.WriteByte(Convert.ToByte(value));
    }
}
