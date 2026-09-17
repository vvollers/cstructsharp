namespace CStructSharp.Docs.Examples;

using System.Collections.Generic;
using System.Numerics;
using global::CStructSharp;
using global::CStructSharp.Codecs;
using global::CStructSharp.Introspection;
using global::CStructSharp.Values;

internal static partial class Program
{
    #region recipe-windows-header
    private static void WindowsHeader()
    {
        // A header pasted from a Windows SDK or a dissect definition: SDK spellings, a repeated `_` padding
        // field, a tagged inline union whose members are promoted, and a pointer-to-string alias.
        const string definition = """
            typedef struct _RECORD {
                DWORD   Magic;
                WORD    Version;
                WORD    _;
                union version_information {
                    DWORD Packed;
                    struct { BYTE Major; BYTE Minor; WORD Build; };
                };
                DWORD   _;
                PWSTR   Name;
            } RECORD, *PRECORD;
            """;
        var layout = new CStruct(definition, pointerSize: 4, aligned: true);
        byte[] bytes = [0x4D, 0x5A, 0x00, 0x00, 0x02, 0x00, 0xFF, 0xFF, 0x0A, 0x00, 0x39, 0x30, 0xEE, 0xEE, 0xEE, 0xEE, 0x00, 0x00, 0x00, 0x00];
        dynamic record = layout.Parse(bytes, "RECORD");
        Equal(0x5A4DU, (uint)record.Magic);
        Equal((ushort)2, (ushort)record.Version);
        Equal((byte)10, (byte)record.Major);
        Equal((ushort)12345, (ushort)record.Build);
        Equal(0x3039000AU, (uint)record.Packed);
        True(((IDictionary<string, object>)record).ContainsKey("_") == false, "padding is not a member");

        // The tag, the alias, and the pointer alias all name the same declaration; the promoted union writes
        // back through the member the data supplies and the padding as zeroes.
        Equal(20, layout.GetStructSizeInBytes("_RECORD"));
        byte[] written = layout.Serialize("RECORD", new { Magic = 0x5A4DU, Version = (ushort)2, Packed = 0x3039000AU, Name = 0U });
        SequenceEqual([0x4D, 0x5A, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x39, 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], written);
    }
    #endregion

    #region recipe-flags-and-data-sized-arrays
    private static void FlagsAndDataSizedArrays()
    {
        // A flag reads as a decomposed name list; `[]` on a struct is an array terminated by an all-zero
        // element (the exFAT/APFS habit) and `[EOF]` takes every whole element that remains.
        const string definition = """
            flag access : uint16 { READ, WRITE, EXEC, HIDDEN = 0x100 };
            struct entry { uint8 kind; uint8 size; };
            struct root {
                access mode;
                entry entries[];
                uint16 trailer[EOF];
            };
            """;
        var layout = new CStruct(definition);
        byte[] bytes = [0x05, 0x01, 1, 10, 2, 20, 0, 0, 0x34, 0x12, 0x78, 0x56];
        dynamic root = layout.Parse(bytes, "root");
        var mode = (FlagValueResult)root.mode;
        Equal("READ|EXEC|HIDDEN", string.Join("|", mode.Names));
        True(mode.Has("EXEC") && !mode.Has("WRITE"), "EXEC is set and WRITE is not");
        Equal(2, ((IReadOnlyList<object?>)root.entries).Count);
        Equal((ushort)0x5678, (ushort)root.trailer[1]);

        // Writing appends the terminator element and nothing after the read-to-end array; a flag accepts names.
        byte[] written = layout.Serialize(
            "root",
            new Dictionary<string, object?>
            {
                ["mode"] = "READ|HIDDEN",
                ["entries"] = new object[] { new Dictionary<string, object?> { ["kind"] = (byte)3, ["size"] = (byte)30 } },
                ["trailer"] = new ushort[] { 1 },
            });
        SequenceEqual([0x01, 0x01, 3, 30, 0, 0, 0x01, 0x00], written);
    }
    #endregion

