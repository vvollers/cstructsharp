namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;
using CStructSharp.Values;

/// <summary>Preserves each BTF import's graph, diagnostics, options, and ownership when scalar compilation is reused.</summary>
[TestClass]
public class MemoryBtfCompilationReuseTests
{
    /// <summary>Repeated and out-of-order roots retain their own ordered reachable graph, including pointer cycles and disjoint types.</summary>
    [TestMethod]
    public void Imports_PreserveRootSpecificGraphsAndIndependentDescriptors()
    {
        byte[] bytes = Graph();
        var metadata = new BtfMetadata(bytes);
        MetadataImportResult first = metadata.Import(5);
        MetadataImportResult other = metadata.Import(6);
        MetadataImportResult disjoint = metadata.Import(8);
        CollectionAssert.AreEqual(new[] { "btf:1", "btf:2", "btf:4", "btf:3", "btf:6:bits:6", "btf:6", "btf:5:bits:9", "btf:5", }, first.Schema.Types.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "btf:3", "btf:1", "btf:2", "btf:4", "btf:5:bits:9", "btf:5", "btf:6:bits:6", "btf:6", }, other.Schema.Types.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "btf:7", "btf:8", }, disjoint.Schema.Types.Keys.ToArray());
        Assert.AreEqual("btf:5", first.RootTypeId);
        Assert.AreEqual("btf:6", other.RootTypeId);
        Assert.AreEqual("btf:8", disjoint.RootTypeId);
        Assert.AreEqual(0, first.Diagnostics.Count);
        Assert.AreEqual(0, other.Diagnostics.Count);
        Assert.AreEqual(0, disjoint.Diagnostics.Count);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            AssertEquivalent(disjoint, metadata.Import(8));
            AssertEquivalent(other, metadata.Import(6));
            AssertEquivalent(first, metadata.Import(5));
        }

        var reverse = new BtfMetadata(bytes);
        AssertEquivalent(other, reverse.Import(6));
        AssertEquivalent(disjoint, reverse.Import(8));
        AssertEquivalent(first, reverse.Import(5));
        AssertGraphValues(first, true, 8);
        AssertGraphValues(metadata.Import(5), true, 8);
    }

    /// <summary>Best-effort imports preserve root-dependent discovery order and keep importer notes before schema diagnostics.</summary>
    [TestMethod]
    public void Imports_PreserveBestEffortDiagnosticAndTypeOrder()
    {
        var metadata = new BtfMetadata(MalformedGraph());
        var options = new MetadataImportOptions { BestEffort = true, };
        MetadataImportResult forward = metadata.Import(8, options);
        MetadataImportResult reverse = metadata.Import(9, options);
        CollectionAssert.AreEqual(new[] { "btf:3", "btf:2", "btf:5", "btf:4", "btf:1", "btf:6", "btf:7", "btf:8", }, forward.Schema.Types.Keys.ToArray());
        CollectionAssert.AreEqual(new[] { "btf:5", "btf:4", "btf:3", "btf:2", "btf:1", "btf:7", "btf:6", "btf:9", }, reverse.Schema.Types.Keys.ToArray());
        string incompleteA = "btf:2: missingA is incomplete or callable; address-only use is supported.";
        string incompleteB = "btf:4: missingB is incomplete or callable; address-only use is supported.";
        string badA = "btf:6: demoted to a 0-byte raw-bytes placeholder - Member 'btf:6.value' exceeds its containing extent.";
        string badB = "btf:7: demoted to a 0-byte raw-bytes placeholder - Member 'btf:7.value' exceeds its containing extent.";
        CollectionAssert.AreEqual(new[] { incompleteA, incompleteB, badA, badB, }, forward.Diagnostics.ToArray());
        CollectionAssert.AreEqual(new[] { incompleteB, incompleteA, badA, badB, }, reverse.Diagnostics.ToArray());
        CollectionAssert.AreEqual(new[] { badA, badB, }, forward.Schema.Diagnostics.ToArray());
        CollectionAssert.AreEqual(new[] { badA, badB, }, reverse.Schema.Diagnostics.ToArray());
        Assert.AreEqual(MemoryTypeKind.RawBytes, forward.Schema.GetType("btf:6").Kind);
        Assert.AreEqual(MemoryTypeKind.RawBytes, forward.Schema.GetType("btf:7").Kind);
        AssertEquivalent(reverse, metadata.Import(9, options));
        AssertEquivalent(forward, metadata.Import(8, options));
        AssertEquivalent(reverse, new BtfMetadata(MalformedGraph()).Import(9, options));
    }

    /// <summary>Warm successful best-effort imports cannot suppress strict validation or reuse a previous exception object.</summary>
    [TestMethod]
    public void Imports_PreserveFreshStrictFailuresAfterBestEffort()
    {
        var metadata = new BtfMetadata(MalformedGraph());
        _ = metadata.Import(8, new MetadataImportOptions { BestEffort = true, });
        _ = metadata.Import(9, new MetadataImportOptions { BestEffort = true, });
        foreach ((uint root, string failingType) in new[] { (8U, "btf:6"), (9U, "btf:7"), (8U, "btf:6"), })
        {
            // Strict imports still validate the first invalid member reachable in this root's discovery order.
            CStructLayoutException first = Assert.Throws<CStructLayoutException>(() => metadata.Import(root));
            first.Data["caller"] = "first";

            // A repeated failure must not expose another caller's exception state.
            CStructLayoutException second = Assert.Throws<CStructLayoutException>(() => metadata.Import(root));
            Assert.AreNotSame(first, second);
            Assert.AreEqual($"Member '{failingType}.value' exceeds its containing extent.", first.Message);
            Assert.AreEqual(first.Message, second.Message);
            Assert.AreEqual(CStructErrorCode.InvalidLayout, second.Code);
            Assert.IsNull(second.Path);
            Assert.IsFalse(second.Data.Contains("caller"));
        }

        Assert.AreEqual(1, metadata.Import(1).Schema.Types.Count);
        Assert.AreEqual(4, metadata.Import(8, new MetadataImportOptions { BestEffort = true, }).Diagnostics.Count);
    }

    /// <summary>Descriptor limits, cancellation, and invalid options remain per-import checks after successful compilation.</summary>
    [TestMethod]
    public void Imports_KeepBudgetsAndCancellationAfterWarmup()
    {
        var metadata = new BtfMetadata(Graph());
        MetadataImportResult expected = metadata.Import(5);
        Assert.AreEqual(8, expected.Schema.Types.Count);
        foreach (uint root in new uint[] { 5, 6, })
        {
            _ = metadata.Import(root);

            // Synthetic bit-slice storage remains part of each fresh root's descriptor budget.
            CStructLayoutException limited = Assert.Throws<CStructLayoutException>(() => metadata.Import(root, new MetadataImportOptions { MaxTypes = 7, }));
            Assert.AreEqual("BTF import exceeds its descriptor budget.", limited.Message);
            Assert.AreEqual(8, metadata.Import(root, new MetadataImportOptions { MaxTypes = 8, }).Schema.Types.Count);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            // A warm layout does not bypass the new operation's cancellation token.
            OperationCanceledException cancelled = Assert.Throws<OperationCanceledException>(() => metadata.Import(root, cancellationToken: cancellation.Token));
            Assert.AreEqual(cancellation.Token, cancelled.CancellationToken);
        }

        // Invalid per-import options are still validated before any reused compilation can be returned.
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Import(5, new MetadataImportOptions { MaxTypes = 0, }));

        // Target pointer widths retain their supported-width validation.
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Import(5, new MetadataImportOptions { PointerSize = 3, }));
        AssertEquivalent(expected, metadata.Import(5));
    }

    /// <summary>Imports with different pointer widths and metadata byte orders produce their own scalar, enum, and bit-slice bytes.</summary>
    [TestMethod]
    public void Imports_KeepPointerWidthAndByteOrderSettings()
    {
        foreach (bool littleEndian in new[] { true, false, })
        {
            var metadata = new BtfMetadata(Graph(littleEndian));
            foreach (int width in new[] { 8, 4, 1, 2, 8, })
            {
                var options = new MetadataImportOptions { PointerSize = width, };
                MetadataImportResult imported = metadata.Import(5, options);
                Assert.AreEqual(width, imported.Schema.PointerSize);
                Assert.AreEqual(littleEndian, imported.Schema.IsLittleEndian);
                Assert.AreEqual(width, imported.Schema.GetType("btf:3").Size);
                Assert.AreEqual(width, imported.Schema.GetType("btf:4").Size);
                AssertGraphValues(imported, littleEndian, width);
                AssertEquivalent(new BtfMetadata(Graph(littleEndian)).Import(5, options), imported);
            }
        }
    }

    /// <summary>Split metadata keeps base definitions, aliases, and same-ID sibling enums independent across imports.</summary>
    [TestMethod]
    public void Imports_KeepSplitTablesAndSiblingDefinitionsIndependent()
    {
        byte[] baseBytes = Graph();
        var basis = new BtfMetadata(baseBytes);
        MetadataImportResult baseResult = basis.Import(5);
        int stringBase = (int)BinaryPrimitives.ReadUInt32LittleEndian(baseBytes.AsSpan(20, 4));
        var first = new BtfMetadata(SplitGraph(stringBase, 7), basis);
        var second = new BtfMetadata(SplitGraph(stringBase, 9), basis);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            foreach ((BtfMetadata metadata, byte enumValue) in new[] { (first, (byte)7), (second, (byte)9), })
            {
                MetadataImportResult alias = metadata.Import(9);
                Assert.AreEqual("btf:9", alias.RootTypeId);
                CollectionAssert.AreEqual(new[] { "btf:9", }, alias.Schema.Types.Keys.ToArray());
                Assert.AreEqual("alias", alias.Schema.GetType("btf:9").Name);
                Assert.AreEqual("BTF v1 type 9, terminal 1, kind 1", alias.Schema.GetType("btf:9").Provenance);
                MetadataImportResult imported = metadata.Import(11);
                CollectionAssert.AreEqual(new[] { "btf:1", "btf:10", "btf:11:bits:6", "btf:11", }, imported.Schema.Types.Keys.ToArray());
                var region = new MemoryRegion(new ByteArrayMemorySource("image", [1, 0, enumValue, 0, 20, 0,]), 0, 6);
                var session = new MemorySession(imported.Schema);
                var enumeration = (EnumValueResult)session.Read(region, imported.RootTypeId, "tag")!;
                Assert.AreEqual("other", enumeration.Name);
                Assert.AreEqual((ulong)enumValue, enumeration.RawBits);
                Assert.AreEqual(5, session.Read(region, imported.RootTypeId, "bits"));
                AssertEquivalent(baseResult, metadata.Import(5));
            }

            AssertEquivalent(baseResult, basis.Import(5));
        }
    }

    /// <summary>Concurrent first imports retain independent schemas and exact results for overlapping and disjoint roots.</summary>
    [TestMethod]
    public void Imports_SupportConcurrentFirstCompilation()
    {
        byte[] bytes = Graph();
        var reference = new BtfMetadata(bytes);
        var expected = new Dictionary<(uint Root, int Width), MetadataImportResult>();
        foreach (uint root in new uint[] { 5, 6, 8, })
        {
            foreach (int width in new[] { 4, 8, })
            {
                expected.Add((root, width), reference.Import(root, new MetadataImportOptions { PointerSize = width, }));
            }
        }

        var shared = new BtfMetadata(bytes);
        var results = new MetadataImportResult[24];

        uint[] roots = [5, 6, 8,];

        // Workers share only the parsed table; every import owns its graph and uses its own target options.
        Parallel.For(0, results.Length, index =>
        {
            uint root = roots[index % 3];
            int width = index % 2 == 0 ? 4 : 8;
            MetadataImportResult actual = shared.Import(root, new MetadataImportOptions { PointerSize = width, });
            AssertEquivalent(expected[(root, width)], actual);
            results[index] = actual;
            if (root == 5)
            {
                AssertGraphValues(actual, true, width);
            }
        });
        for (int index = 6; index < results.Length; index++)
        {
            AssertEquivalent(results[index - 6], results[index]);
        }
    }

    /// <summary>Checks the complete public descriptor content and ordering while requiring independently owned import objects.</summary>
    /// <param name="expected">Earlier import whose descriptors must stay unchanged.</param>
    /// <param name="actual">Fresh import of the same root and settings.</param>
    private static void AssertEquivalent(MetadataImportResult expected, MetadataImportResult actual)
    {
        Assert.AreNotSame(expected, actual);
        Assert.AreNotSame(expected.Schema, actual.Schema);
        Assert.AreNotSame(expected.Schema.Types, actual.Schema.Types);
        Assert.AreNotSame(expected.Diagnostics, actual.Diagnostics);
        Assert.AreEqual(expected.RootTypeId, actual.RootTypeId);
        Assert.AreEqual(expected.Schema.PointerSize, actual.Schema.PointerSize);
        Assert.AreEqual(expected.Schema.IsLittleEndian, actual.Schema.IsLittleEndian);
        CollectionAssert.AreEqual(expected.Schema.Types.Keys.ToArray(), actual.Schema.Types.Keys.ToArray());
        CollectionAssert.AreEqual(expected.Diagnostics.ToArray(), actual.Diagnostics.ToArray());
        CollectionAssert.AreEqual(expected.Schema.Diagnostics.ToArray(), actual.Schema.Diagnostics.ToArray());
        foreach ((string id, MemoryTypeDefinition left) in expected.Schema.Types)
        {
            MemoryTypeDefinition right = actual.Schema.GetType(id);
            Assert.AreNotSame(left, right);
            Assert.AreEqual(left.Id, right.Id);
            Assert.AreEqual(left.Name, right.Name);
            Assert.AreEqual(left.Kind, right.Kind);
            Assert.AreEqual(left.Size, right.Size);
            Assert.AreEqual(left.ElementTypeId, right.ElementTypeId);
            Assert.AreEqual(left.Count, right.Count);
            Assert.AreEqual(left.ScalarType, right.ScalarType);
            Assert.AreEqual(left.Declaration, right.Declaration);
            Assert.AreEqual(left.Provenance, right.Provenance);
            Assert.AreEqual(left.IsLittleEndian, right.IsLittleEndian);
            CollectionAssert.AreEqual(left.Fields.ToArray(), right.Fields.ToArray());
            for (int field = 0; field < left.Fields.Count; field++)
            {
                Assert.AreNotSame(left.Fields[field], right.Fields[field]);
            }
        }
    }

    /// <summary>Checks independent expected storage for the graph's scalar, enum, pointer, and three-bit field.</summary>
    /// <param name="imported">Import rooted at the graph's first record.</param>
    /// <param name="littleEndian">Expected byte order.</param>
    /// <param name="pointerWidth">Expected pointer width in bytes.</param>
    private static void AssertGraphValues(MetadataImportResult imported, bool littleEndian, int pointerWidth)
    {
        ulong pointer = pointerWidth == 8 ? 0x0102030405060708UL : pointerWidth == 4 ? 0x01020304UL : pointerWidth == 2 ? 0x0102UL : 1UL;
        var expected = new byte[16];
        expected[0] = littleEndian ? (byte)0x34 : (byte)0x12;
        expected[1] = littleEndian ? (byte)0x12 : (byte)0x34;
        expected[littleEndian ? 2 : 3] = 7;
        for (int index = 0; index < pointerWidth; index++)
        {
            int shift = (littleEndian ? index : pointerWidth - 1 - index) * 8;
            expected[4 + index] = (byte)(pointer >> shift);
        }

        expected[12] = littleEndian ? (byte)0x14 : (byte)0x28;
        var session = new MemorySession(imported.Schema);
        var value = new Dictionary<string, object?>
        {
            ["value"] = (ushort)0x1234,
            ["tag"] = 7,
            ["next"] = new StoredPointer(pointer, pointerWidth),
            ["bits"] = 5,
        };
        CollectionAssert.AreEqual(expected, session.Serialize(imported.RootTypeId, value));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", expected), 0, expected.Length);
        var read = (StructValue)session.Read(region, imported.RootTypeId)!;
        Assert.AreEqual((ushort)0x1234, read["value"]);
        Assert.AreEqual("on", ((EnumValueResult)read["tag"]!).Name);
        Assert.AreEqual(new StoredPointer(pointer, pointerWidth), read["next"]);
        Assert.AreEqual(5, read["bits"]);
    }

    /// <summary>Creates two overlapping pointer cycles sharing scalar and enum definitions plus one disjoint record.</summary>
    /// <param name="littleEndian">Byte order of every metadata word.</param>
    /// <returns>A BTF blob whose record roots are IDs 5, 6, and 8.</returns>
    private static byte[] Graph(bool littleEndian = true)
    {
        (string strings, Dictionary<string, uint> names) = Names("word", "kind", "on", "first", "second", "value", "tag", "next", "bits", "wide", "disjoint");
        uint[] words =
        [
            names["word"], 1U << 24, 2, 16,
            names["kind"], (6U << 24) | 1, 2, names["on"], 7,
            0, 2U << 24, 5,
            0, 2U << 24, 6,
            names["first"], 0x84000004, 16, names["value"], 1, 0, names["tag"], 2, 16, names["next"], 4, 32, names["bits"], 1, (3U << 24) | 98,
            names["second"], 0x84000004, 16, names["next"], 3, 0, names["value"], 1, 64, names["bits"], 1, (3U << 24) | 82, names["tag"], 2, 96,
            names["wide"], 1U << 24, 4, 32,
            names["disjoint"], (4U << 24) | 1, 4, names["value"], 7, 0,
        ];
        byte[] bytes = MemoryBtfCoverageTests.Blob(words, strings);
        if (!littleEndian)
        {
            bytes[0] = 0xeb;
            bytes[1] = 0x9f;
            for (int offset = 4; offset < 24 + (words.Length * 4); offset += 4)
            {
                Array.Reverse(bytes, offset, 4);
            }
        }

        return bytes;
    }

    /// <summary>Creates two roots that encounter incomplete targets and invalid zero-size composites in opposite orders.</summary>
    /// <returns>A BTF blob with malformed roots 8 and 9 and a valid scalar root 1.</returns>
    private static byte[] MalformedGraph()
    {
        (string strings, Dictionary<string, uint> names) = Names("byte", "missingA", "missingB", "badA", "badB", "forward", "reverse", "pointerA", "pointerB", "first", "second", "value");
        return MemoryBtfCoverageTests.Blob(
        [
            names["byte"], 1U << 24, 1, 8,
            names["missingA"], 7U << 24, 0,
            0, 2U << 24, 2,
            names["missingB"], 7U << 24, 0,
            0, 2U << 24, 4,
            names["badA"], (4U << 24) | 1, 0, names["value"], 1, 0,
            names["badB"], (4U << 24) | 1, 0, names["value"], 1, 0,
            names["forward"], (4U << 24) | 4, 16, names["pointerA"], 3, 0, names["pointerB"], 5, 64, names["first"], 6, 128, names["second"], 7, 128,
            names["reverse"], (4U << 24) | 4, 16, names["pointerB"], 5, 0, names["pointerA"], 3, 64, names["second"], 7, 128, names["first"], 6, 128,
        ],
        strings);
    }

    /// <summary>Creates split types that reuse a base scalar while defining an independent enum and bit-slice record.</summary>
    /// <param name="stringBase">Byte length of the base table's string section.</param>
    /// <param name="enumValue">Value assigned to the split enum's member.</param>
    /// <returns>A split BTF blob extending the eight types in <see cref="Graph"/> with IDs 9 through 11.</returns>
    private static byte[] SplitGraph(int stringBase, uint enumValue)
    {
        (string strings, Dictionary<string, uint> names) = Names("alias", "kind", "other", "split", "value", "tag", "bits");
        foreach (string name in names.Keys.ToArray())
        {
            names[name] += (uint)stringBase;
        }

        return MemoryBtfCoverageTests.Blob(
        [
            names["alias"], 8U << 24, 1,
            names["kind"], (6U << 24) | 1, 2, names["other"], enumValue,
            names["split"], 0x84000003, 6, names["value"], 1, 0, names["tag"], 10, 16, names["bits"], 1, (3U << 24) | 34,
        ],
        strings);
    }

    /// <summary>Builds a NUL-terminated UTF-8 string table and the byte offset of every supplied name.</summary>
    /// <param name="names">Distinct names in their desired table order.</param>
    /// <returns>The string table and its ordinal name-to-byte-offset map.</returns>
    private static (string Strings, Dictionary<string, uint> Offsets) Names(params string[] names)
    {
        var strings = new StringBuilder("\0");
        var offsets = new Dictionary<string, uint>(StringComparer.Ordinal);
        uint offset = 1;
        foreach (string name in names)
        {
            offsets.Add(name, offset);
            strings.Append(name).Append('\0');
            offset += (uint)Encoding.UTF8.GetByteCount(name) + 1;
        }

        return (strings.ToString(), offsets);
    }
}
