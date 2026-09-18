namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The built-in alias spellings (Windows SDK, Linux kernel, IDA, C99, dissect) resolve to their canonical codecs
///     everywhere a type name is accepted: fields, bitfield storage, enum backing, typedef chains, and roots.
/// </summary>
[TestClass]
public class PrimitiveSpellingTests
{
    /// <summary>Every alias spelling compiles to the same size and value as its canonical codec.</summary>
    [TestMethod]
    public void Aliases_ResolveToCanonicalCodecs()
    {
        foreach ((string alias, string canonical) in PrimitiveSpellings.Aliases)
        {
            if (PrimitiveCodecs.IsVariableLengthType(canonical) || Leb128Codec.IsType(canonical) || canonical == "void")
            {
                continue;
            }

            var aliased = new CStruct($"struct root {{ {alias} value; }};");
            var reference = new CStruct($"struct root {{ {canonical} value; }};");
            Assert.AreEqual(reference.GetStructSizeInBytes("root"), aliased.GetStructSizeInBytes("root"), alias);
            Assert.AreEqual(reference.GetStructAlignmentInBytes("root"), aliased.GetStructAlignmentInBytes("root"), alias);

            byte[] bytes = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
            dynamic first = aliased.Parse(bytes.AsSpan(), "root");
            dynamic second = reference.Parse(bytes.AsSpan(), "root");
            Assert.AreEqual((object)second.value, (object)first.value, alias);
        }
    }

    /// <summary>The Windows SDK vocabulary compiles verbatim with its documented widths.</summary>
    [TestMethod]
    public void WindowsSpellings_HaveDocumentedWidths()
    {
        var layout = new CStruct(
            "struct root { BYTE a; WORD b; DWORD c; QWORD d; LONG e; ULONG f; USHORT g; UCHAR h; CHAR i; WCHAR j; LONGLONG k; ULONGLONG l; INT8 m; UINT64 n; };");
        Assert.AreEqual(1 + 2 + 4 + 8 + 4 + 4 + 2 + 1 + 1 + 2 + 8 + 8 + 1 + 8, layout.GetStructSizeInBytes("root"));

        dynamic value = layout.Parse(new byte[54].AsSpan(), "root");
        Assert.AreEqual((byte)0, (byte)value.a);
        Assert.AreEqual((char)0, (char)value.i);
        Assert.AreEqual((char)0, (char)value.j);
    }

    /// <summary>Kernel, IDA, and MSVC spellings compile, including the multi-word <c>unsigned __int64</c>.</summary>
    [TestMethod]
    public void KernelAndCompilerSpellings_Compile()
    {
        var layout = new CStruct("struct root { u8 a; __u16 b; u32 c; __u64 d; s8 e; __s32 f; wchar_t g; _BYTE h; _DWORD i; unsigned __int64 j; __int32 k; u_int16_t l; };");
        Assert.AreEqual(1 + 2 + 4 + 8 + 1 + 4 + 2 + 1 + 4 + 8 + 4 + 2, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>dissect's <c>uleb128</c>/<c>ileb128</c> spellings decode as the 64-bit LEB128 codecs.</summary>
    [TestMethod]
    public void Leb128Spellings_DecodeAsWidestCodecs()
    {
        var layout = new CStruct("struct root { uleb128 a; ileb128 b; };");
        dynamic value = layout.Parse(new byte[] { 0xE5, 0x8E, 0x26, 0x7F, }.AsSpan(), "root");
        Assert.AreEqual(624485UL, (ulong)value.a);
        Assert.AreEqual(-1L, (long)value.b);
    }

    /// <summary>The <c>long</c> family is 64 bits by default and 32 bits with <see cref="CStructCompilationOptions.CLongWidth"/> = 32; Windows <c>LONG</c> never moves.</summary>
    [TestMethod]
    public void CLongWidth_SelectsTheLongFamilyWidthOnly()
    {
        const string source = "struct root { long a; unsigned long b; ulong c; long int d; LONG e; ULONG f; };";
        var lp64 = new CStruct(source);
        var ilp32 = new CStruct(source, compilationOptions: new CStructCompilationOptions { CLongWidth = 32, });
        Assert.AreEqual((8 * 4) + 4 + 4, lp64.GetStructSizeInBytes("root"));
        Assert.AreEqual(4 * 6, ilp32.GetStructSizeInBytes("root"));

        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0, 5, 0, 0, 0,];
        dynamic value = ilp32.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(-1, (int)value.a);
        Assert.AreEqual(1U, (uint)value.b);
        Assert.AreEqual(5U, (uint)value.f);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CStruct(source, compilationOptions: new CStructCompilationOptions { CLongWidth = 16, }));
    }