    #region recipe-header-preprocessor
    private static void HeaderPreprocessor()
    {
        // Preprocessor lines and expression forms that appear in real headers: text and 64-bit defines are
        // published as constants, `#ifdef` selects declarations, and counts may use sizeof, offsetof, % and ?:.
        const string definition = """
            #define MAGIC "CD001"
            #define BLOCK_MASK (1 << 40)
            #define LEGACY_VERSION 1
            #ifdef WIDE_COUNTS
            typedef uint32 count_t;
            #else
            typedef uint16 count_t;
            #endif
            struct header { uint8 kind; uint32 length; };
            struct root {
                count_t count;
                uint8 payload[count % 4 == 0 ? sizeof(header) : offsetof(header, length)];
            };
            """;
        var narrow = new CStruct(definition);
        Equal("CD001", narrow.Constants["MAGIC"].Value);
        Equal(BigInteger.One << 40, narrow.Constants["BLOCK_MASK"].Value);
        Equal(LayoutConstantKind.Integer, narrow.Constants["LEGACY_VERSION"].Kind);
        dynamic aligned = narrow.Parse(new byte[] { 4, 0, 1, 2, 3, 4, 5 }, "root");
        Equal(5, ((IReadOnlyList<object?>)aligned.payload).Count);
        dynamic unaligned = narrow.Parse(new byte[] { 3, 0, 1 }, "root");
        Equal(1, ((IReadOnlyList<object?>)unaligned.payload).Count);

        // The same source compiled with a -D style definition takes the other branch and is its own cache entry.
        var wide = CStruct.GetOrCompile(definition, compilationOptions: new CStructCompilationOptions { Defined = new HashSet<string> { "WIDE_COUNTS" } });
        Equal(4, wide.Layout.Declarations.Single(item => item.Name == "root").Fields[0].Size!.Value);
    }
    #endregion

    #region recipe-layout-introspection
    private static void LayoutIntrospection()
    {
        // `Layout` describes the compiled declarations with sizes, offsets, and members; `ToDefinition` renders
        // them back; `WithEndianness` compiles the sibling layout a byte-order-switching format needs.
        var layout = new CStruct("enum kind : uint8 { NONE, DATA = 2 }; struct root { kind type; uint16 length; char name[4]; };");
        LayoutDeclarationInfo root = layout.Layout.Declarations.Single(item => item.Name == "root");
        Equal(7, root.Size!.Value);
        Equal(1, root.Fields[1].Offset!.Value);
        Equal("uint16", root.Fields[1].TypeName);
        Equal(LayoutArrayKind.Fixed, root.Fields[2].ArrayKind);
        LayoutDeclarationInfo kind = layout.Layout.Declarations.Single(item => item.Name == "kind");
        Equal(new BigInteger(2), kind.Members[1].Value);

        string definition = layout.ToDefinition();
        True(definition.Contains("enum kind : uint8 {", StringComparison.Ordinal), "the rendered definition names the enum");
        Equal(7, new CStruct(definition).GetStructSizeInBytes("root"));

        byte[] bigEndian = [2, 0x01, 0x02, (byte)'a', (byte)'b', 0, 0];
        Equal((ushort)0x0102, (ushort)layout.WithEndianness(isLittleEndian: false).Parse(bigEndian, "root").length);
        True(ReferenceEquals(layout, layout.WithEndianness(isLittleEndian: true)), "the same byte order returns the same instance");
    }
    #endregion

    #region recipe-custom-codec
    private static void CustomCodec()
    {
        // A caller-defined type: a protobuf-style varint registered under the name a definition uses.
        var codecs = new List<ICustomCodec> { new Varint() };
        var layout = new CStruct(
            "struct entry { varint id; uint8 kind; }; struct root { varint count; entry items[count]; };",
            compilationOptions: new CStructCompilationOptions { Codecs = codecs });
        byte[] bytes = [2, 0x80, 0x01, 7, 0x05, 9];
        dynamic root = layout.Parse(bytes, "root");
        Equal(128UL, (ulong)root.items[0].id);
        Equal((byte)9, (byte)root.items[1].kind);
        SequenceEqual(bytes, layout.Serialize("root", root));
        using var stream = new MemoryStream(bytes);
        Equal(4L, layout.ResolveAddress(stream, "root.items[1].id"));
    }

    private sealed class Varint : ICustomCodec
    {
        public string Name => "varint";

        public int? FixedSize => null;

        public int Alignment => 1;

        public object Read(Stream stream)
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                int next = stream.ReadByte();
                if (next < 0)
                {
                    throw new EndOfStreamException();
                }

                value |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("varint longer than 64 bits");
        }

        public void Write(Stream stream, object value)
        {
            ulong remaining = Convert.ToUInt64(value);
            do
            {
                byte next = (byte)(remaining & 0x7F);
                remaining >>= 7;
                stream.WriteByte(remaining == 0 ? next : (byte)(next | 0x80));
            }
            while (remaining != 0);
        }
    }
    #endregion
}
