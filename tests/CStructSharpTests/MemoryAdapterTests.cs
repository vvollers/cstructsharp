namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Checks fixed Portable projection and bounded caching without host ABI assumptions.</summary>
[TestClass]
public class MemoryAdapterTests
{
    /// <summary>Compiled offsets, aliases, arrays, and high pointer bits survive the memory adapter.</summary>
    [TestMethod]
    public void Portable_UsesCompiledFieldsAndAliases()
    {
        var layout = new CStruct("struct Node { uint32 id; Node *next; }; typedef Node Alias; struct Root { Alias nodes[2]; };");
        MemorySchema schema = PortableMemorySchema.Create(layout, "Root");
        var session = new MemorySession(schema);
        var node = new Dictionary<string, object?> { ["id"] = 7U, ["next"] = new StoredPointer(ulong.MaxValue), };
        byte[] bytes = session.Serialize("Root", new Dictionary<string, object?> { ["nodes"] = new object[] { node, node, }, });
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(7U, session.Read(region, "Root", "nodes[1].id"));
        Assert.AreEqual(new StoredPointer(ulong.MaxValue), session.Read(region, "Root", "nodes[1].next.address"));
        Assert.Throws<ArgumentException>(() => session.Resolve(region, "Root", "nodes[1].next.address.value"));
    }

    /// <summary>Cache hits avoid physical reads, eviction obeys capacity, and changes invalidate cached values.</summary>
    [TestMethod]
    public void Cache_BoundsBytes_AndInvalidatesGeneration()
    {
        var source = new ByteArrayMemorySource("image", new byte[] { 1, 2, 3, 4, });
        var cached = new CachedMemorySource("snapshot", source, 2);
        var context = new MemoryAccessContext();
        byte[] bytes = new byte[2];
        cached.Read(0, bytes, context);
        cached.Read(0, bytes, context);
        Assert.AreEqual(2L, context.BytesRequested);
        cached.Read(2, bytes, context);
        cached.Read(0, bytes, context);
        Assert.AreEqual(6L, context.BytesRequested);
        source.Write(0, new byte[] { 9, }, new MemoryAccessContext());
        cached.Read(0, bytes, context);
        Assert.AreEqual(9, bytes[0]);
        Assert.Throws<OperationCanceledException>(() => cached.Read(0, bytes, new MemoryAccessContext(cancellationToken: new CancellationToken(true))));
    }

    /// <summary>All supported pointer widths round-trip their maximum bits in both byte orders.</summary>
    [TestMethod]
    public void PointerWidths_RejectTruncation_AndRoundTrip()
    {
        foreach (int width in new[] { 1, 2, 4, 8, })
        {
            foreach (bool littleEndian in new[] { false, true, })
            {
                var schema = new MemorySchema([new("p", "p", MemoryTypeKind.Pointer, width),], littleEndian);
                var session = new MemorySession(schema);
                ulong maximum = width == 8 ? ulong.MaxValue : (1UL << (width * 8)) - 1;
                var pointer = new StoredPointer(maximum, width);
                byte[] bytes = session.Serialize("p", pointer);
                Assert.IsTrue(bytes.All(value => value == 255));
                Assert.AreEqual(pointer, session.Read(new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, width), "p"));
                if (width < 8)
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => new StoredPointer(maximum + 1, width));
                }
            }
        }
    }
}
