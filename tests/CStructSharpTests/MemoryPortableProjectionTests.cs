namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Tests the fixed Portable adapter's aliases, anonymous storage, and deliberate runtime-layout boundary.</summary>
[TestClass]
public class MemoryPortableProjectionTests
{
    /// <summary>A fixed empty array retains its element metadata while reading and creating no element bytes.</summary>
    [TestMethod]
    public void Portable_ProjectsEmptyFixedArray()
    {
        var session = new MemorySession(PortableMemorySchema.Create(new CStruct("struct Root { uint16 empty[0]; uint8 tail; };"), "Root"));
        byte[] bytes = session.Serialize("Root", new Dictionary<string, object?> { ["empty"] = Array.Empty<ushort>(), ["tail"] = (byte)9, });
        CollectionAssert.AreEqual(new byte[] { 9, }, bytes);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, 1);
        Assert.AreEqual(0, ((object?[])session.Read(region, "Root", "empty")!).Length);
        Assert.AreEqual((byte)9, session.Read(region, "Root", "tail"));
    }

    /// <summary>Pointer typedef roots, array typedefs, nested pointer levels, and opaque values retain exact storage.</summary>
    [TestMethod]
    public void Portable_ProjectsPointerAndArrayAliases()
    {
        var layout = new CStruct("typedef Node *NodePtr; struct Node { uint32 value; NodePtr next; }; typedef uint16 Matrix[2][3]; typedef void *Opaque; struct Root { Matrix values; Node **indirect; Opaque raw; };", pointerSize: 4);
        var rootPointer = new MemorySession(PortableMemorySchema.Create(layout, "NodePtr"));
        Assert.AreEqual(MemoryTypeKind.Pointer, rootPointer.Schema.GetType("NodePtr").Kind);
        CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 255, }, rootPointer.Serialize("NodePtr", new StoredPointer(uint.MaxValue, 4)));
        Assert.AreEqual("Portable typedef", rootPointer.Schema.GetType("NodePtr").Provenance);

        var session = new MemorySession(PortableMemorySchema.Create(layout, "Root"));
        byte[] bytes = session.Serialize("Root", new Dictionary<string, object?>
        {
            ["values"] = new object[] { new ushort[] { 1, 2, 3, }, new ushort[] { 4, 5, 6, }, },
            ["indirect"] = new StoredPointer(20, 4),
            ["raw"] = new StoredPointer(uint.MaxValue, 4),
        });
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual((ushort)6, session.Read(region, "Root", "values[1][2]"));
        Assert.AreEqual(new StoredPointer(uint.MaxValue, 4), session.Read(region, "Root", "raw"));
        MemoryTypeDefinition indirect = session.Resolve(region, "Root", "indirect").Type;
        Assert.AreEqual(MemoryTypeKind.Pointer, session.Schema.GetType(indirect.ElementTypeId!).Kind);
        Assert.IsNull(session.Resolve(region, "Root", "raw").Type.ElementTypeId);
    }

    /// <summary>Anonymous unions remain overlapping views and bit padding never becomes a writable named member.</summary>
    [TestMethod]
    public void Portable_ProjectsPromotedUnionsAndBitfields()
    {
        var layout = new CStruct("struct Root { uint8 :2; uint8 flag:3; union { uint16 word; uint8 low; }; };", compilationOptions: new CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst, });
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Root"));
        var source = new ByteArrayMemorySource("image", new byte[] { 0x28, 0x34, 0x12, });
        var region = new MemoryRegion(source, 0, 3);
        Assert.AreEqual(5, session.Read(region, "Root", "flag"));
        Assert.AreEqual((ushort)0x1234, session.Read(region, "Root", "word"));
        Assert.AreEqual((byte)0x34, session.Read(region, "Root", "low"));
        session.PlanUpdate(region, "Root", "low", (byte)7).Commit();
        CollectionAssert.AreEqual(new byte[] { 0x28, 7, 0x12, }, source.ToArray());
    }

    /// <summary>Primitive roots can be probed, but layouts requiring runtime information are explicitly rejected.</summary>
    [TestMethod]
    public void Portable_RejectsDynamicLayoutsAndInvalidInputs()
    {
        var fixedLayout = new CStruct("struct Root { uint8 value; };");
        var scalar = new MemorySession(PortableMemorySchema.Create(fixedLayout, "uint16"));
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, scalar.Serialize("uint16", (ushort)0x1234));
        Assert.Throws<ArgumentNullException>(() => PortableMemorySchema.Create(null!, "Root"));
        Assert.Throws<ArgumentException>(() => PortableMemorySchema.Create(fixedLayout, string.Empty));
        Assert.Throws<ArgumentException>(() => PortableMemorySchema.Create(new CStruct("struct Root { uint8 count; uint8 values[count]; };"), "Root"));
        Assert.Throws<ArgumentException>(() => PortableMemorySchema.Create(new CStruct("struct Root { uint8 count; if (count) { uint8 value; } };"), "Root"));
        MemorySchema incomplete = PortableMemorySchema.Create(fixedLayout, "void");
        Assert.AreEqual(MemoryTypeKind.Incomplete, incomplete.GetType("void").Kind);
    }
}
