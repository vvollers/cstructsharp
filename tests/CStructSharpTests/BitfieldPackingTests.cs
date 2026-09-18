namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     <see cref="BitfieldPacking"/>: SysV placement (the default) reproduces GCC's bytes for every recorded shape in
///     aligned and packed placement; MSVC placement keeps one unit per declared size. Every shape is serialized,
///     parsed back, and probed through the size, address, debug, and update operations so the placement rule is
///     the same everywhere.
/// </summary>
[TestClass]
public class BitfieldPackingTests
{
    private static readonly CStructCompilationOptions Msvc = new() { BitfieldPacking = BitfieldPacking.Msvc, };

    /// <summary>Bytes recorded from <c>gcc -O0</c> on x86-64 (natural placement and <c>#pragma pack(1)</c>) for each shape.</summary>
    public static IEnumerable<object[]> GccShapes =>
    [
        ["struct s { uint8 a:4; uint16 b:4; };", "a=15,b=10", "AF00", "AF"],
        ["struct s { uint8 a:3; uint8 b:5; uint16 c; };", "a=7,b=31,c=43981", "FF00CDAB", "FFCDAB"],
        ["struct s { uint32 a:3; uint32 b:29; uint32 c:1; };", "a=7,b=536870911,c=1", "FFFFFFFF01000000", "FFFFFFFF01"],
        ["struct s { uint16 a:15; uint8 b:2; };", "a=32767,b=3", "FF7F0300", "FFFF01"],
        ["struct s { uint8 a:3; uint8 :0; uint8 b:3; };", "a=7,b=7", "0707", "0707"],
        ["struct s { uint8 a:3; uint32 :0; uint8 b:3; };", "a=7,b=7", "0700000007", "0700000007"],
        ["struct s { int8 a:3; uint8 b:5; };", "a=7,b=31", "FF", "FF"],
        ["struct s { uint8 a:6; uint8 b:6; };", "a=63,b=63", "3F3F", "FF0F"],
        ["struct s { uint8 a:4; uint32 b:12; uint8 c:4; };", "a=15,b=4095,c=15", "FFFF0F00", "FFFF0F"],
        ["struct s { uint32 a:8; uint8 b:8; uint16 c:8; };", "a=255,b=255,c=255", "FFFFFF00", "FFFFFF"],
        ["struct s { uint8 a:4; uint16 b:12; uint8 c; };", "a=15,b=4095,c=171", "FFFFAB00", "FFFFAB"],
        ["struct s { uint64 a:4; uint8 b:4; };", "a=15,b=15", "FF00000000000000", "FF"],
        ["struct s { uint8 a:1; uint16 b:1; uint32 c:1; uint64 d:1; };", "a=1,b=1,c=1,d=1", "0F00000000000000", "0F"],
        ["struct s { uint16 a:4; uint8 b:4; uint16 c:4; };", "a=1,b=2,c=3", "2103", "2103"],
        ["struct s { uint32 a:20; uint16 b:8; };", "a=1048575,b=255", "FFFFFF0F", "FFFFFF0F"],
        ["struct s { uint8 x; uint32 a:4; uint8 b:4; };", "x=170,a=15,b=15", "AAFF0000", "AAFF"],
        ["struct s { uint8 a:3; uint8 b:5; uint8 c:1; };", "a=7,b=31,c=1", "FF01", "FF01"],
        ["struct s { uint16 a:4; int16 b:4; uint8 tail; };", "a=10,b=11,tail=165", "BAA5", "BAA5"],
        ["struct s { uint8 prefix; uint16 first:3; uint16 center:5; uint16 last:8; uint8 tail; };", "prefix=238,first=5,center=26,last=165,tail=126", "EED5A57E", "EED5A57E"],
        ["struct s { uint8 a:4; uint8 b:4; uint16 c:4; uint16 d:4; uint8 tail; };", "a=10,b=11,c=12,d=13,tail=126", "BADC7E00", "BADC7E"],
        ["struct s { uint8 head; uint16 type:2; uint16 other:3; uint8 tail; };", "head=9,type=1,other=3,tail=7", "090D0700", "090D07"],
        ["struct s { uint32 low:4; uint32 high:4; uint32 m; };", "low=1,high=2,m=1", "2100000001000000", "2101000000"],
        ["struct s { uint16 a:4; uint16 b:4; uint64 c:4; uint64 d:4; uint16 e; uint32 f:4; uint64 g; };", "a=2,b=1,c=2,d=1,e=16,f=2,g=24", "12121000020000001800000000000000", "12121000021800000000000000"],
    ];

