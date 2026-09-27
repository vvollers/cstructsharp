namespace CStructSharp.Tests;

using System.Buffers.Binary;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Acceptance boundaries for independent placement, source contracts, and hostile metadata graphs.</summary>
[TestClass]
public class MemoryAcceptanceBoundaryTests
{
    /// <summary>Read, inspection, and patch failures retain the requested path and logical source separately from missing backing bytes.</summary>
    [TestMethod]
    public void SessionFailures_PreserveLogicalOperationContext()
    {
        var schema = new MemorySchema([new("u", "u", MemoryTypeKind.Scalar, 4, scalarType: "uint32"), new("r", "r", MemoryTypeKind.Struct, 8, [new("value", "u", 4),]),]);
        var session = new MemorySession(schema);
        var source = new MappedMemorySource("logical", []);
        var region = new MemoryRegion(source, 100, 8);
        MemoryAccessException read = Assert.Throws<MemoryAccessException>(() => session.Read(region, "r", "value"));
        MemoryAccessException inspect = Assert.Throws<MemoryAccessException>(() => session.Inspect(region, "r", "value"));
        MemoryAccessException patch = Assert.Throws<MemoryAccessException>(() => session.PlanUpdate(region, "r", "value", 1U));
        foreach (MemoryAccessException failure in new[] { read, inspect, patch, })
        {
            Assert.AreEqual("r.value", failure.Path);
            Assert.AreSame(region, failure.LogicalRegion);
            Assert.AreEqual(104UL, failure.Address);
            Assert.AreEqual("logical", failure.SourceId);
        }
    }

