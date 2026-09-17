namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Synthetic independently encoded BTF/ISF fixtures and traversal failures.</summary>
[TestClass]
public class MemoryMetadataTests
{
    /// <summary>Builds BTF v1 bytes with a uint32 and a struct whose value is at byte offset four.</summary>
    private static byte[] Btf(bool littleEndian = true)
    {
        byte[] strings = Encoding.UTF8.GetBytes("\0u32\0record\0value\0");
        uint[] types = [1, 1U << 24, 4, 32, 5, (4U << 24) | 1, 8, 12, 1, 32,];
        var bytes = new byte[24 + (types.Length * 4) + strings.Length];
        bytes[0] = littleEndian ? (byte)0x9f : (byte)0xeb;
        bytes[1] = littleEndian ? (byte)0xeb : (byte)0x9f;
        bytes[2] = 1;
        uint[] header = [24, 0, (uint)(types.Length * 4), (uint)(types.Length * 4), (uint)strings.Length,];
        for (int i = 0; i < header.Length; i++)
        {
            Word(bytes.AsSpan(4 + (i * 4), 4), header[i], littleEndian);
        }

        for (int i = 0; i < types.Length; i++)
        {
            Word(bytes.AsSpan(24 + (i * 4), 4), types[i], littleEndian);
        }

        strings.CopyTo(bytes, 24 + (types.Length * 4));
        return bytes;
    }

    /// <summary>Encodes fixture metadata independently of the importer.</summary>
    private static void Word(Span<byte> bytes, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
    }

