namespace CStructSharp.Tests;

using CStructSharp.Memory;
using CStructSharp.Values;

/// <summary>Exercises explicit layout operations and normalized Portable aliases beyond contiguous scalar storage.</summary>
[TestClass]
public class MemoryExplicitLayoutTests
{
    /// <summary>Big-endian storage maps low bits to the last byte and preserves a separate first-byte field.</summary>
    [TestMethod]
    public void BigEndianBitSlices_UsePhysicalOverlapRules()
    {
        var schema = new MemorySchema([
            new("u8", "u8", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("u16", "u16", MemoryTypeKind.Scalar, 2, scalarType: "uint16>"),
            new("r", "r", MemoryTypeKind.Struct, 2, [new("byte", "u8", 0), new("bits", "u16", 0, 0, 4),]),
        ]);
        var source = new ByteArrayMemorySource("image", new byte[] { 0x12, 0x3f, });
        var session = new MemorySession(schema);
        var region = new MemoryRegion(source, 0, 2);
        Assert.AreEqual(15, session.Read(region, "r", "bits"));
        session.PlanUpdate(region, "r", "bits", 5).Commit();
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x35, }, source.ToArray());
    }

    /// <summary>Union construction requires a selected view and selected updates preserve the remaining storage.</summary>
    [TestMethod]
    public void Union_RequiresExplicitWriteSelection()
    {
        var schema = new MemorySchema([
            new("u8", "u8", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("u16", "u16", MemoryTypeKind.Scalar, 2, scalarType: "uint16"),
            new("u", "u", MemoryTypeKind.Union, 2, [new("small", "u8", 0), new("large", "u16", 0),]),
        ]);
        var session = new MemorySession(schema);
        CollectionAssert.AreEqual(new byte[] { 3, 0, }, session.Serialize("u", UnionValue.FromMember("u", "small", (byte)3)));
        Assert.Throws<ArgumentException>(() => session.Serialize("u", new Dictionary<string, object?> { ["small"] = (byte)3, }));
        var source = new ByteArrayMemorySource("image", new byte[] { 0x34, 0x12, });
        var region = new MemoryRegion(source, 0, 2);
        session.PlanUpdate(region, "u", "small", (byte)9).Commit();
        CollectionAssert.AreEqual(new byte[] { 9, 0x12, }, source.ToArray());
        var values = (UnionValue)session.Read(region, "u")!;
        Assert.AreEqual((ushort)0x1209, values["large"]);
    }

    /// <summary>Creates the two-byte union <c>u</c> with a one-byte <c>small</c> and a two-byte <c>large</c> view.</summary>
    /// <returns>A session over the union schema.</returns>
    private static MemorySession CreateUnionSession()
    {
        var schema = new MemorySchema([
            new("u8", "u8", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("u16", "u16", MemoryTypeKind.Scalar, 2, scalarType: "uint16"),
            new("u", "u", MemoryTypeKind.Union, 2, [new("small", "u8", 0), new("large", "u16", 0),]),
        ]);
        return new MemorySession(schema);
    }

    /// <summary>A whole-union read keeps the exact source bytes and every member view, and writes back unchanged.</summary>
    [TestMethod]
    public void UnionRead_ReturnsUnionValue_ThatRoundTripsExactly()
    {
        MemorySession session = CreateUnionSession();
        byte[] original = [0x34, 0x12,];
        var region = new MemoryRegion(new ByteArrayMemorySource("image", original), 0, 2);

        var union = (UnionValue)session.Read(region, "u")!;
        Assert.AreEqual("u", union.UnionName);
        Assert.IsFalse(union.HasSelection);
        CollectionAssert.AreEqual(original, union.RawStorage!.Value.ToArray());
        CollectionAssert.AreEqual(new[] { "small", "large", }, union.Members.Keys.ToArray());
        Assert.AreEqual((byte)0x34, union["small"]);
        Assert.AreEqual((ushort)0x1234, union["large"]);

        // Without a selected member the raw storage is written, so the read value reproduces the source exactly.
        CollectionAssert.AreEqual(original, session.Serialize("u", union));
        var target = new ByteArrayMemorySource("target", new byte[] { 0, 0, });
        session.PlanUpdate(new MemoryRegion(target, 0, 2), "u", string.Empty, union).Commit();
        CollectionAssert.AreEqual(original, target.ToArray());

        // Inspect returns the same shape as Read.
        Assert.IsInstanceOfType<UnionValue>(session.Inspect(region, "u").Value);
    }

    /// <summary>A whole-union read charges the raw storage once and each member view once against the byte budget.</summary>
    [TestMethod]
    public void UnionRead_ChargesRawStorageAndEachMemberView()
    {
        MemorySession session = CreateUnionSession();
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[] { 0x34, 0x12, }), 0, 2);

        // Two raw bytes, one byte for "small", and two bytes for "large".
        Assert.IsInstanceOfType<UnionValue>(session.Read(region, "u", context: new MemoryAccessContext(maxBytes: 5)));
        Assert.Throws<MemoryAccessException>(() => session.Read(region, "u", context: new MemoryAccessContext(maxBytes: 4)));
    }

    /// <summary>Selecting a member encodes it over zeroed storage, both when creating and when replacing a union.</summary>
    [TestMethod]
    public void UnionFromMember_ZeroesBytesBeyondTheMember()
    {
        MemorySession session = CreateUnionSession();
        CollectionAssert.AreEqual(new byte[] { 7, 0, }, session.Serialize("u", UnionValue.FromMember("u", "small", (byte)7)));

        // Replacing the whole union clears the old high byte instead of preserving it.
        var source = new ByteArrayMemorySource("image", new byte[] { 0xff, 0xff, });
        session.PlanUpdate(new MemoryRegion(source, 0, 2), "u", string.Empty, UnionValue.FromMember("u", "small", (byte)7)).Commit();
        CollectionAssert.AreEqual(new byte[] { 7, 0, }, source.ToArray());
    }

    /// <summary>A union value must name the union being written, and raw storage must match its size; other shapes are rejected.</summary>
    [TestMethod]
    public void UnionWrite_RejectsWrongNameSizeOrShape()
    {
        MemorySession session = CreateUnionSession();
        Assert.Throws<ArgumentException>(() => session.Serialize("u", UnionValue.FromMember("other", "small", (byte)7)));
        Assert.Throws<ArgumentException>(() => session.Serialize("u", UnionValue.FromRaw("U", new byte[] { 1, 2, })));
        Assert.Throws<ArgumentException>(() => session.Serialize("u", UnionValue.FromRaw("u", new byte[] { 1, 2, 3, })));
        Assert.Throws<ArgumentException>(() => session.Serialize("u", new byte[] { 1, 2, }));
        CollectionAssert.AreEqual(new byte[] { 1, 2, }, session.Serialize("u", UnionValue.FromRaw("u", new byte[] { 1, 2, })));
    }

    /// <summary>Arrays read in the core shapes: a typed primitive array for numeric elements, an object list otherwise.</summary>
    [TestMethod]
    public void ArrayRead_UsesPrimitiveArrayForNumbersAndListOtherwise()
    {
        var layout = new CStruct("struct Item { uint16 a; }; struct Root { uint32 words[2]; Item items[2]; uint32 none[0]; };");
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Root"));
        byte[] bytes = [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 4, 0,];
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);

        var words = (PrimitiveArray<uint>)session.Read(region, "Root", "words")!;
        CollectionAssert.AreEqual(new uint[] { 1, 2, }, words.ToArray());

        var items = (List<object?>)session.Read(region, "Root", "items")!;
        Assert.AreEqual(2, items.Count);
        Assert.AreEqual((ushort)4, ((StructValue)items[1]!)["a"]);

        // An empty numeric array still has its element type, even though no element was decoded.
        var none = (PrimitiveArray<uint>)session.Read(region, "Root", "none")!;
        Assert.AreEqual(0, none.Count);
    }

    /// <summary>Recursive aliases retain final fields, and target-width integer aliases keep their declared byte width.</summary>
    [TestMethod]
    public void Portable_RecursiveAliasesAndTargetWidth()
    {
        var layout = new CStruct("typedef struct Node Alias; struct Node { uintptr_t number; Alias *next; };", pointerSize: 4);
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Node"));
        byte[] bytes = session.Serialize("Node", new Dictionary<string, object?> { ["number"] = 42U, ["next"] = new StoredPointer(8, 4), });
        var image = new ByteArrayMemorySource("image", bytes.Concat(bytes).ToArray());
        Assert.AreEqual(42U, session.Read(new MemoryRegion(image, 0, 8), "Node", "next.value.number"));
    }

    /// <summary>Two virtual slices of the same physical byte cannot be committed as one conflicting patch.</summary>
    [TestMethod]
    public void Patch_RejectsAliasedPhysicalFragments()
    {
        var source = new ByteArrayMemorySource("image", new byte[1]);
        var mapped = new MappedMemorySource("virtual", [new(0, new MemoryRegion(source, 0, 1)), new(1, new MemoryRegion(source, 0, 1)),]);
        Assert.Throws<ArgumentException>(() => MemoryPatch.Create(new MemoryRegion(mapped, 0, 2), new byte[] { 1, 2, }));
        CollectionAssert.AreEqual(new byte[1], source.ToArray());
    }
}