    /// <summary>SysV placement matches GCC byte for byte, and the other operations agree with the bytes.</summary>
    [TestMethod]
    [DynamicData(nameof(GccShapes))]
    public void SysV_MatchesGcc(string layout, string values, string alignedHex, string packedHex)
    {
        AssertShape(new CStruct(layout, aligned: true), values, alignedHex);
        AssertShape(new CStruct(layout, aligned: false), values, packedHex);
    }

    /// <summary>MSVC placement: one unit per declared size, a new unit on every size change, whole units retained.</summary>
    [TestMethod]
    [DataRow("struct s { uint8 a:4; uint16 b:4; };", "a=15,b=10", "0F000A00", "0F0A00")]
    [DataRow("struct s { uint16 a:4; int16 b:4; uint8 tail; };", "a=10,b=11,tail=165", "BA00A500", "BA00A5")]
    [DataRow("struct s { uint8 a:4; uint8 b:4; uint16 c:4; uint16 d:4; uint8 tail; };", "a=10,b=11,c=12,d=13,tail=126", "BA00DC007E00", "BADC007E")]
    [DataRow("struct s { uint32 a:3; uint32 b:29; uint32 c:1; };", "a=7,b=536870911,c=1", "FFFFFFFF01000000", "FFFFFFFF01000000")]
    [DataRow("struct s { uint16 a:15; uint8 b:2; };", "a=32767,b=3", "FF7F0300", "FF7F03")]
    [DataRow("struct s { uint8 a:3; uint8 :0; uint8 b:3; };", "a=7,b=7", "0707", "0707")]
    [DataRow("struct s { uint8 a:6; uint8 b:6; };", "a=63,b=63", "3F3F", "3F3F")]
    [DataRow("struct s { uint64 a:4; uint8 b:4; };", "a=15,b=15", "0F000000000000000F00000000000000", "0F000000000000000F")]
    public void Msvc_KeepsOneUnitPerDeclaredSize(string layout, string values, string alignedHex, string packedHex)
    {
        AssertShape(new CStruct(layout, aligned: true, compilationOptions: Msvc), values, alignedHex);
        AssertShape(new CStruct(layout, aligned: false, compilationOptions: Msvc), values, packedHex);
    }

    /// <summary>Big-endian units with low-bit-first numbering fill a cell from its last byte, so SysV keeps whole cells.</summary>
    [TestMethod]
    public void SysV_BigEndianLowBitFirst_KeepsWholeCells()
    {
        // a takes bits 0-3 of the byte cell (its low nibble); b takes bits 4-7 of the big-endian uint16 cell [0,2),
        // which live in the second byte's high nibble; c follows the cell.
        var layout = new CStruct("struct s { uint8 a:4; uint16 b:4; uint8 c; };", isLittleEndian: false);
        Assert.AreEqual(3, layout.GetStructSizeInBytes("s"));
        byte[] bytes = [0x0A, 0xB0, 0xCC];
        StructValue value = layout.Parse(bytes, "s");
        Assert.AreEqual(0xA, value.Get<int>("a"));
        Assert.AreEqual(0xB, value.Get<int>("b"));
        Assert.AreEqual(0xCC, value.Get<int>("c"));
        Assert.AreEqual(2L, layout.ResolveAddress(new MemoryStream(bytes), "s.c"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("s", value));

        // High-bit-first numbering fills from the first byte, so the SysV rule applies unchanged: bytes AF CC.
        var highFirst = new CStruct("struct s { uint8 a:4; uint16 b:4; uint8 c; };", isLittleEndian: false, compilationOptions: new CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst, });
        Assert.AreEqual(2, highFirst.GetStructSizeInBytes("s"));
        StructValue high = highFirst.Parse([0xAB, 0xCC], "s");
        Assert.AreEqual(0xA, high.Get<int>("a"));
        Assert.AreEqual(0xB, high.Get<int>("b"));
        Assert.AreEqual(0xCC, high.Get<int>("c"));
    }

