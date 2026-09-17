namespace CStructSharp.Tests;

using System.Globalization;
using System.Numerics;
using CStructSharp.Memory;

/// <summary>Cross-operation assertions for bit slices, explicit pointers, and fixed collection shapes.</summary>
[TestClass]
public class MemoryOperationMatrixTests
{
    /// <summary>Independent bit arithmetic checks both byte orders, signed extrema, and exact preservation of untouched bits.</summary>
    [TestMethod]
    public void BitSlices_RoundTripAndPatchAllStorageWidths()
    {
        foreach (int size in new[] { 1, 2, 4, 8, })
        {
            foreach (bool little in new[] { false, true, })
            {
                foreach (int offset in new[] { 0, 1, 7, (size * 8) - 1, }.Distinct())
                {
                    foreach (int width in new[] { 1, Math.Min(3, (size * 8) - offset), (size * 8) - offset, }.Distinct())
                    {
                        foreach (bool signed in new[] { false, true, })
                        {
                            var schema = new MemorySchema(
                            [
                                new("word", "word", MemoryTypeKind.Scalar, size, scalarType: "uint" + (size * 8)),
                                new("record", "record", MemoryTypeKind.Struct, size, [new("bits", "word", 0, offset, width, signed),]),
                            ],
                            isLittleEndian: little);
                            var session = new MemorySession(schema);
                            BigInteger mask = (BigInteger.One << width) - 1;
                            BigInteger value = signed ? -(BigInteger.One << (width - 1)) : mask;
                            byte[] created = session.Serialize("record", new Dictionary<string, object?> { ["bits"] = value, });
                            BigInteger expected = (value & mask) << offset;
                            for (int index = 0; index < size; index++)
                            {
                                int shift = (little ? index : size - 1 - index) * 8;
                                Assert.AreEqual((byte)((expected >> shift) & 255), created[index]);
                            }

                            byte[] original = Enumerable.Repeat((byte)0xa5, size).ToArray();
                            var source = new ByteArrayMemorySource("image", original);
                            var region = new MemoryRegion(source, 0, size);
                            session.PlanUpdate(region, "record", "bits", value).Commit();
                            BigInteger parsed = BigInteger.Parse(Convert.ToString(session.Read(region, "record", "bits"), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
                            Assert.AreEqual(value, parsed);
                            byte[] updated = source.ToArray();
                            for (int index = 0; index < size; index++)
                            {
                                int shift = (little ? index : size - 1 - index) * 8;
                                byte byteMask = (byte)(((mask << offset) >> shift) & 255);
                                Assert.AreEqual((byte)((original[index] & ~byteMask) | created[index]), updated[index]);
                            }

                            Assert.Throws<ArgumentOutOfRangeException>(() => session.PlanUpdate(region, "record", "bits", signed ? BigInteger.One << (width - 1) : mask + 1));
                        }
                    }
                }
            }
        }
    }

    /// <summary>A resolver receives stored and containing coordinates and can switch spaces without rewriting stored bits.</summary>
    [TestMethod]
    public void Resolver_SwitchesSpacesAndRetainsPointerStorage()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct Node { uint32 id; Node *next; };"), "Node");
        var initial = new MemorySession(schema);
        byte[] one = initial.Serialize("Node", new Dictionary<string, object?> { ["id"] = 1U, ["next"] = new StoredPointer(0x101), });
        byte[] two = initial.Serialize("Node", new Dictionary<string, object?> { ["id"] = 2U, ["next"] = new StoredPointer(0), });
        var left = new ByteArrayMemorySource("left", one);
        var right = new MappedMemorySource("right", [new(0x100, new MemoryRegion(new ByteArrayMemorySource("right image", two), 0, two.Length)),]);
        int resolutions = 0;

        // This application's low-bit tag is removed only when selecting a target.
        var session = new MemorySession(schema, request =>
        {
            resolutions++;
            Assert.AreEqual(new StoredPointer(0x101), request.Pointer);
            Assert.AreEqual(4UL, request.Storage.Address);
            Assert.AreEqual(0UL, request.Container.Address);
            Assert.AreEqual(12L, request.Container.Length);
            Assert.AreEqual("Node", request.TargetTypeId);
            Assert.AreEqual(12, request.TargetSize);
            Assert.AreEqual("next.value.id", request.Path);
            Assert.IsTrue(request.Depth > 0);
            return new MemoryRegion(right, request.Pointer.Bits & ~1UL, request.TargetSize);
        });
        var region = new MemoryRegion(left, 0, one.Length);
        Assert.AreEqual(new StoredPointer(0x101), session.Read(region, "Node", "next"));
        Assert.AreEqual(0, resolutions);
        Assert.AreEqual(2U, session.Read(region, "Node", "next.value.id"));
        Assert.AreEqual(1, resolutions);
        CollectionAssert.AreEqual(one, left.ToArray());
        MemoryAccessException nullError = Assert.Throws<MemoryAccessException>(() => initial.Read(new MemoryRegion(right, 0x100, 12), "Node", "next.value"));
        Assert.AreEqual(MemoryFailure.InvalidValue, nullError.Failure);
        StringAssert.Contains(nullError.Message, "null");
    }

    /// <summary>Array counts, selected bounds, output budgets, and incomplete types have explicit failure behavior.</summary>
    [TestMethod]
    public void Operations_RejectIncompatibleValuesAndExtents()
    {
        var schema = new MemorySchema([
            new("u", "u", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("a", "a", MemoryTypeKind.Array, 2, elementTypeId: "u", count: 2),
            new("p", "p", MemoryTypeKind.Pointer, 8),
            new("x", "x", MemoryTypeKind.Incomplete, 0),
        ]);
        var session = new MemorySession(schema);
        var source = new ByteArrayMemorySource("image", new byte[8]);
        var region = new MemoryRegion(source, 0, 8);
        Assert.Throws<ArgumentException>(() => session.Serialize("a", new byte[] { 1, }));
        Assert.Throws<ArgumentException>(() => session.Serialize("a", 1));
        Assert.Throws<ArgumentException>(() => session.Serialize("p", 0UL));
        Assert.Throws<ArgumentException>(() => session.Serialize("p", new StoredPointer(0, 4)));
        Assert.Throws<ArgumentException>(() => session.Serialize("x", 0));
        Assert.Throws<ArgumentException>(() => session.Read(region, "x"));
        Assert.Throws<MemoryAccessException>(() => session.Serialize("a", new byte[] { 1, 2, }, new MemoryAccessContext(maxBytes: 1)));
        Assert.Throws<MemoryAccessException>(() => session.Read(region, "a", context: new MemoryAccessContext(maxRequests: 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Resolve(region, "a", "[2]"));
        Assert.Throws<ArgumentException>(() => session.Resolve(region, "p", "value"));
        Assert.Throws<MemoryAccessException>(() => session.PlanUpdate(region, "p", string.Empty, new StoredPointer(0), new MemoryAccessContext(maxBytes: 1)));
        Assert.Throws<ArgumentException>(() => MemoryPatch.Create(region, new byte[7]));
        Assert.Throws<ArgumentException>(() => MemoryPatch.Create(region, new byte[8], expected: new byte[7]));
        MemoryAccessException mismatch = Assert.Throws<MemoryAccessException>(() => MemoryPatch.Create(region, new byte[8], expected: Enumerable.Repeat((byte)1, 8).ToArray()));
        Assert.AreEqual(MemoryFailure.StaleSource, mismatch.Failure);
        StringAssert.Contains(mismatch.Message, "planning");
    }
}
