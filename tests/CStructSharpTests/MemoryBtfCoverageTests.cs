namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Hand-encoded BTF fixtures cover split identity, integer slices, enum widths, packed multi-field bitfield storage, and bounded mutation input.</summary>
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

    /// <summary>
    /// Two bitfields packed into one shared storage word keep the same byte offset and their own position within
    /// that word, instead of the second one being placed as though it needed a fresh word of its own starting
    /// wherever its bit offset happened to fall. This is the shape of Linux's <c>struct uclamp_bucket</c> (an
    /// 8-byte struct with an 11-bit <c>value</c> field at bit 0 and a 53-bit <c>tasks</c> field at bit 11, both
    /// stored in one <c>unsigned long</c>), which is what first exposed the bug this test guards against: the
    /// second field's byte-truncated bit offset (11 / 8 = byte 1) plus its full 8-byte storage type's size
    /// overflowed the struct's own 8-byte extent, even though the two fields correctly share the struct's only
    /// storage word.
    /// </summary>
    [TestMethod]
    public void Bitfields_ShareOneStorageWordAcrossMultipleMembers()
    {
        byte[] bytes = Blob(
            [
            1, 1U << 24, 8, 64,
            3, 0x84000002, 8,
            5, 1, 0x0B000000,
            11, 1, 0x3500000B,
            ],
            "\0u\0r\0value\0tasks\0");
        MetadataImportResult imported = new BtfMetadata(bytes).Import(2);

        MemoryField value = imported.Schema.GetField(imported.RootTypeId, "value");
        Assert.AreEqual(0, value.Offset);
        Assert.AreEqual(0, value.BitOffset);
        Assert.AreEqual(11, value.BitWidth);

        MemoryField tasks = imported.Schema.GetField(imported.RootTypeId, "tasks");
        Assert.AreEqual(0, tasks.Offset);
        Assert.AreEqual(11, tasks.BitOffset);
        Assert.AreEqual(53, tasks.BitWidth);

        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[8]), 0, 8);
        session.PlanUpdate(region, imported.RootTypeId, "value", 5UL).Commit();
        session.PlanUpdate(region, imported.RootTypeId, "tasks", 12UL).Commit();
        Assert.AreEqual(5UL, Convert.ToUInt64(session.Read(region, imported.RootTypeId, "value")));
        Assert.AreEqual(12UL, Convert.ToUInt64(session.Read(region, imported.RootTypeId, "tasks")));
    }

    /// <summary>Describes a struct's own members - the same shared-bitfield-storage shape as the test above - without importing anything.</summary>
    [TestMethod]
    public void Describe_ReportsStructMembersIncludingSharedBitfieldStorage()
    {
        byte[] bytes = Blob(
            [
            1, 1U << 24, 8, 64,
            3, 0x84000002, 8,
            5, 1, 0x0B000000,
            11, 1, 0x3500000B,
            ],
            "\0u\0r\0value\0tasks\0");
        var metadata = new BtfMetadata(bytes);

        BtfTypeDescription description = metadata.Describe(2);

        Assert.AreEqual(2U, description.Id);
        Assert.AreEqual("r", description.Name);
        Assert.AreEqual(BtfKind.Struct, description.Kind);
        Assert.AreEqual(8, description.Size);
        Assert.AreEqual(2, description.Members.Count);
        Assert.AreEqual(new BtfMemberDescription("value", 1, 0, 0, 11), description.Members[0]);
        Assert.AreEqual(new BtfMemberDescription("tasks", 1, 0, 11, 53), description.Members[1]);
    }

    /// <summary>Describes an array by its element type and count, computing the same total size <see cref="BtfMetadata.Import"/> would.</summary>
    [TestMethod]
    public void Describe_ReportsArrayElementAndCount()
    {
        byte[] bytes = Blob([1, 1U << 24, 4, 32, 0, 3U << 24, 0, 1, 1, 3,], "\0u\0");
        var metadata = new BtfMetadata(bytes);

        BtfTypeDescription description = metadata.Describe(2);

        Assert.AreEqual(BtfKind.Array, description.Kind);
        Assert.AreEqual(1U, description.ElementTypeId);
        Assert.AreEqual(3, description.ElementCount);
        Assert.AreEqual(12, description.Size);
        Assert.AreEqual(0, description.Members.Count);
    }

    /// <summary>Describes a pointer by its target and the supplied pointer width, since BTF itself does not encode pointer size.</summary>
    [TestMethod]
    public void Describe_ReportsPointerTargetAndSuppliedSize()
    {
        byte[] bytes = Blob([1, 1U << 24, 4, 32, 0, 2U << 24, 1,], "\0u\0");
        var metadata = new BtfMetadata(bytes);

        BtfTypeDescription description = metadata.Describe(2, pointerSize: 8);

        Assert.AreEqual(BtfKind.Ptr, description.Kind);
        Assert.AreEqual(1U, description.TargetTypeId);
        Assert.AreEqual(8, description.Size);
    }

    /// <summary>Describing a typedef follows it to its storage kind, the same way <see cref="BtfMetadata.Import"/> would, while keeping the typedef's own name for display.</summary>
    [TestMethod]
    public void Describe_FollowsModifiersButKeepsTheDeclaredName()
    {
        byte[] bytes = Blob([1, 1U << 24, 4, 32, 3, 8U << 24, 1,], "\0u\0myint\0");
        var metadata = new BtfMetadata(bytes);

        BtfTypeDescription description = metadata.Describe(2);

        Assert.AreEqual("myint", description.Name);
        Assert.AreEqual(BtfKind.Int, description.Kind);
        Assert.AreEqual(4, description.Size);
    }

    /// <summary>
    /// Describing a struct never needs its members' own members to be valid - only <see cref="BtfMetadata.Import"/>,
    /// which does recurse, actually needs that. This is the property that makes <see cref="BtfMetadata.Describe"/>
    /// usable for finding out what a type looks like while diagnosing why importing something built from it failed.
    /// </summary>
    [TestMethod]
    public void Describe_DoesNotRecurseIntoAMembersOwnMembers()
    {
        byte[] bytes = Blob(
            [
            1, 0x04000001, 8, 7, 999, 0,
            11, 0x04000001, 8, 17, 1, 0,
            ],
            "\0Inner\0bad\0Outer\0inner\0");
        var metadata = new BtfMetadata(bytes);

        BtfTypeDescription description = metadata.Describe(2);
        Assert.AreEqual("Outer", description.Name);
        Assert.AreEqual(1, description.Members.Count);
        Assert.AreEqual(new BtfMemberDescription("inner", 1, 0, null, null), description.Members[0]);

        Assert.Throws<ArgumentException>(() => metadata.Import(2));
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