    /// <summary>Both BTF byte orders produce authoritative offsets and select only the requested field bytes.</summary>
    [TestMethod]
    public void Btf_ImportsOffsets_AndBothOrders()
    {
        foreach (bool little in new[] { true, false, })
        {
            var metadata = new BtfMetadata(Btf(little));
            MetadataImportResult imported = metadata.Import(metadata.FindType("record"));
            Assert.AreEqual(4, imported.Schema.GetField(imported.RootTypeId, "value").Offset);
            byte[] bytes = [0, 0, 0, 0, 0x12, 0x34, 0x56, 0x78,];
            var context = new MemoryAccessContext();
            object? value = new MemorySession(imported.Schema).Read(new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, 8), imported.RootTypeId, "value", context);
            Assert.AreEqual(little ? 0x78563412U : 0x12345678U, value);
            Assert.AreEqual(4, context.BytesRequested);
        }
    }

    /// <summary>Truncated sections, invalid references, oversized inputs, and malformed JSON are rejected.</summary>
    [TestMethod]
    public void Metadata_RejectsMalformedInputs()
    {
        byte[] data = Btf();
        for (int length = 0; length < data.Length; length++)
        {
            Assert.Throws<ArgumentException>(() => new BtfMetadata(data.AsMemory(0, length)));
        }

        Assert.Throws<ArgumentException>(() => new BtfMetadata(data, maxBytes: 16));
        Word(data.AsSpan(56, 4), 99, true);
        Assert.Throws<ArgumentException>(() => new BtfMetadata(data).Import(2));
        Assert.Throws<System.Text.Json.JsonException>(() => IsfMetadata.Import("{"u8.ToArray(), "node"));
    }

    /// <summary>ISF resolves a recursive node pointer and signed slices without external profiles or native ABI assumptions.</summary>
    [TestMethod]
    public void Isf_ImportsRecursivePointers_AndBitfields()
    {
        const string json = """
        {
          "metadata": { "format": "6.2.0" },
          "base_types": {
            "s8": { "size": 1, "kind": "int", "signed": true, "endian": "little" },
            "u32": { "size": 4, "kind": "int", "signed": false, "endian": "little" }
          },
          "user_types": {
            "node": { "kind": "struct", "size": 16, "fields": {
              "flags": { "offset": 0, "type": { "kind": "bitfield", "bit_position": 1, "bit_length": 3, "type": { "kind": "base", "name": "s8" } } },
              "id": { "offset": 4, "type": { "kind": "base", "name": "u32" } },
              "next": { "offset": 8, "type": { "kind": "pointer", "subtype": { "kind": "struct", "name": "node" } } }
            } }
          },
          "enums": {}, "symbols": {}
        }
        """;
        MetadataImportResult imported = IsfMetadata.Import(Encoding.UTF8.GetBytes(json), "node");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = session.Serialize(imported.RootTypeId, new Dictionary<string, object?> { ["flags"] = -1, ["id"] = 42U, ["next"] = new StoredPointer(ulong.MaxValue), });
        Assert.AreEqual(14, bytes[0]);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(-1L, session.Read(region, imported.RootTypeId, "flags"));
        Assert.AreEqual(new StoredPointer(ulong.MaxValue), session.Read(region, imported.RootTypeId, "next"));
        Assert.AreEqual(42U, session.Read(region, imported.RootTypeId, "id"));
    }

    /// <summary>Sentinels, repeats, bounded work, and source identity remain distinct termination conditions.</summary>
    [TestMethod]
    public void Walkers_TrackSentinels_Cycles_AndSpaces()
    {
        var source = new ByteArrayMemorySource("one", new byte[4]);
        var other = new ByteArrayMemorySource("two", new byte[4]);
        var root = new MemoryRegion(source, 0, 1);

        // Visit three addresses then return to the sentinel.
        MemoryWalkResult list = MemoryWalker.SentinelList(root, (node, _) => new MemoryRegion(source, (node.Address + 1) % 4, 1));
        Assert.AreEqual(MemoryWalkStop.Sentinel, list.Stop);
        Assert.AreEqual(3, list.Nodes.Count);

        // A loop that excludes the sentinel is corruption, not successful completion.
        MemoryWalkResult cycle = MemoryWalker.SentinelList(root, (_, _) => new MemoryRegion(source, 1, 1));
        Assert.AreEqual(MemoryWalkStop.RepeatedNode, cycle.Stop);

        // Identical coordinates in different spaces are separate nodes; shared children are deduplicated.
        MemoryWalkResult tree = MemoryWalker.Tree(root, (node, _) => ReferenceEquals(node.Source, source) ? new[] { new MemoryRegion(other, 0, 1), new MemoryRegion(other, 0, 1), } : Array.Empty<MemoryRegion>());
        Assert.AreEqual(2, tree.Nodes.Count);
        Assert.Throws<OverflowException>(() => MemoryWalker.ContainingRecord(3, 4));
    }

    /// <summary>A split field patch lists two backing fragments and preserves unrelated bytes in both pages.</summary>
    [TestMethod]
    public void Patch_SplitsPhysicalWrites_AndPrevalidatesReadOnlyFragments()
    {
        var backing = new ByteArrayMemorySource("backing", new byte[20]);
        var mapped = new MappedMemorySource("space", [new(100, new MemoryRegion(backing, 2, 2)), new(102, new MemoryRegion(backing, 12, 2)),]);
        MemoryPatch patch = MemoryPatch.Create(new MemoryRegion(mapped, 100, 4), new byte[] { 1, 2, 3, 4, });
        Assert.AreEqual(2, patch.Fragments.Count);
        patch.Commit();
        byte[] expected = new byte[20];
        expected[2] = 1;
        expected[3] = 2;
        expected[12] = 3;
        expected[13] = 4;
        CollectionAssert.AreEqual(expected, backing.ToArray());
        using var file = new MemoryStream(new byte[4]);
        var readOnly = new StreamMemorySource("readonly", file);
        Assert.Throws<NotSupportedException>(() => MemoryPatch.Create(new MemoryRegion(readOnly, 0, 4), new byte[] { 1, 2, 3, 4, }).Commit());
        CollectionAssert.AreEqual(new byte[4], file.ToArray());
    }
}
