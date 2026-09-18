namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     Inline unions inside structs (named and anonymous), inline structs inside unions, and the promotion of an
///     anonymous union's members into the containing struct - the NTFS/PE header shapes.
/// </summary>
[TestClass]
public class InlineUnionTests
{
    private const string FileNameLayout = """
                                          struct file_name {
                                              uint32 Attributes;
                                              union {
                                                  struct { uint16 EaSize; uint16 Reserved; };
                                                  uint32 ReparseTag;
                                              };
                                              uint8 NameLength;
                                          };
                                          """;

    private static readonly byte[] FileNameBytes = [0x20, 0, 0, 0, 0x34, 0x12, 0x78, 0x56, 3,];

    /// <summary>A named inline union reads as a <see cref="UnionValue"/> exactly like a named union type.</summary>
    [TestMethod]
    public void NamedInlineUnion_ReadsAsUnionValue()
    {
        var layout = new CStruct("struct root { uint8 tag; union { uint32 wide; uint16 narrow; } u; uint8 tail; };");
        Assert.AreEqual(6, layout.GetStructSizeInBytes("root"));

        dynamic value = layout.Parse(new byte[] { 1, 0x78, 0x56, 0x34, 0x12, 9, }.AsSpan(), "root");
        var union = (UnionValue)value.u;
        Assert.AreEqual(0x12345678U, (uint)union["wide"]!);
        Assert.AreEqual((ushort)0x5678, (ushort)union["narrow"]!);
        Assert.AreEqual((byte)9, (byte)value.tail);
        CollectionAssert.AreEqual(new byte[] { 0x78, 0x56, 0x34, 0x12, }, union.GetRawStorageArray());

        byte[] written = layout.Serialize("root", value);
        CollectionAssert.AreEqual(new byte[] { 1, 0x78, 0x56, 0x34, 0x12, 9, }, written);

        byte[] selected = layout.Serialize(
            "root",
            new Dictionary<string, object?> { ["tag"] = (byte)1, ["u"] = UnionValue.FromMember("u", "narrow", (ushort)0xBEEF), ["tail"] = (byte)9, });
        CollectionAssert.AreEqual(new byte[] { 1, 0xEF, 0xBE, 0, 0, 9, }, selected);
    }

    /// <summary>An anonymous union splices its members (and the members of its anonymous structs) into the struct.</summary>
    [TestMethod]
    public void AnonymousUnion_PromotesMembers()
    {
        var layout = new CStruct(FileNameLayout);
        Assert.AreEqual(9, layout.GetStructSizeInBytes("file_name"));

        dynamic value = layout.Parse(FileNameBytes.AsSpan(), "file_name");
        Assert.AreEqual(0x20U, (uint)value.Attributes);
        Assert.AreEqual((ushort)0x1234, (ushort)value.EaSize);
        Assert.AreEqual((ushort)0x5678, (ushort)value.Reserved);
        Assert.AreEqual(0x56781234U, (uint)value.ReparseTag);
        Assert.AreEqual((byte)3, (byte)value.NameLength);

        var members = (IDictionary<string, object?>)value;
        CollectionAssert.AreEqual(new[] { "Attributes", "EaSize", "Reserved", "ReparseTag", "NameLength", }, members.Keys.ToArray());
    }

    /// <summary>Every walker sees the promoted names: address resolution, selected reads, updates, and typed reads.</summary>
    [TestMethod]
    public void AnonymousUnion_MembersAreAddressable()
    {
        var layout = new CStruct(FileNameLayout);
        using var stream = new MemoryStream((byte[])FileNameBytes.Clone());

        Assert.AreEqual(4, layout.ResolveAddress(stream, "file_name.EaSize"));
        Assert.AreEqual(6, layout.ResolveAddress(stream, "file_name.Reserved"));
        Assert.AreEqual(4, layout.ResolveAddress(stream, "file_name.ReparseTag"));
        Assert.AreEqual(8, layout.ResolveAddress(stream, "file_name.NameLength"));
        Assert.AreEqual((ushort)0x5678, layout.ReadValue<ushort>(FileNameBytes.AsSpan(), "file_name.Reserved"));
        Assert.AreEqual(0x56781234U, layout.ReadValue<uint>(FileNameBytes.AsSpan(), "file_name.ReparseTag"));

        layout.Update(stream, "file_name.EaSize", (ushort)0xAAAA);
        CollectionAssert.AreEqual(new byte[] { 0x20, 0, 0, 0, 0xAA, 0xAA, 0x78, 0x56, 3, }, stream.ToArray());
        layout.Update(stream, "file_name.ReparseTag", 0x01020304U);
        CollectionAssert.AreEqual(new byte[] { 0x20, 0, 0, 0, 4, 3, 2, 1, 3, }, stream.ToArray());

        FileName typed = layout.ReadValue<FileName>(FileNameBytes.AsSpan(), "file_name");
        Assert.AreEqual((ushort)0x1234, typed.EaSize);
        Assert.AreEqual(0x56781234U, typed.ReparseTag);
    }

