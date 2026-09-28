namespace CStructSharp.Tests;

using System.Numerics;
using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     Inline unions inside structs (named and anonymous), inline structs inside unions, and the promotion of an
///     anonymous union's members into the containing struct - the NTFS/PE header shapes - and a union's size when its
///     largest member does not end on its alignment.
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

    /// <summary>
    ///     Updating a struct that contains an anonymous union follows <see cref="UpdateOptions.ClearUnionStorage"/> as a
    ///     named union does: kept storage keeps the bytes the written member does not cover, cleared storage zeroes them.
    /// </summary>
    /// <param name="clear">The update's <see cref="UpdateOptions.ClearUnionStorage"/>.</param>
    /// <param name="expected">The six bytes after the update: <c>a</c>, the four-byte union, <c>b</c>.</param>
    [TestMethod]
    [DataRow(false, "0102FFFFFF03")]
    [DataRow(true, "010200000003")]
    public void AnonymousUnion_UpdateFollowsClearUnionStorage(bool clear, string expected)
    {
        var promoted = new CStruct("struct inner { uint8 a; union { uint32 wide; uint8 narrow; }; uint8 b; }; struct root { inner i; };");
        var named = new CStruct("struct inner { uint8 a; union u { uint32 wide; uint8 narrow; } x; uint8 b; }; struct root { inner i; };");
        var options = new UpdateOptions { ClearUnionStorage = clear, };

        byte[] promotedData = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,];
        promoted.Update(promotedData, "root.i", new StructValue { ["a"] = (byte)1, ["narrow"] = (byte)2, ["b"] = (byte)3, }, options: options);
        byte[] namedData = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,];
        named.Update(namedData, "root.i", new StructValue { ["a"] = (byte)1, ["x"] = UnionValue.FromMember("u", "narrow", (byte)2), ["b"] = (byte)3, }, options: options);

        Assert.AreEqual(expected, Convert.ToHexString(promotedData));
        Assert.AreEqual(expected, Convert.ToHexString(namedData));
    }

    /// <summary>Only aligned layouts round the union's three-byte largest member up to a two-byte boundary.</summary>
    /// <param name="aligned">Whether natural alignment and tail padding are enabled.</param>
    /// <param name="expectedSize">The resulting union extent in bytes.</param>
    [TestMethod]
    [DataRow(false, 3)]
    [DataRow(true, 4)]
    public void UnionTail_RoundsTheLargestMemberOnlyWhenAligned(bool aligned, int expectedSize)
    {
        var layout = new CStruct("union choice { uint8 bytes[3]; uint16 number; };", aligned: aligned);

        Assert.AreEqual(expectedSize, layout.GetStructSizeInBytes("choice"));
        Assert.AreEqual(expectedSize, layout.CompiledModel.Symbols["choice"].Symbol.FixedSize);
    }

    /// <summary>
    ///     A pointer leads to two bytes representing 0x1234.
    /// </summary>
    /// <remarks>
    ///     Both union views must begin at that same target address, with small reading its first byte and large reading
    ///     both. The fixture checks byte order, alignment, raw storage, and debug positions so a union is not
    ///     accidentally read like sequential struct fields.
    /// </remarks>
    /// <param name="aligned">Whether the layout applies portable field alignment.</param>
    /// <param name="isLittleEndian">Whether multi-byte union members store their least-significant byte first.</param>
    [TestMethod]
    [DynamicData(nameof(RegressionTestSupport.AlignmentAndEndianMatrix), typeof(RegressionTestSupport))]
    public void ParseStream_PointerToUnion_RewindsEveryMemberToTargetAddress(
        bool aligned,
        bool isLittleEndian)
    {
        var targetBytes = new byte[2];
        RegressionTestSupport.WriteUnsigned(targetBytes, 0, 2, 0x1234, isLittleEndian);
        using PointerFixture fixture = RegressionTestSupport.CreatePointerFixture(
            "union choice { uint8 small; uint16 large; };",
            "choice",
            targetBytes,
            isLittleEndian,
            aligned: aligned);

        dynamic parsed = fixture.Layout.Parse(fixture.Stream, "root");
        var pointer = (Pointer)parsed.target;
        var union = (UnionValue)pointer.Value!;
        IReadOnlyDictionary<string, object?> members = union.Members;

        Assert.IsFalse(union.HasSelection);
        CollectionAssert.AreEqual(targetBytes, union.RawStorage!.Value.ToArray());
        Assert.AreEqual(targetBytes[0], (byte)members["small"]!);
        Assert.AreEqual((ushort)0x1234, (ushort)members["large"]!);
        RegressionTestSupport.AssertPositionRestored(fixture.Stream, 1);

        fixture.Stream.Position = 0;
        (_, IReadOnlyList<DebugData> debug) = fixture.Layout.ParseWithDebug(fixture.Stream, "root");
        Assert.IsTrue(
            debug.Count(item => item.Start == fixture.TargetAddress) >= 2,
            "Every pointer-target union member must start at the overlapping target address.");
        RegressionTestSupport.AssertPositionRestored(fixture.Stream, 1);

        fixture.Stream.Position = 0;
        Assert.AreEqual(
            fixture.TargetAddress,
            fixture.Layout.ResolveAddress(fixture.Stream, "root.target.value.large"));
        RegressionTestSupport.AssertPositionRestored(fixture.Stream, 0);
    }

    /// <summary>A mapped class for the NTFS <c>file_name</c> shape, whose properties bind to promoted union and struct members.</summary>
    internal sealed class FileName : ICStructMapped<FileName>
    {
        /// <summary>Gets or sets the <c>uint32 Attributes</c> field at byte offset 0.</summary>
        public uint Attributes { get; set; }

        /// <summary>Gets or sets <c>EaSize</c>: the low 16 bits of the union at byte offset 4.</summary>
        public ushort EaSize { get; set; }

        /// <summary>Gets or sets <c>Reserved</c>: the high 16 bits of the union at byte offset 4.</summary>
        public ushort Reserved { get; set; }

        /// <summary>Gets or sets <c>ReparseTag</c>: all four union bytes at byte offset 4, over both halves.</summary>
        public uint ReparseTag { get; set; }

        /// <summary>Gets or sets the <c>uint8 NameLength</c> field at byte offset 8.</summary>
        public byte NameLength { get; set; }

        /// <summary>Anonymous union and struct members are promoted, so every leaf is addressable by its own name.</summary>
        /// <param name="source">The parsed <c>file_name</c> record.</param>
        /// <returns>The mapped class with every promoted leaf copied.</returns>
        public static FileName ReadFrom(StructValue source)
        {
            return new FileName
            {
                Attributes = source.Get<uint>("Attributes"),
                EaSize = source.Get<ushort>("EaSize"),
                Reserved = source.Get<ushort>("Reserved"),
                ReparseTag = source.Get<uint>("ReparseTag"),
                NameLength = source.Get<byte>("NameLength"),
            };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(FileName value, StructValue target)
        {
            target["Attributes"] = value.Attributes;
            target["EaSize"] = value.EaSize;
            target["Reserved"] = value.Reserved;
            target["ReparseTag"] = value.ReparseTag;
            target["NameLength"] = value.NameLength;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<FileName>();
        }
    }
}
