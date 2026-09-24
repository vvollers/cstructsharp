namespace CStructSharp.Docs.Examples;

using System.Text;
using global::CStructSharp.Memory;
using global::CStructSharp.Memory.Metadata;

/// <summary>Executable memory-guide examples; each assertion checks the result described in the corresponding article.</summary>
internal static class MemoryTutorialExamples
{
    /// <summary>Checks mapping, metadata, pointer interpretation, bounded work, and offline updates using independent toy bytes.</summary>
    internal static void Run()
    {
        CrossPage();
        ExplicitLayout();
        ImportIsf();
        ImportBtf();
        DescribeBtf();
        RelativePointer();
        SentinelList();
        OfflinePatch();
        UnionCreation();
        CacheAndFailures();
    }

    /// <summary>Separates a field's local offset, unsigned logical address, and two physical image offsets.</summary>
    private static void CrossPage()
    {
        #region memory-cross-page
        // The schema comes from an ordinary Portable declaration; the session applies it to any source.
        var layout = new CStruct("struct Record { uint32 value; };");
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Record"));

        // The "capture file": six bytes whose offsets 2 and 3 belong to nothing we read.
        var image = new ByteArrayMemorySource("image", new byte[] { 0x78, 0x56, 0, 0, 0x34, 0x12 });

        // The record ends one page and starts the next, so two mappings describe its four logical bytes.
        const ulong start = 0xffff800000000ffe;
        var mapped = new MappedMemorySource("process", new[]
        {
            new MemoryMapping(start, new MemoryRegion(image, 0, 2)),
            new MemoryMapping(start + 2, new MemoryRegion(image, 4, 2)),
        });
        var region = new MemoryRegion(mapped, start, 4);
        MemoryInspection inspection = session.Inspect(region, "Record", "value");
        Require((uint)inspection.Value! == 0x12345678U, "Cross-page value");
        Require(inspection.BackingRegions.Count == 2, "Two backing fragments");
        Require(inspection.BackingRegions[1].Address == 4, "Second physical offset");

        // The same region can feed the core stream reader; position 0 means the region's start address.
        using Stream view = region.OpenRead();
        Require(view.Position == 0 && view.Length == 4, "Local stream coordinates");
        Require((uint)layout.ReadValue(view, "Record.value")! == 0x12345678U, "Core stream bridge");
        #endregion
    }

    /// <summary>Describes authoritative native placement and verifies sign extension and preservation of neighboring bits.</summary>
    private static void ExplicitLayout()
    {
        #region memory-explicit-layout
        // Every offset and size is stated, as metadata would state it; nothing is computed from declaration order.
        var schema = new MemorySchema(new[]
        {
            new MemoryTypeDefinition("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new MemoryTypeDefinition("word", "word", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new MemoryTypeDefinition("record", "Record", MemoryTypeKind.Struct, 8, fields: new[]
            {
                // "state" is bits 1..3 of the byte at offset 0, read as a signed two's-complement number.
                new MemoryField("state", "byte", 0, bitOffset: 1, bitWidth: 3, signed: true),
                new MemoryField("count", "word", 4),
            }),
        });
        var session = new MemorySession(schema);

        // Byte 0 is 1000 1111: bits 1..3 are 111, which is -1 in three-bit two's complement.
        var source = new ByteArrayMemorySource("record", new byte[] { 0x8f, 0xaa, 0xbb, 0xcc, 42, 0, 0, 0 });
        var region = new MemoryRegion(source, 0, 8);
        Require((long)session.Read(region, "record", "state")! == -1, "Signed three-bit value");
        Require((uint)session.Read(region, "record", "count")! == 42, "Member at offset four");

        // Writing -2 (bits 110) touches only bits 1..3; bit 0, the high nibble, and the padding survive.
        session.PlanUpdate(region, "record", "state", -2L).Commit();
        Require(source.ToArray().SequenceEqual(new byte[] { 0x8d, 0xaa, 0xbb, 0xcc, 42, 0, 0, 0 }), "Neighbor preservation");
        #endregion
    }