    /// <summary>Writing a promoted union picks the widest supplied member, so a parsed value round-trips byte for byte and a new value needs only one member.</summary>
    [TestMethod]
    public void AnonymousUnion_WritesThroughASuppliedMember()
    {
        var layout = new CStruct(FileNameLayout);
        dynamic parsed = layout.Parse(FileNameBytes.AsSpan(), "file_name");
        CollectionAssert.AreEqual(FileNameBytes, layout.Serialize("file_name", parsed));

        byte[] viaTag = layout.Serialize(
            "file_name",
            new Dictionary<string, object?> { ["Attributes"] = 1U, ["ReparseTag"] = 0x0A0B0C0DU, ["NameLength"] = (byte)7, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A, 7, }, viaTag);

        byte[] viaStruct = layout.Serialize(
            "file_name",
            new Dictionary<string, object?> { ["Attributes"] = 1U, ["EaSize"] = (ushort)0x0102, ["Reserved"] = (ushort)0, ["NameLength"] = (byte)7, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 0x02, 0x01, 0, 0, 7, }, viaStruct);

        Assert.Throws<CStructWriteException>(
            () => layout.Serialize("file_name", new Dictionary<string, object?> { ["Attributes"] = 1U, ["NameLength"] = (byte)7, }));
    }

    /// <summary>Debug records name promoted members without an empty path segment.</summary>
    [TestMethod]
    public void AnonymousUnion_DebugPathsSkipTheAnonymousLevel()
    {
        var layout = new CStruct(FileNameLayout);
        (dynamic _, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(new MemoryStream(FileNameBytes), "file_name");
        Assert.IsTrue(debug.Any(item => item.Path == "file_name.EaSize" && item.Start == 4 && item.End == 6));
        Assert.IsTrue(debug.Any(item => item.Path == "file_name.ReparseTag" && item.Start == 4 && item.End == 8));
        Assert.IsFalse(debug.Any(item => item.Path.Contains("..")));
    }

    /// <summary>A union may hold inline structs and unions, named or anonymous, and the layout rules follow the members.</summary>
    [TestMethod]
    public void Union_AcceptsInlineComposites()
    {
        var layout = new CStruct("union u { struct { uint32 a; uint32 b; } pair; struct { uint8 x; } ; union { uint16 s; uint8 c; } inner; uint64 q; };", aligned: true);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("u"));
        Assert.AreEqual(8, layout.GetStructAlignmentInBytes("u"));

        object? value = layout.ReadValue(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, }.AsSpan(), "u");
        var union = (UnionValue)value!;
        Assert.AreEqual(0x04030201U, (uint)((dynamic)union["pair"]!).a);
        Assert.AreEqual((byte)1, (byte)union["x"]!);
        Assert.AreEqual((ushort)0x0201, (ushort)((UnionValue)union["inner"]!)["s"]!);
        Assert.AreEqual(0x0807060504030201UL, (ulong)union["q"]!);
    }

    /// <summary>Promoted names collide with the struct's own names and with each other, as for anonymous structs.</summary>
    [TestMethod]
    public void AnonymousUnion_RejectsNameCollisions()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct r { uint8 a; union { uint8 a; uint16 b; }; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct r { union { uint8 a; }; union { uint16 a; }; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("union u { uint8 n; uint8 v[n]; };"));
        Assert.IsTrue(new CStruct("struct r { union { uint8 a; } first; union { uint16 a; } second; };").CStructElements["r"] is Struct);
    }

    /// <summary>The compiled model treats an inline union exactly as a named union declaration.</summary>
    [TestMethod]
    public void InlineUnion_HasUnionKind()
    {
        var layout = new CStruct("struct r { union { uint8 a; uint32 b; } u; };", aligned: true);
        Assert.AreEqual(4, layout.GetStructSizeInBytes("r"));
        Assert.AreEqual(4, layout.GetStructAlignmentInBytes("r"));
        Assert.AreEqual(2, new CStruct("struct r { uint8 t; union { uint8 a; uint8 b; }; };").GetStructSizeInBytes("r"));
    }

    private sealed class FileName
    {
        public uint Attributes { get; set; }

        public ushort EaSize { get; set; }

        public ushort Reserved { get; set; }

        public uint ReparseTag { get; set; }

        public byte NameLength { get; set; }
    }
}