    /// <summary>Whole-array reads preserve element stride, and promotion rejects ambiguous names in either declaration order.</summary>
    [TestMethod]
    public void ArraysAndPromotion_KeepStrideAndRejectAmbiguity()
    {
        var word = new MemoryTypeDefinition("w", "w", MemoryTypeKind.Scalar, 2, scalarType: "uint16");
        var array = new MemoryTypeDefinition("a", "a", MemoryTypeKind.Array, 6, elementTypeId: "w", count: 3);
        var child = new MemoryTypeDefinition("c", "c", MemoryTypeKind.Struct, 2, [new("x", "w", 0),]);
        foreach (bool reverse in new[] { false, true, })
        {
            MemoryField[] fields = [new("child", "c", 0, promoted: true), new("x", "w", 2),];
            if (reverse)
            {
                Array.Reverse(fields);
            }

            var schema = new MemorySchema([word, array, child, new("r", "r", MemoryTypeKind.Struct, 4, fields),]);
            var session = new MemorySession(schema);
            var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[] { 1, 0, 2, 0, 3, 0, }), 0, 6);
            object?[] values = (object?[])session.Read(region, "a")!;
            CollectionAssert.AreEqual(new object?[] { (ushort)1, (ushort)2, (ushort)3, }, values);
            Assert.AreEqual((ushort)3, session.Read(region, "a", "[2]"));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Read(region, "a", "[3]"));
            StringAssert.Contains(Assert.Throws<ArgumentException>(() => session.Read(region, "r", "x")).Message, "Ambiguous");
            StringAssert.Contains(Assert.Throws<KeyNotFoundException>(() => session.Read(region, "r", "absent")).Message, "absent");
            Assert.Throws<MemoryAccessException>(() => session.Read(region, "r", context: new MemoryAccessContext(maxDepth: 1)));
        }
    }

    /// <summary>Signed slices accept zero and the positive limit while all adjacent out-of-range values are rejected.</summary>
    [TestMethod]
    public void BitSlices_CheckBothSignedLimitsAndPhysicalOverlap()
    {
        foreach (bool little in new[] { false, true, })
        {
            var word = new MemoryTypeDefinition("w", "w", MemoryTypeKind.Scalar, 2, scalarType: "uint16");
            foreach (bool signed in new[] { false, true, })
            {
                var schema = new MemorySchema([word, new("r", "r", MemoryTypeKind.Struct, 2, [new("x", "w", 0, 3, 9, signed),]),], isLittleEndian: little);
                var session = new MemorySession(schema);
                int minimum = signed ? -256 : 0;
                int maximum = signed ? 255 : 511;
                foreach (int value in new[] { minimum, 0, 1, maximum, })
                {
                    byte[] bytes = session.Serialize("r", new Dictionary<string, object?> { ["x"] = value, });
                    object? actual = session.Read(new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, 2), "r", "x");
                    Assert.AreEqual((long)value, Convert.ToInt64(actual));
                }

                foreach (int value in new[] { minimum - 1, maximum + 1, })
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => session.Serialize("r", new Dictionary<string, object?> { ["x"] = value, }));
                }
            }

            // A two-byte slice overlaps a field in its second physical byte in either byte order.
            Assert.Throws<ArgumentException>(() => new MemorySchema([word, new("r", "r", MemoryTypeKind.Struct, 2, [new("x", "w", 0, 3, 9), new("y", "w", 0, 8, 1),]),], isLittleEndian: little));
        }
    }

    /// <summary>Import normalizes full-width, cross-byte, signed, unsigned, and big-endian BTF bitfields.</summary>
    [TestMethod]
    public void BtfBits_KeepWidthSignPlacementAndNames()
    {
        foreach (bool little in new[] { false, true, })
        {
            foreach (int width in new[] { 8, 13, 32, 64, })
            {
                int offset = width == 64 ? 0 : 1;
                byte[] bytes = MemoryBtfCoverageTests.Blob([1, 1U << 24, 8, 64, 3, 0x84000001, 16, 5, 1, ((uint)width << 24) | (uint)offset,], "\0u\0r\0f\0");
                if (!little)
                {
                    bytes[0] = 0xeb;
                    bytes[1] = 0x9f;
                    for (int index = 4; index < 64; index += 4)
                    {
                        Array.Reverse(bytes, index, 4);
                    }
                }

                MetadataImportResult imported = new BtfMetadata(bytes).Import(2);
                MemoryField field = imported.Schema.GetField(imported.RootTypeId, "f");
                Assert.AreEqual(width, field.BitWidth);
                Assert.IsFalse(field.Signed);
                int storage = imported.Schema.GetType(field.TypeId).Size;
                Assert.AreEqual(little ? offset : (storage * 8) - offset - width, field.BitOffset);
                var session = new MemorySession(imported.Schema);
                ulong value = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
                byte[] created = session.Serialize(imported.RootTypeId, new Dictionary<string, object?> { ["f"] = value, });
                Assert.AreEqual(value, Convert.ToUInt64(session.Read(new MemoryRegion(new ByteArrayMemorySource("image", created), 0, created.Length), imported.RootTypeId, "f")));
            }
        }

        var basis = new BtfMetadata(MemoryBtfCoverageTests.Blob([1, 1U << 24, 4, 32,], "\0u\0"));
        var split = new BtfMetadata(MemoryBtfCoverageTests.Blob([3, 8U << 24, 1,], "alias\0"), basis);
        MetadataImportResult alias = split.Import(2);
        Assert.AreEqual("alias", alias.Schema.GetType(alias.RootTypeId).Name);
        StringAssert.Contains(alias.Schema.GetType(alias.RootTypeId).Provenance!, "type 2");
    }

    /// <summary>BTF bounds apply to empty sections and inherited tables, and a split chain cannot evade its depth cap.</summary>
    [TestMethod]
    public void BtfSectionsAndSplitChains_EnforceExactBounds()
    {
        byte[] empty = MemoryBtfCoverageTests.Blob([], "\0");
        Assert.AreEqual(0, new BtfMetadata(empty).TypeCount);
        Assert.Throws<ArgumentException>(() => new BtfMetadata(empty, maxBytes: 0));
        Assert.Throws<ArgumentException>(() => new BtfMetadata(empty, maxTypes: 0));
        var basis = new BtfMetadata(MemoryBtfCoverageTests.Blob([1, 1U << 24, 4, 32, 1, 8U << 24, 1,], "\0u\0"));
        Assert.Throws<ArgumentException>(() => new BtfMetadata(empty, basis, maxTypes: 1));
        Assert.AreEqual(2, new BtfMetadata(empty, basis, maxTypes: 2).TypeCount);
        var chain = new BtfMetadata(empty);
        for (int index = 0; index < 128; index++)
        {
            chain = new BtfMetadata(empty, chain);
        }

        StringAssert.Contains(Assert.Throws<ArgumentException>(() => new BtfMetadata(empty, chain)).Message, "depth");
        byte[] bad = (byte[])empty.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(20, 4), 2);
        Assert.Throws<ArgumentException>(() => new BtfMetadata(bad));
        bad = (byte[])empty.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(4, 4), 23);
        Assert.Throws<ArgumentException>(() => new BtfMetadata(bad));
        byte[] invalidUtf8 = MemoryBtfCoverageTests.Blob([1, 1U << 24, 1, 8,], "\0a\0");
        invalidUtf8[^2] = 0xff;
        Assert.Throws<System.Text.DecoderFallbackException>(() => new BtfMetadata(invalidUtf8));
    }

    /// <summary>Schema graph limits apply to aggregate members and deep by-value arrays, and cancellation prevents compilation.</summary>
    [TestMethod]
    public void SchemaGraph_EnforcesAggregateAndDepthLimits()
    {
        var word = new MemoryTypeDefinition("w", "w", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        Assert.Throws<ArgumentException>(() => new MemorySchema([word, new("a", "a", MemoryTypeKind.Struct, 1, [new("x", "w", 0),]), new("b", "b", MemoryTypeKind.Struct, 1, [new("x", "w", 0),]),], maxFields: 1));
        Assert.Throws<ArgumentException>(() => new MemorySchema([new("i", "i", MemoryTypeKind.Incomplete, 0), new("r", "r", MemoryTypeKind.Struct, 0, [new("x", "i", 0),]),]));
        Assert.Throws<ArgumentException>(() => new MemorySchema([word, new("r", "r", MemoryTypeKind.Struct, 0, [new("x", "w", 0),]),]));
        var definitions = new List<MemoryTypeDefinition>();
        for (int index = 0; index < 130; index++)
        {
            definitions.Add(new("a" + index, "a" + index, MemoryTypeKind.Array, 1, elementTypeId: index == 129 ? "w" : "a" + (index + 1), count: 1));
        }

        definitions.Add(word);
        StringAssert.Contains(Assert.Throws<ArgumentException>(() => new MemorySchema(definitions)).Message, "depth");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new MemorySchema([word,], cancellationToken: cancellation.Token));
        var session = new MemorySession(new MemorySchema([word,]));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[1]), 0, 1);
        var cancelled = new MemoryAccessContext(cancellationToken: cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => session.Resolve(region, "w", context: cancelled));
        Assert.Throws<OperationCanceledException>(() => session.Serialize("w", (byte)1, cancelled));
        Assert.Throws<ArgumentException>(() => new MemorySchema([new("s", "s", MemoryTypeKind.Scalar, 1, scalarType: " "),]));
    }
}