    /// <summary>Imports a complete synthetic ISF type description and reads a value using the returned root identity.</summary>
    private static void ImportIsf()
    {
        #region memory-isf
        const string json = """
            { "metadata": { "format": "6.2.0" },
              "base_types": { "u32": { "kind": "int", "size": 4, "signed": false, "endian": "little" } },
              "user_types": { "counter": { "kind": "struct", "size": 8, "fields": {
                "value": { "offset": 4, "type": { "kind": "base", "name": "u32" } }
              } } }, "enums": {}, "symbols": {} }
            """;
        // The importer follows "counter" and the types it references; RootTypeId is the importer's ID for it.
        MetadataImportResult imported = IsfMetadata.Import(Encoding.UTF8.GetBytes(json), "counter", pointerSize: 8);
        var session = new MemorySession(imported.Schema);

        // Offsets 0..3 are outside the selected member, so their contents cannot influence the result.
        var source = new ByteArrayMemorySource("capture", new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 7, 0, 0, 0 });
        object? value = session.Read(new MemoryRegion(source, 0, 8), imported.RootTypeId, "value");
        Require((uint)value! == 7, "Imported explicit offset");
        Require(imported.Schema.GetField(imported.RootTypeId, "value").Offset == 4, "Metadata inspection");
        #endregion
    }

    /// <summary>Imports a small independently encoded BTF table with one integer and one padded record.</summary>
    private static void ImportBtf()
    {
        #region memory-btf
        // BTF header: little-endian magic, v1, 24-byte header, 40-byte type table, 18-byte string table.
        byte[] blob = Convert.FromHexString(
            "9FEB01001800000000000000280000002800000012000000" +
            // Type 1: name offset 1 (u32), INT kind, size 4, unsigned 32-bit encoding.
            "01000000000000010400000020000000" +
            // Type 2: name offset 5 (record), STRUCT kind with one member, size 8.
            "050000000100000408000000" +
            // Member: name offset 12 (value), type 1, bit offset 32 (byte offset 4).
            "0C0000000100000020000000" +
            // UTF-8 strings: empty, u32, record, value; each is zero-terminated.
            "00753332007265636F72640076616C756500");
        // Parsing indexes the table; FindType demands a unique name; Import compiles the reachable graph.
        var metadata = new BtfMetadata(blob);
        uint rootId = metadata.FindType("record");
        MetadataImportResult imported = metadata.Import(rootId, pointerSize: 8);
        var session = new MemorySession(imported.Schema);
        var image = new ByteArrayMemorySource("BTF record", new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 9, 0, 0, 0 });
        Require((uint)session.Read(new MemoryRegion(image, 0, 8), imported.RootTypeId, "value")! == 9, "BTF member placement");
        Require(imported.Schema.GetField(imported.RootTypeId, "value").Offset == 4, "BTF bit-to-byte offset");
        #endregion
    }

    /// <summary>Describes the same BTF struct's own members without importing anything it refers to.</summary>
    private static void DescribeBtf()
    {
        #region memory-btf-describe
        // The same blob ImportBtf uses: one integer type and one struct with a single member.
        byte[] blob = Convert.FromHexString(
            "9FEB01001800000000000000280000002800000012000000" +
            "01000000000000010400000020000000" +
            "050000000100000408000000" +
            "0C0000000100000020000000" +
            "00753332007265636F72640076616C756500");
        var metadata = new BtfMetadata(blob);
        uint rootId = metadata.FindType("record");

        // Unlike Import, Describe never follows a member into its own type - it reports the declared ID as-is,
        // so this succeeds even if "value"'s own type would fail to import.
        BtfTypeDescription description = metadata.Describe(rootId);
        Require(description.Kind == BtfKind.Struct, "Describe reports the struct kind");
        Require(description.Members.Count == 1, "Describe reports one member");
        BtfMemberDescription value = description.Members[0];
        Require(value.Name == "value" && value.Offset == 4, "Describe reports the same placement Import would");
        Require(value.BitOffset is null && value.BitWidth is null, "Describe reports a whole-value (non-bitfield) member");
        #endregion
    }

    /// <summary>Interprets a stored displacement relative to its containing object without changing the encoded pointer.</summary>
    private static void RelativePointer()
    {
        #region memory-relative-pointer
        var layout = new CStruct("struct Node { uint32 value; Node *next; };");
        MemorySchema schema = PortableMemorySchema.Create(layout, "Node");
        var source = new ByteArrayMemorySource("relative image", new byte[64]);
        // This format stores a byte displacement from the start of the containing Node, so the resolver
        // adds the stored bits to the container's address. The stored bits themselves are never changed.
        var session = new MemorySession(schema, request => new MemoryRegion(
            request.Container.Source,
            checked(request.Container.Address + request.Pointer.Bits),
            request.TargetSize));
        int size = schema.GetType("Node").Size;

        // Serialize creates each record's bytes; placing them at 8 and 24 is the application's decision.
        byte[] first = session.Serialize("Node", new Dictionary<string, object?>
        {
            ["value"] = 10U,
            ["next"] = new StoredPointer(16),
        });
        byte[] second = session.Serialize("Node", new Dictionary<string, object?>
        {
            ["value"] = 20U,
            ["next"] = new StoredPointer(0),
        });
        source.Write(8, first, new MemoryAccessContext());
        source.Write(24, second, new MemoryAccessContext());
        var root = new MemoryRegion(source, 8, size);

        // "next" is the stored bits; "next.value" follows them; "next.value.value" is the target's member.
        Require(((StoredPointer)session.Read(root, "Node", "next")!).Bits == 16, "Stored displacement");
        Require((uint)session.Read(root, "Node", "next.value.value")! == 20, "Resolved target member");
        Require(session.Resolve(root, "Node", "next.value").Region.Address == 24, "Resolved address");
        #endregion
    }

    /// <summary>Builds a circular list whose sentinel is excluded from the returned data nodes.</summary>
    private static void SentinelList()
    {
        #region memory-sentinel
        var layout = new CStruct("struct Link { Link *next; };");
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Link"));
        var source = new ByteArrayMemorySource("list", new byte[32]);
        // The list is head at 8 -> data at 16 -> data at 24 -> head at 8.
        foreach ((ulong address, ulong next) in new[] { (8UL, 16UL), (16UL, 24UL), (24UL, 8UL) })
        {
            byte[] bytes = session.Serialize("Link", new Dictionary<string, object?> { ["next"] = new StoredPointer(next) });
            source.Write(address, bytes, new MemoryAccessContext());
        }

        var budget = new MemoryAccessContext(maxBytes: 256, maxRequests: 100);

        // The callback reads one link and returns the next node's region. It must pass the walk's context
        // to the session so every link read spends the same budget; a fresh context would bypass the limit.
        MemoryWalkResult result = MemoryWalker.SentinelList(new MemoryRegion(source, 8, 8), (node, context) =>
        {
            var next = (StoredPointer)session.Read(node, "Link", "next", context)!;
            return new MemoryRegion(node.Source, next.Bits, 8);
        }, maxNodes: 4, context: budget);
        Require(result.Stop == MemoryWalkStop.Sentinel && result.Nodes.Count == 2, "Sentinel completion");
        Require(result.Nodes[0].Address == 16 && result.Nodes[1].Address == 24, "List order");

        // An intrusive link at 0x1028 whose member offset is 0x28 belongs to the record starting at 0x1000.
        Require(MemoryWalker.ContainingRecord(0x1028, 0x28) == 0x1000, "Embedded member address");
        #endregion
    }

    /// <summary>Stages a cross-mapping update on an overlay and exports the finite edited view.</summary>
    private static void OfflinePatch()
    {
        #region memory-offline-patch
        var session = new MemorySession(PortableMemorySchema.Create(new CStruct("struct Record { uint32 value; };"), "Record"));

        // Stack: mapping (logical addresses) over overlay (changed bytes) over the untouched original image.
        var original = new ByteArrayMemorySource("original", new byte[] { 0x78, 0x56, 0xaa, 0xbb, 0x34, 0x12 });
        var overlay = new OverlayMemorySource("edits", original, maxChangedBytes: 4);
        var logical = new MappedMemorySource("virtual copy", new[]
        {
            new MemoryMapping(0x1000, new MemoryRegion(overlay, 0, 2)),
            new MemoryMapping(0x1002, new MemoryRegion(overlay, 4, 2)),
        });
        var record = new MemoryRegion(logical, 0x1000, 4);

        // Planning resolves the mappings into physical fragments and stages bytes; nothing is written yet.
        MemoryPatch patch = session.PlanUpdate(record, "Record", "value", 0x11223344U);
        Require(patch.Fragments.Count == 2, "Physical fragment preview");
        Require((uint)session.Read(record, "Record", "value")! == 0x12345678U, "Planning is read-only");

        // Commit writes into the overlay; the original image below it keeps its bytes.
        patch.Commit();
        Require((uint)session.Read(record, "Record", "value")! == 0x11223344U, "Committed value");
        Require(original.ToArray()[0] == 0x78, "Original remains unchanged");

        // Export the overlay's own coordinates: the edited bytes with the untouched gap bytes between them.
        using Stream edited = new MemoryRegion(overlay, 0, original.Length).OpenRead();
        using var output = new MemoryStream();
        edited.CopyTo(output);
        Require(output.ToArray().SequenceEqual(new byte[] { 0x44, 0x33, 0xaa, 0xbb, 0x22, 0x11 }), "Export preserves image gaps");
        #endregion
    }

    /// <summary>Makes the chosen interpretation of overlapping union bytes explicit during creation.</summary>
    private static void UnionCreation()
    {
        #region memory-union
        var layout = new CStruct("union Value { uint32 number; uint8 bytes[4]; };");
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Value"));

        // A union write must name the member being encoded; a dictionary of overlapping members is ambiguous.
        byte[] bytes = session.Serialize("Value", new MemoryUnionSelection("number", 0x12345678U));
        Require(bytes.SequenceEqual(new byte[] { 0x78, 0x56, 0x34, 0x12 }), "Chosen union interpretation");

        // Alternatively, supply the union's exact bytes when the interpretation is unknown or irrelevant.
        byte[] raw = session.Serialize("Value", new byte[] { 1, 2, 3, 4 });
        Require(raw.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "Exact raw union storage");
        #endregion
    }

    /// <summary>Checks backing-byte accounting on cache hits and distinguishes missing mappings from cancellation.</summary>
    private static void CacheAndFailures()
    {
        #region memory-cache
        var session = new MemorySession(PortableMemorySchema.Create(new CStruct("struct Record { uint32 value; };"), "Record"));
        var source = new ByteArrayMemorySource("snapshot", new byte[] { 42, 0, 0, 0 });
        var cache = new CachedMemorySource("cached", source, capacity: 16);
        var region = new MemoryRegion(cache, 0, 4);

        // Two separate contexts make the cost of each read visible: the first misses, the second hits.
        var cold = new MemoryAccessContext(maxBytes: 16, maxRequests: 20);
        var warm = new MemoryAccessContext(maxBytes: 16, maxRequests: 20);
        Require((uint)session.Read(region, "Record", "value", cold)! == 42, "Cold result");
        Require((uint)session.Read(region, "Record", "value", warm)! == 42, "Warm result");
        Require(cold.BytesRequested == 4 && warm.BytesRequested == 0, "Backing bytes avoided on hit");
        Require(warm.Requests > 0, "Cache hits still consume work");
        // Writing to the source advances its generation; the cache notices and discards its entries.
        source.Write(0, new byte[] { 7, 0, 0, 0 }, new MemoryAccessContext());
        Require((uint)session.Read(region, "Record", "value")! == 7, "Generation invalidates cache");
        #endregion

        #region memory-failure
        // An empty mapping table means no address is available; the failure names the address and the path.
        var missing = new MemoryRegion(new MappedMemorySource("missing", Array.Empty<MemoryMapping>()), 0x1000, 4);
        try
        {
            session.Read(missing, "Record", "value");
            throw new InvalidOperationException("Expected an unmapped address.");
        }
        catch (MemoryAccessException error) when (error.Failure == MemoryFailure.Unmapped)
        {
            Require(error.Address == 0x1000 && error.Path == "Record.value", "Failure coordinates");
            Require(error.LogicalRegion == missing, "Requested root retained");
        }

        // A cancelled token stops the operation before any bytes are read; it is not a memory failure.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            session.Read(region, "Record", "value", new MemoryAccessContext(cancellationToken: cancellation.Token));
            throw new InvalidOperationException("Expected cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation is a caller control signal, not an unavailable-memory result.
        }
        #endregion
    }

    /// <summary>Fails the executable guide when a documented result differs from the implementation.</summary>
    /// <param name="condition">The documented invariant.</param>
    /// <param name="message">Identifies the example result that failed.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
