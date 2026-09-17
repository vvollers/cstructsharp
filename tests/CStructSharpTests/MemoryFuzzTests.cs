namespace CStructSharp.Tests;

using System.Text;
using System.Text.Json;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Reproducible bounded mutation and independent byte-oracle checks for memory metadata and mapping arithmetic.</summary>
[TestClass]
public class MemoryFuzzTests
{
    /// <summary>Random fixed mappings agree with independently joined pages at low and high unsigned addresses.</summary>
    [TestMethod]
    public void Mappings_RandomRangesMatchIndependentBytes()
    {
        var random = new Random(71923);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            byte[] physical = new byte[512];
            random.NextBytes(physical);
            var source = new ByteArrayMemorySource("physical", physical);
            ulong address = iteration % 2 == 0 ? 100 : ulong.MaxValue - 255;
            int left = random.Next(1, 64);
            int right = random.Next(1, 64);
            var mapped = new MappedMemorySource("logical", [new(address, new MemoryRegion(source, 7, left)), new(address + (uint)left, new MemoryRegion(source, 300, right)),]);
            byte[] expected = physical.AsSpan(7, left).ToArray().Concat(physical.AsSpan(300, right).ToArray()).ToArray();
            int start = random.Next(expected.Length);
            int count = random.Next(1, expected.Length - start + 1);
            byte[] actual = new byte[count];
            var budget = new MemoryAccessContext(maxBytes: count, maxRequests: 20);
            Assert.AreEqual(count, mapped.Read(address + (uint)start, actual, budget));
            CollectionAssert.AreEqual(expected.AsSpan(start, count).ToArray(), actual);
            Assert.AreEqual(count, budget.BytesRequested);
            Assert.AreEqual(count, mapped.Describe(address + (uint)start, count).Sum(region => region.Length));
            MemoryAccessException hole = Assert.Throws<MemoryAccessException>(() => mapped.Read(address + (uint)expected.Length, new byte[1], new MemoryAccessContext()));
            Assert.AreEqual(MemoryFailure.Unmapped, hole.Failure);
            Assert.Throws<ArgumentException>(() => new MappedMemorySource("overlap", [new(address, new MemoryRegion(source, 0, 2)), new(address + 1, new MemoryRegion(source, 10, 2)),]));
        }
    }

    /// <summary>Byte mutations of a small ISF either produce a bounded valid type or a documented input failure.</summary>
    [TestMethod]
    public void Isf_SeededMutationsFailWithoutUnexpectedExceptions()
    {
        byte[] seed = Encoding.UTF8.GetBytes("""
            {"metadata":{"format":"6.2.0"},"base_types":{"u":{"kind":"int","size":4,"signed":false,"endian":"little"}},"user_types":{},"enums":{},"symbols":{}}
            """);
        var random = new Random(76121);
        int accepted = 0;
        int rejected = 0;
        for (int iteration = 0; iteration < 256; iteration++)
        {
            byte[] input = (byte[])seed.Clone();
            input[random.Next(input.Length)] = (byte)random.Next(256);
            try
            {
                MetadataImportResult result = IsfMetadata.Import(input, "u", maxBytes: 1024, maxTypes: 16);
                Assert.IsTrue(result.Schema.Types.Count <= 16);
                Assert.IsTrue(result.Schema.GetType(result.RootTypeId).Size <= 16);
                accepted++;
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or CStructLayoutException)
            {
                rejected++;
            }
        }

        Assert.AreEqual(256, accepted + rejected);
        Assert.IsTrue(rejected > 128);
    }
}