    /// <summary>The <c>long</c> width also applies to enum backing and bitfield storage, and is part of the compiled-layout cache key.</summary>
    [TestMethod]
    public void CLongWidth_AppliesToEnumsBitfieldsAndCacheKey()
    {
        const string source = "enum mode : unsigned long { A = 1 }; struct root { long low : 4; long high : 4; mode m; };";
        var narrow = new CStruct(source, compilationOptions: new CStructCompilationOptions { CLongWidth = 32, });

        // Packed SysV placement keeps only the byte the eight bits need before the enum: 1 + 4 with a 32-bit long,
        // 1 + 8 with a 64-bit one; aligned placement pads the enum to its own width.
        Assert.AreEqual(5, narrow.GetStructSizeInBytes("root"));
        Assert.AreEqual(9, new CStruct(source).GetStructSizeInBytes("root"));
        Assert.AreEqual(8, new CStruct(source, aligned: true, compilationOptions: new CStructCompilationOptions { CLongWidth = 32, }).GetStructSizeInBytes("root"));
        Assert.AreEqual(16, new CStruct(source, aligned: true).GetStructSizeInBytes("root"));

        CStruct cachedWide = CStruct.GetOrCompile(source);
        CStruct cachedNarrow = CStruct.GetOrCompile(source, compilationOptions: new CStructCompilationOptions { CLongWidth = 32, });
        Assert.AreNotSame(cachedWide, cachedNarrow);
        Assert.AreEqual(9, cachedWide.GetStructSizeInBytes("root"));
        Assert.AreEqual(5, cachedNarrow.GetStructSizeInBytes("root"));
    }

    /// <summary>A 32-bit <c>long</c> layout goes through every operation with 4-byte storage.</summary>
    [TestMethod]
    public void CLongWidth_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct("struct root { long a; unsigned long b; uint8 tail; };", compilationOptions: new CStructCompilationOptions { CLongWidth = 32, });
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 2, 0, 0, 0, 7,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        IReadOnlyList<DebugData> debug = layout.ParseWithDebug(stream, "root").Debug;
        Assert.IsTrue(debug.Any(item => item.Path == "root.b" && item.Start == 4 && item.End == 8));
        stream.Position = 0;
        Assert.AreEqual(8, layout.ResolveAddress(stream, "root.tail"));
        Assert.AreEqual(-1, layout.ReadValue<int>(bytes.AsSpan(), "root.a"));

