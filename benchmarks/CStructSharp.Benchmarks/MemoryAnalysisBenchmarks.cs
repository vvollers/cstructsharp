namespace CStructSharp.Benchmarks;

using System.Text;
using BenchmarkDotNet.Attributes;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Measures independent memory workloads, keeping import cost separate from reused layout reads.</summary>
public class MemoryAnalysisBenchmarks
{
    private MemorySession session = null!;
    private MemoryRegion region = null!;
    private MemoryRegion cached = null!;
    private byte[] metadata = null!;
    private MemorySession largeSession = null!;
    private MemoryRegion largeRegion = null!;
    private MemorySession listSession = null!;
    private MemoryRegion listHead = null!;

    /// <summary>Builds a two-page mapping and warms a bounded cache with the same selected read.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var source = new ByteArrayMemorySource("physical", new byte[8192]);
        var mapping = new MappedMemorySource("virtual", [new(0xffff800000000000, new MemoryRegion(source, 0, 4096)), new(0xffff800000001000, new MemoryRegion(source, 4096, 4096)),]);
        this.region = new MemoryRegion(mapping, 0xffff800000000ffe, 4);
        this.cached = new MemoryRegion(new CachedMemorySource("cache", mapping), this.region.Address, 4);
        this.session = new MemorySession(new MemorySchema([new("u32", "u32", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),]));
        this.largeSession = new MemorySession(new MemorySchema([new("u32", "u32", MemoryTypeKind.Scalar, 4, scalarType: "uint32"), new("large", "large", MemoryTypeKind.Struct, 1_000_000, [new("value", "u32", 900_000),]),]));
        var sparse = new MappedMemorySource("sparse", [new(900_000, new MemoryRegion(source, 0, 4)),]);
        this.largeRegion = new MemoryRegion(sparse, 0, 1_000_000);
        this.listSession = new MemorySession(new MemorySchema([new("next", "next", MemoryTypeKind.Pointer, 8),]));
        byte[] listBytes = new byte[4097 * 8];
        for (int index = 0; index < 4097; index++)
        {
            ulong next = 0xffff800000000000 + (ulong)(((index + 1) % 4097) * 8);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(listBytes.AsSpan(index * 8, 8), next);
        }

        var listSource = new ByteArrayMemorySource("list image", listBytes);
        var listSpace = new MappedMemorySource("list space", [new(0xffff800000000000, new MemoryRegion(listSource, 0, listBytes.Length)),]);
        this.listHead = new MemoryRegion(listSpace, 0xffff800000000000, 8);
        _ = this.session.Read(this.cached, "u32");
        this.metadata = Encoding.UTF8.GetBytes("""
            {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},
            "user_types":{"record":{"kind":"struct","size":8,"fields":{"value":{"offset":4,"type":{"kind":"base","name":"u32"}}}}},"enums":{},"symbols":{}}
            """);
    }

    /// <summary>Reads a scalar whose bytes span two mappings and returns source accounting.</summary>
    [Benchmark]
    public MemoryAccessContext CrossPageRead()
    {
        var context = new MemoryAccessContext();
        _ = this.session.Read(this.region, "u32", context: context);
        return context;
    }

    /// <summary>Reads the same scalar from a warmed exact-range cache.</summary>
    [Benchmark]
    public MemoryAccessContext CachedRead()
    {
        var context = new MemoryAccessContext();
        _ = this.session.Read(this.cached, "u32", context: context);
        return context;
    }

    /// <summary>Reads four mapped bytes from a one-million-byte sparse record without materializing its gaps.</summary>
    [Benchmark]
    public MemoryAccessContext SparseSelectedRead()
    {
        var context = new MemoryAccessContext();
        _ = this.largeSession.Read(this.largeRegion, "large", "value", context);
        return context;
    }

    /// <summary>Walks 4,096 actual stored pointers through a high-address source using shared decoding and work budgets.</summary>
    [Benchmark]
    public MemoryWalkResult StoredPointerTraversal4096()
    {
        // The consumer interprets a decoded pointer; only the fixture builder encodes primitive bytes manually.
        return MemoryWalker.SentinelList(
            this.listHead,
            (node, context) =>
            {
                StoredPointer next = (StoredPointer)this.listSession.Read(node, "next", context: context)!;
                return new MemoryRegion(node.Source, next.Bits, 8);
            },
            maxNodes: 4096);
    }

    /// <summary>Imports a bounded profile and compiles its scalar and placement views.</summary>
    [Benchmark]
    public MetadataImportResult ImportIsf() => IsfMetadata.Import(this.metadata, "record");

    /// <summary>Plans and commits a scalar patch spanning two physical fragments.</summary>
    [Benchmark]
    public MemoryPatch MappedUpdate()
    {
        MemoryPatch patch = this.session.PlanUpdate(this.region, "u32", string.Empty, 7U);
        patch.Commit();
        return patch;
    }

    /// <summary>Traverses 32 synthetic nodes with source-scoped identity and a hard work limit.</summary>
    [Benchmark]
    public MemoryWalkResult BoundedTraversal()
    {
        // Sequential virtual addresses model a sentinel-linked fixture without primitive decoding overhead.
        return MemoryWalker.SentinelList(this.region, (node, _) => new MemoryRegion(node.Source, node.Address == this.region.Address + 32 ? this.region.Address : node.Address + 1, 1), maxNodes: 32);
    }
}
