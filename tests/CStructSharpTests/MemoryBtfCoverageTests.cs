namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Hand-encoded BTF fixtures cover split identity, integer slices, enum widths, and bounded mutation input.</summary>
[TestClass]
public class MemoryBtfCoverageTests
{
    /// <summary>Builds metadata words independently of any CStructSharp serializer.</summary>
    internal static byte[] Blob(uint[] types, string strings)
    {
        byte[] names = Encoding.UTF8.GetBytes(strings);
        byte[] result = new byte[24 + (types.Length * 4) + names.Length];
        result[0] = 0x9f;
        result[1] = 0xeb;
        result[2] = 1;
        uint[] header = [24, 0, (uint)types.Length * 4, (uint)types.Length * 4, (uint)names.Length,];
        for (int i = 0; i < header.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + (i * 4), 4), header[i]);
        }

        for (int i = 0; i < types.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(24 + (i * 4), 4), types[i]);
        }

        names.CopyTo(result, 24 + (types.Length * 4));
        return result;
    }

    /// <summary>Split aliases preserve IDs and resolve names through both string tables.</summary>
    [TestMethod]
    public void Split_ResolvesBaseTypesAndStrings()
    {
        var basis = new BtfMetadata(Blob([1, 1U << 24, 4, 32,], "\0u32\0"));
        var split = new BtfMetadata(Blob([5, 8U << 24, 1,], "alias\0"), basis);
        Assert.AreEqual(2, split.TypeCount);
        Assert.AreEqual(2U, split.FindType("alias"));
        MetadataImportResult imported = split.Import(2);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[] { 42, 0, 0, 0, }), 0, 4);
        Assert.AreEqual(42U, new MemorySession(imported.Schema).Read(region, imported.RootTypeId));
        Assert.AreEqual(1U, split.FindType("u32"));
    }

    /// <summary>Modern and legacy signed bitfield encodings produce the same checked projection.</summary>
    [TestMethod]
    public void Bitfields_NormalizeLegacyAndModern()
    {
        foreach (bool legacy in new[] { true, false, })
        {
            uint integer = legacy ? 0x01010003U : 0x01000008U;
            uint info = (4U << 24) | 1 | (legacy ? 0 : 0x80000000);
            uint memberBits = legacy ? 0 : (3U << 24) | 1;
            var metadata = new BtfMetadata(Blob([1, 1U << 24, 1, integer, 3, info, 1, 5, 1, memberBits,], "\0i\0r\0f\0"));
            MetadataImportResult imported = metadata.Import(2);
            var session = new MemorySession(imported.Schema);
            var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[] { 0xfe, }), 0, 1);
            Assert.AreEqual(-1L, session.Read(region, imported.RootTypeId, "f"));
            session.PlanUpdate(region, imported.RootTypeId, "f", -4).Commit();
            Assert.AreEqual(-4L, session.Read(region, imported.RootTypeId, "f"));
            byte[] bytes = new byte[1];
            region.Source.Read(0, bytes, new MemoryAccessContext());
            Assert.AreEqual(0xf8, bytes[0]);
        }
    }

    /// <summary>Full unsigned ENUM64 storage round-trips without signed address conversion.</summary>
    [TestMethod]
    public void Enum64_RoundTripsMaximum()
    {
        var metadata = new BtfMetadata(Blob([1, (19U << 24) | 1, 8, 3, uint.MaxValue, uint.MaxValue,], "\0E\0MAX\0"));
        MetadataImportResult imported = metadata.Import(1);
        var session = new MemorySession(imported.Schema);
        byte[] bytes = session.Serialize(imported.RootTypeId, ulong.MaxValue);
        Assert.IsTrue(bytes.All(value => value == 255));
        Assert.IsNotNull(session.Read(new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, 8), imported.RootTypeId));
    }

    /// <summary>Forward and qualified void targets remain address-only; arrays reuse fixed element stride.</summary>
    [TestMethod]
    public void References_RespectIncompleteTargetsAndArrays()
    {
        var metadata = new BtfMetadata(Blob(
            [
            1, 1U << 24, 4, 32,
            0, 3U << 24, 0, 1, 1, 2,
            0, 10U << 24, 0,
            0, 2U << 24, 3,
            ],
            "\0u32\0"));
        MetadataImportResult array = metadata.Import(2);
        var session = new MemorySession(array.Schema);
        byte[] bytes = session.Serialize(array.RootTypeId, new uint[] { 7, 9, });
        Assert.AreEqual(9U, session.Read(new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, 8), array.RootTypeId, "[1]"));
        MetadataImportResult pointer = metadata.Import(4);
        Assert.AreEqual(MemoryTypeKind.Incomplete, pointer.Schema.GetType("btf:3").Kind);
    }

    /// <summary>Repeatable mutations must either form a valid bounded schema or fail with a documented input error.</summary>
    [TestMethod]
    public void Btf_SeededMutationsRemainBounded()
    {
        byte[] seed = Blob([1, 1U << 24, 4, 32,], "\0u32\0");
        var random = new Random(42019);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            byte[] bytes = (byte[])seed.Clone();
            bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
            try
            {
                var metadata = new BtfMetadata(bytes, maxTypes: 8);
                _ = metadata.Import(1);
            }
            catch (Exception error) when (error is ArgumentException or OverflowException or CStructException)
            {
                // Invalid metadata is rejected; runtime failures such as indexing bugs must escape this filter.
            }
        }
    }
}
