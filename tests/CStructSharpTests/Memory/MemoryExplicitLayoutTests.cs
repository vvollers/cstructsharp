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
        CollectionAssert.AreEqual(new byte[] { 3, 0, }, session.Serialize("u", new MemoryUnionSelection("small", (byte)3)));
        Assert.Throws<ArgumentException>(() => session.Serialize("u", new Dictionary<string, object?> { ["small"] = (byte)3, }));
        var source = new ByteArrayMemorySource("image", new byte[] { 0x34, 0x12, });
        var region = new MemoryRegion(source, 0, 2);
        session.PlanUpdate(region, "u", "small", (byte)9).Commit();
        CollectionAssert.AreEqual(new byte[] { 9, 0x12, }, source.ToArray());
        var values = (StructValue)session.Read(region, "u")!;
        Assert.AreEqual((ushort)0x1209, values["large"]);
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