        byte[] written = layout.Serialize("root", new Dictionary<string, object?> { ["a"] = -1, ["b"] = 2U, ["tail"] = (byte)7, });
        CollectionAssert.AreEqual(bytes, written);
        using var target = new MemoryStream();
        layout.Write(target, "root", new Dictionary<string, object?> { ["a"] = -1, ["b"] = 2U, ["tail"] = (byte)7, });
        CollectionAssert.AreEqual(bytes, target.ToArray());
        layout.Update(stream, "root.b", 9U);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 9, 0, 0, 0, 7, }, stream.ToArray());
    }

    /// <summary>A layout may carry its own <c>typedef</c> of an alias spelling; the layout's definition wins.</summary>
    [TestMethod]
    public void UserTypedef_ShadowsBuiltInAlias()
    {
        var same = new CStruct("typedef uint32 DWORD; struct root { DWORD a; };");
        Assert.AreEqual(4, same.GetStructSizeInBytes("root"));

        var different = new CStruct("typedef uint16 DWORD; typedef uint8 CHAR; struct root { DWORD a; CHAR b; };");
        Assert.AreEqual(3, different.GetStructSizeInBytes("root"));
        dynamic value = different.Parse(new byte[] { 1, 0, 0x41, }.AsSpan(), "root");
        Assert.AreEqual((ushort)1, (ushort)value.a);
        Assert.AreEqual((byte)0x41, (byte)value.b);

        var enumShadow = new CStruct("enum BYTE : uint16 { A = 1 }; struct root { BYTE b; };");
        Assert.AreEqual(2, enumShadow.GetStructSizeInBytes("root"));

        // Canonical codec names remain reserved.
        Assert.Throws<CStructLayoutException>(() => new CStruct("typedef uint16 uint32; struct root { uint32 a; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct char { uint8 a; };"));
    }

    /// <summary>Enum backing accepts alias spellings, multi-word spellings, and typedef chains that end in one.</summary>
    [TestMethod]
    public void EnumBacking_AcceptsAliasSpellings()
    {
        var layout = new CStruct(
            "typedef ULONG my_ulong; enum a : ULONG { X = 1 }; enum b : unsigned int { Y = 2 }; enum c : my_ulong { Z = 3 }; enum d : uint8_t { W = 4 }; struct root { a p; b q; c r; d s; };");
        Assert.AreEqual(13, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, }.AsSpan(), "root");
        Assert.AreEqual("uint32", ((EnumValueResult)value.p).StorageType);
        Assert.AreEqual("Z", ((EnumValueResult)value.r).Name);
        Assert.AreEqual(new BigInteger(4), ((EnumValueResult)value.s).Value);
    }

    /// <summary>Bitfield storage may be an alias spelling or a typedef of an integer codec; aliases of one codec share a storage unit.</summary>
    [TestMethod]
    public void BitfieldStorage_AcceptsAliasesAndTypedefs()
    {
        // Packed SysV placement: eleven contiguous bits in two bytes (GCC -fpack-struct gives 21 05).
        var layout = new CStruct("typedef uint16 my_u16; struct root { DWORD a : 4; unsigned int b : 4; my_u16 c : 3; };");

        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 0x21, 0x05, }.AsSpan(), "root");
        Assert.AreEqual(1, (int)value.a);
        Assert.AreEqual(2, (int)value.b);
        Assert.AreEqual(5, (int)value.c);

        // MSVC packing: `DWORD` and `unsigned int` are the same size, so they share one 4-byte unit; the 2-byte typedef
        // starts another.
        var msvc = new CStruct("typedef uint16 my_u16; struct root { DWORD a : 4; unsigned int b : 4; my_u16 c : 3; };", compilationOptions: new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, });
        Assert.AreEqual(6, msvc.GetStructSizeInBytes("root"));
        dynamic msvcValue = msvc.Parse(new byte[] { 0x21, 0, 0, 0, 0x05, 0, }.AsSpan(), "root");
        Assert.AreEqual(1, (int)msvcValue.a);
        Assert.AreEqual(2, (int)msvcValue.b);
        Assert.AreEqual(5, (int)msvcValue.c);
    }

    /// <summary><c>size_t</c> and its relatives are as wide as the layout's pointer size, and a layout may redeclare them.</summary>
    [TestMethod]
    public void PointerSizedSpellings_FollowThePointerSize()
    {
        const string source = "struct root { size_t a; ssize_t b; uintptr_t c; SIZE_T d; ULONG_PTR e; };";
        Assert.AreEqual(5 * 8, new CStruct(source).GetStructSizeInBytes("root"));
        Assert.AreEqual(5 * 4, new CStruct(source, pointerSize: 4).GetStructSizeInBytes("root"));
        Assert.AreEqual(5 * 2, new CStruct(source, pointerSize: 2).GetStructSizeInBytes("root"));

        var narrow = new CStruct(source, pointerSize: 4);
        dynamic value = narrow.Parse(new byte[] { 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 3, 0, 0, 0, 4, 0, 0, 0, 5, 0, 0, 0, }.AsSpan(), "root");
        Assert.AreEqual(1U, (uint)value.a);
        Assert.AreEqual(-1, (int)value.b);

        Assert.AreEqual(2, new CStruct("typedef uint16 size_t; struct root { size_t a; };").GetStructSizeInBytes("root"));
        Assert.AreEqual(8, new CStruct("enum e : size_t { A = 1 }; struct root { e v; };").GetStructSizeInBytes("root"));
    }

    /// <summary>The spelling table, the registry, and codec recognition agree on every entry.</summary>
    [TestMethod]
    public void SpellingViews_Agree()
    {
        foreach ((string alias, string canonical) in PrimitiveSpellings.Aliases)
        {
            PrimitiveCodec viaAlias = PrimitiveCodec.Resolve(alias, true);
            PrimitiveCodec viaCanonical = PrimitiveCodec.Resolve(canonical, true);
            Assert.AreEqual(viaCanonical.Kind, viaAlias.Kind, alias);
            Assert.AreEqual(viaCanonical.Size, viaAlias.Size, alias);
            Assert.AreEqual(canonical != "void", viaAlias.IsPrimitive, alias);
            Assert.AreEqual(canonical, PrimitiveSpellings.Canonicalize(alias, 64), alias);
        }

        Assert.AreEqual("int32", PrimitiveSpellings.Canonicalize("long", 32));
        Assert.AreEqual("uint64", PrimitiveSpellings.Canonicalize("unsigned long int", 64));
        Assert.AreEqual("uint8", PrimitiveSpellings.Canonicalize("uint8", 64));
        Assert.IsTrue(PrimitiveSpellings.IsAlias("DWORD"));
        Assert.IsFalse(PrimitiveSpellings.IsAlias("uint32"));
    }
}
