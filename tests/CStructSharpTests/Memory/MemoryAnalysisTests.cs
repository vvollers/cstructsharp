namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Independent byte fixtures for unsigned addressing, explicit metadata, and offline surgical patches.</summary>
[TestClass]
public class MemoryAnalysisTests
{
    private const ulong Kernel = 0xffff800000000000;

    /// <summary>Describes a record independently from its test bytes, including a signed slice and recursive pointer.</summary>
    private static MemorySchema CreateSchema(bool littleEndian = true)
    {
        return new MemorySchema(
        [
            new("u32", "unsigned", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("u8", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("ptr", "node pointer", MemoryTypeKind.Pointer, 8, elementTypeId: "node"),
            new("node", "node", MemoryTypeKind.Struct, 24, fields:
            [
                new("pid", "u32", 0),
                new("state", "u8", 8, 1, 3, signed: true),
                new("parent", "ptr", 16),
            ]),
        ],
        isLittleEndian: littleEndian);
    }

    /// <summary>A high-address value spanning discontiguous mappings decodes exactly and reports a missing second page.</summary>
    [TestMethod]
    public void MappedRead_CrossesPages_AndRejectsHoles()
    {
        byte[] image = new byte[0xa000];
        image[0x2ffe] = 0x78;
        image[0x2fff] = 0x56;
        image[0x9000] = 0x34;
        image[0x9001] = 0x12;
        var backing = new ByteArrayMemorySource("physical", image);
        var mapped = new MappedMemorySource(
        "kernel",
        [
            new(Kernel, new MemoryRegion(backing, 0x2000, 4096)),
            new(Kernel + 4096, new MemoryRegion(backing, 0x9000, 4096)),
        ]);
        var context = new MemoryAccessContext();
        var session = new MemorySession(CreateSchema());
        Assert.AreEqual(0x12345678U, session.Read(new MemoryRegion(mapped, Kernel + 4094, 4), "u32", context: context));
        Assert.AreEqual(4, context.BytesRequested);
        Assert.AreEqual(2, mapped.Describe(Kernel + 4094, 4).Count);

        var hole = new MappedMemorySource("hole", [new(Kernel, new MemoryRegion(backing, 0x2000, 4096)),]);
        MemoryAccessException error = Assert.Throws<MemoryAccessException>(() => session.Read(new MemoryRegion(hole, Kernel + 4094, 4), "u32"));
        Assert.AreEqual(MemoryFailure.Unmapped, error.Failure);
        Assert.AreEqual(Kernel + 4096, error.Address);
        Assert.AreEqual("u32", error.Path);
        Assert.AreSame(hole, error.LogicalRegion!.Source);
        Assert.AreEqual(Kernel + 4094, error.LogicalRegion.Address);
    }

    /// <summary>Pointer storage spans all 64 bits, remains unresolved by default, and survives serialization.</summary>
    [TestMethod]
    public void Pointer_UnsignedStorage_AndExplicitFollow()
    {
        var session = new MemorySession(CreateSchema());
        byte[] bytes = session.Serialize("node", new Dictionary<string, object?> { ["pid"] = 42U, ["state"] = -3, ["parent"] = new StoredPointer(Kernel), });
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0, 0, 0x80, 0xff, 0xff, }, bytes[16..24]);
        Assert.AreEqual(0x0a, bytes[8]);
        var source = new ByteArrayMemorySource("image", bytes);
        var space = new MappedMemorySource("kernel", [new(Kernel, new MemoryRegion(source, 0, 24)),]);
        var region = new MemoryRegion(space, Kernel, 24);
        Assert.AreEqual(new StoredPointer(Kernel), session.Read(region, "node", "parent"));
        Assert.AreEqual(42U, session.Read(region, "node", "parent.value.pid"));
        Assert.AreEqual(-3L, session.Read(region, "node", "state"));
        Assert.AreEqual(ulong.MaxValue, ((StoredPointer)session.Read(new MemoryRegion(new ByteArrayMemorySource("max", Enumerable.Repeat((byte)255, 8).ToArray()), 0, 8), "ptr")!).Bits);
    }