    /// <summary>A named field may not be zero bits wide; only the unnamed separator may.</summary>
    [TestMethod]
    public void ZeroWidth_NamedField_IsRejected()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a:3; uint8 gap:0; uint8 b:3; };"));
        StringAssert.Contains(exception.Message, "Bitfield width must be greater than zero (only an unnamed ': 0' separator may be zero): gap");
    }

    /// <summary>
    ///     A packed SysV run whose second field straddles two bytes is read through its two-byte window - also when
    ///     that field is captured as a later count during address resolution, not only during a parse.
    /// </summary>
    [TestMethod]
    public void SysV_PackedStraddlingBitfield_CountCapturedDuringAddressResolution()
    {
        // a = bits 0-5 of byte 0; n = bits 6-11 (the top two bits of byte 0 and the low nibble of byte 1): the
        // window for n is two bytes although uint8 is one. n = 3 → items[3] follows at offset 2.
        var layout = new CStruct("struct s { uint8 a:6; uint8 n:6; uint8 items[n]; uint8 tail; };", aligned: false);
        byte[] bytes = [0b1100_0000 | 0x2A, 0x00, 0x11, 0x22, 0x33, 0x44];
        StructValue value = layout.Parse(bytes, "s");
        Assert.AreEqual(0x2A, value.Get<int>("a"));
        Assert.AreEqual(3, value.Get<int>("n"));
        Assert.AreEqual(0x44, value.Get<int>("tail"));
        Assert.AreEqual(3, layout.GetArrayLength(bytes, "s.items"));
        Assert.AreEqual(4L, layout.ResolveAddress(new MemoryStream(bytes), "s.items[2]"));
        Assert.AreEqual(5L, layout.ResolveAddress(new MemoryStream(bytes), "s.tail"));
        Assert.AreEqual(0x33, layout.ReadValue<int>(bytes, "s.items[2]"));

        // With the high nibble of byte 1 set, a one-byte window would read n as 3 + 16·k; the placed window keeps 3.
        byte[] noisy = [0b1100_0000 | 0x2A, 0xF0, 0x11, 0x22, 0x33, 0x44];
        Assert.AreEqual(3, layout.ReadValue<int>(noisy, "s.n"));
        Assert.AreEqual(5L, layout.ResolveAddress(new MemoryStream(noisy), "s.tail"));
    }

    /// <summary>The separator is not a member, has no value, and its own type does not add alignment under SysV.</summary>
    [TestMethod]
    public void ZeroWidth_IsASeparatorOnly()
    {
        var layout = new CStruct("struct s { uint8 a:3; uint32 :0; uint8 b:3; };", aligned: true);
        Assert.AreEqual(5, layout.GetStructSizeInBytes("s"));
        Assert.AreEqual(1, layout.GetStructAlignmentInBytes("s"));
        StructValue value = layout.Parse([7, 0, 0, 0, 7], "s");
        CollectionAssert.AreEqual(new[] { "a", "b" }, value.Keys.ToArray());
        Assert.AreEqual(4L, layout.ResolveAddress(new MemoryStream(new byte[5]), "s.b"));
        StringAssert.Contains(layout.ToDefinition(), "uint32  : 0;");

        // MSVC lets the separator's unit boundary count toward alignment.
        var msvc = new CStruct("struct s { uint8 a:3; uint32 :0; uint8 b:3; };", aligned: true, compilationOptions: Msvc);
        Assert.AreEqual(8, msvc.GetStructSizeInBytes("s"));
        Assert.AreEqual(4, msvc.GetStructAlignmentInBytes("s"));

        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 named:0; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { float :0; };"));
    }

    /// <summary>A packed window wider than eight bytes cannot be read as one unit; the layout is rejected with the remedies.</summary>
    [TestMethod]
    public void SysV_Packed_RejectsAWindowWiderThanEightBytes()
    {
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct s { uint8 a:4; uint64 b:64; };"));
        StringAssert.Contains(failure.Message, "would span 9 bytes");
        StringAssert.Contains(failure.Message, "BitfieldPacking.Msvc");
        Assert.AreEqual(16, new CStruct("struct s { uint8 a:4; uint64 b:64; };", aligned: true).GetStructSizeInBytes("s"));
        Assert.AreEqual(9, new CStruct("struct s { uint8 a:4; uint64 b:64; };", compilationOptions: Msvc).GetStructSizeInBytes("s"));
    }

    /// <summary>The packing is part of the compiled-layout cache key and of the introspection view.</summary>
    [TestMethod]
    public void Packing_IsACacheKeyAndVisibleInLayoutInfo()
    {
        const string source = "struct s { uint8 a:4; uint16 b:4; };";
        CStruct sysv = CStruct.GetOrCompile(source);
        CStruct msvc = CStruct.GetOrCompile(source, compilationOptions: Msvc);
        Assert.AreNotSame(sysv, msvc);
        Assert.AreEqual(1, sysv.GetStructSizeInBytes("s"));
        Assert.AreEqual(3, msvc.GetStructSizeInBytes("s"));

        Introspection.LayoutFieldInfo b = sysv.Layout.Declarations.Single().Fields.Single(field => field.Name == "b");
        Assert.AreEqual(0, b.Offset);
        Assert.AreEqual(1, b.Size);
        Assert.AreEqual(4, b.BitOffset);
        Introspection.LayoutFieldInfo msvcB = msvc.Layout.Declarations.Single().Fields.Single(field => field.Name == "b");
        Assert.AreEqual(1, msvcB.Offset);
        Assert.AreEqual(2, msvcB.Size);
        Assert.AreEqual(0, msvcB.BitOffset);
    }

    private static void AssertShape(CStruct layout, string values, string hex)
    {
        byte[] expected = Convert.FromHexString(hex);
        var input = new Dictionary<string, object?>();
        foreach (string pair in values.Split(','))
        {
            string[] parts = pair.Split('=');
            input[parts[0]] = ulong.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        }

        string mode = layout.Aligned ? "aligned" : "packed";
        Assert.AreEqual(expected.Length, layout.GetStructSizeInBytes("s"), $"{mode} size");
        byte[] serialized = layout.Serialize("s", input);
        CollectionAssert.AreEqual(expected, serialized, $"{mode} bytes");

        StructValue parsed = layout.Parse(expected, "s");
        foreach ((string name, object? value) in input)
        {
            Assert.AreEqual(Convert.ToUInt64(value), Convert.ToUInt64(parsed[name]), $"{mode} {name}");
            using var stream = new MemoryStream(expected);
            Assert.AreEqual(Convert.ToUInt64(value), Convert.ToUInt64(layout.ReadValue(stream, "s." + name)), $"{mode} ReadValue {name}");
        }

        (StructValue debugValue, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(expected, "s");
        Assert.AreEqual(input.Count, debug.Count(item => item.Path.StartsWith("s.", StringComparison.Ordinal)), $"{mode} debug records");
        CollectionAssert.AreEqual(expected, layout.Serialize("s", debugValue), $"{mode} debug round trip");

        // Updating every member to its own value in place leaves the bytes untouched, and the address of each
        // member's storage lies inside the struct.
        foreach ((string name, object? value) in input)
        {
            using var stream = new MemoryStream((byte[])expected.Clone());
            layout.Update(stream, "s." + name, value!);
            CollectionAssert.AreEqual(expected, stream.ToArray(), $"{mode} update {name}");
            long address = layout.ResolveAddress(new MemoryStream(expected), "s." + name);
            Assert.IsTrue(address >= 0 && address < expected.Length, $"{mode} address {name}");
        }
    }
}
