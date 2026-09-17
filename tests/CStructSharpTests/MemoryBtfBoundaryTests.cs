namespace CStructSharp.Tests;

using System.Buffers.Binary;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;
using CStructSharp.Values;

/// <summary>Checks bounded BTF section parsing and representative reachable native type families.</summary>
[TestClass]
public class MemoryBtfBoundaryTests
{
    /// <summary>Primitive, qualifier, forward, function, union, and enum descriptions retain their intended semantics.</summary>
    [TestMethod]
    public void Btf_ImportsReachableKinds()
    {
        uint[] words = [
            1, 1U << 24, 1, 0x01000008,
            3, 16U << 24, 4,
            0, 9U << 24, 1,
            0, 10U << 24, 3,
            0, 11U << 24, 4,
            0, 18U << 24, 5,
            5, 7U << 24, 0,
            0, 2U << 24, 7,
            7, 12U << 24, 10,
            0, 13U << 24, 0,
            9, (5U << 24) | 2, 4, 11, 1, 0, 13, 2, 0,
            15, (6U << 24) | 0x80000001, 4, 17, uint.MaxValue,
            15, (19U << 24) | 0x80000001, 8, 17, uint.MaxValue, uint.MaxValue,
        ];
        var metadata = new BtfMetadata(MemoryBtfCoverageTests.Blob(words, "\0i\0f\0T\0F\0U\0a\0b\0E\0NEG\0"));
        Assert.AreEqual(13, metadata.TypeCount);
        Assert.Throws<ArgumentException>(() => metadata.FindType("E"));
        Assert.Throws<KeyNotFoundException>(() => metadata.FindType("absent"));
        MetadataImportResult signed = metadata.Import(6);
        Assert.AreEqual((sbyte)-1, new MemorySession(signed.Schema).Read(new MemoryRegion(new ByteArrayMemorySource("image", new byte[] { 255, }), 0, 1), signed.RootTypeId));
        MetadataImportResult floating = metadata.Import(2);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0xc0, 0x3f, }, new MemorySession(floating.Schema).Serialize(floating.RootTypeId, 1.5F));
        MetadataImportResult union = metadata.Import(11);
        var unionSession = new MemorySession(union.Schema);
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 0, }, unionSession.Serialize(union.RootTypeId, new MemoryUnionSelection("a", (sbyte)-1)));
        foreach (uint id in new uint[] { 12, 13, })
        {
            MetadataImportResult enumeration = metadata.Import(id);
            byte[] bytes = new MemorySession(enumeration.Schema).Serialize(enumeration.RootTypeId, -1);
            Assert.AreEqual(id == 12 ? 4 : 8, bytes.Length);
            Assert.IsTrue(bytes.All(value => value == 255));
            var value = (EnumValueResult)new MemorySession(enumeration.Schema).Read(new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length), enumeration.RootTypeId)!;
            Assert.AreEqual("NEG", value.Name);
            Assert.AreEqual(-1, (int)value.Value);
            Assert.IsTrue(value.IsSigned);
        }

        foreach (uint id in new uint[] { 7, 9, 10, })
        {
            MetadataImportResult incomplete = metadata.Import(id);
            Assert.AreEqual(MemoryTypeKind.Incomplete, incomplete.Schema.GetType(incomplete.RootTypeId).Kind);
            Assert.IsTrue(incomplete.Diagnostics.Count > 0);
        }
    }

    /// <summary>Invalid header lengths, reserved bits, string extents, and metadata budgets fail before schema construction.</summary>
    [TestMethod]
    public void Btf_RejectsMalformedSections()
    {
        byte[] seed = MemoryBtfCoverageTests.Blob([1, 1U << 24, 4, 32,], "\0u32\0");
        foreach ((int offset, uint value) in new (int, uint)[]
        {
            (4, 20), (8, 100), (12, 100), (16, 0), (20, 100), (24, 100), (28, 0x01010000), (28, 0x01000001), (28, 31U << 24),
        })
        {
            byte[] corrupt = (byte[])seed.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(offset, 4), value);
            ArgumentException error = Assert.Throws<ArgumentException>(() => new BtfMetadata(corrupt));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message));
        }

        foreach (int offset in new[] { 0, 2, 3, 40, 44, })
        {
            byte[] corrupt = (byte[])seed.Clone();
            corrupt[offset] = offset == 2 ? (byte)2 : (byte)1;
            Assert.Throws<ArgumentException>(() => new BtfMetadata(corrupt));
        }

        Assert.Throws<ArgumentException>(() => new BtfMetadata(seed, maxBytes: seed.Length - 1));
        Assert.Throws<ArgumentException>(() => new BtfMetadata(seed, maxTypes: 0));
        var metadata = new BtfMetadata(seed, maxBytes: seed.Length, maxTypes: 1);
        Assert.AreEqual(1, metadata.TypeCount);
        Assert.Throws<ArgumentException>(() => metadata.Import(2));
        Assert.Throws<OperationCanceledException>(() => new BtfMetadata(seed, cancellationToken: new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => metadata.Import(1, cancellationToken: new CancellationToken(true)));
    }

    /// <summary>Invalid modifiers, unsupported value kinds, and truncated type records cannot become plausible values.</summary>
    [TestMethod]
    public void Btf_RejectsBadDependenciesAndUnsupportedRoots()
    {
        foreach (uint[] words in new uint[][]
        {
            [0, 8U << 24, 1,],
            [0, 8U << 24, 99,],
            [0, 14U << 24, 0, 0,],
            [0, 15U << 24, 0,],
            [0, 1U << 24, 1, 3,],
            [0, 1U << 24, 1, 8, 0, (4U << 24) | 1, 1, 0, 1, 1,],
        })
        {
            var metadata = new BtfMetadata(MemoryBtfCoverageTests.Blob(words, "\0"));
            uint root = words.Length > 4 ? 2U : 1U;
            Assert.Throws<ArgumentException>(() => metadata.Import(root));
        }

        byte[] truncated = MemoryBtfCoverageTests.Blob([0, 1U << 24, 1,], "\0");
        Assert.Throws<ArgumentException>(() => new BtfMetadata(truncated));
        var baseMetadata = new BtfMetadata(MemoryBtfCoverageTests.Blob([0, 1U << 24, 1, 8,], "\0"));
        Assert.Throws<ArgumentException>(() => new BtfMetadata(MemoryBtfCoverageTests.Blob([0, 2U << 24, 1,], string.Empty), baseMetadata, maxTypes: 1));
    }
}