    /// <summary>Signed updates preserve neighboring bits and padding, reject overflow, and detect stale plans.</summary>
    [TestMethod]
    public void Patch_PreservesBits_AndRejectsStaleSource()
    {
        var session = new MemorySession(CreateSchema());
        byte[] original = Enumerable.Repeat((byte)0xa5, 24).ToArray();
        var source = new ByteArrayMemorySource("image", original);
        var region = new MemoryRegion(source, 0, 24);
        MemoryPatch patch = session.PlanUpdate(region, "node", "state", 2);
        CollectionAssert.AreEqual(original, source.ToArray());
        patch.Commit();
        byte[] expected = (byte[])original.Clone();
        expected[8] = 0xa5;
        CollectionAssert.AreEqual(expected, source.ToArray());
        session.PlanUpdate(region, "node", "state", -1).Commit();
        expected[8] = 0xaf;
        CollectionAssert.AreEqual(expected, source.ToArray());
        Assert.Throws<MemoryAccessException>(() => patch.Commit());
        Assert.Throws<ArgumentOutOfRangeException>(() => session.PlanUpdate(region, "node", "state", 4));
    }

    /// <summary>Overlay commits change only the overlay and never create bytes in holes.</summary>
    [TestMethod]
    public void Overlay_IsCopyOnWrite_AndBudgeted()
    {
        var source = new ByteArrayMemorySource("image", new byte[24]);
        var overlay = new OverlayMemorySource("edit", source, maxChangedBytes: 4);
        var session = new MemorySession(CreateSchema());
        var region = new MemoryRegion(overlay, 0, 24);
        session.PlanUpdate(region, "node", "pid", 123U).Commit();
        Assert.AreEqual(123U, session.Read(region, "node", "pid"));
        CollectionAssert.AreEqual(new byte[24], source.ToArray());
        Assert.Throws<MemoryAccessException>(() => overlay.Write(24, new byte[] { 1, }, new MemoryAccessContext()));
        Assert.Throws<MemoryPatchCommitException>(() => session.PlanUpdate(region, "node", "state", 1).Commit());
    }

    /// <summary>Cancellation, address overflow, overlap, and limited physical work fail explicitly.</summary>
    [TestMethod]
    public void Bounds_AndCancellation_AreEnforced()
    {
        var source = new ByteArrayMemorySource("image", new byte[24]);
        var session = new MemorySession(CreateSchema());
        var region = new MemoryRegion(source, 0, 24);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRegion(source, ulong.MaxValue, 2));
        Assert.Throws<ArgumentException>(() => new MappedMemorySource("bad", [new(1, region), new(3, region),]));
        Assert.Throws<MemoryAccessException>(() => session.Read(region, "node", "pid", new MemoryAccessContext(maxBytes: 3)));
        Assert.Throws<OperationCanceledException>(() => session.Read(region, "node", "pid", new MemoryAccessContext(cancellationToken: new CancellationToken(true))));
        Assert.Throws<ArgumentException>(() => new MemorySchema([new("bad", "bad", MemoryTypeKind.Struct, 1, fields: [new("self", "bad", 0),]),]));
    }

    /// <summary>Finite regions give EOF arrays a real boundary and preserve ownership of the caller's stream.</summary>
    [TestMethod]
    public void Region_UsesLocalPositions_AndFiniteEof()
    {
        using var original = new MemoryStream(new byte[] { 9, 1, 0, 2, 0, 9, });
        original.Position = 5;
        var source = new StreamMemorySource("file", original);
        using (Stream view = new MemoryRegion(source, 1, 4).OpenRead())
        {
            var layout = new CStruct("struct root { uint16 values[EOF]; };");
            dynamic result = layout.Parse(view, "root");
            Assert.AreEqual((ushort)2, (ushort)result.values[1]);
        }

        Assert.AreEqual(5, original.Position);
        Assert.IsTrue(original.CanRead);
    }
}
