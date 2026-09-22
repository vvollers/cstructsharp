namespace CStructSharp.Tests.Generated;

using CStructSharp.Generated;

/// <summary>Checks the writer's pooled buffer lifetime across growth and final disposal.</summary>
[TestClass]
public class WriteCursorOwnershipTests
{
    /// <summary>Growing a rental preserves its prefix and clears new storage even when the pool contains used arrays.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void Growth_ClearsTheNewStorageTail()
    {
        using var rentals = new PoolReturnListener();
        var cursor = new WriteCursor(null, "root");
        try
        {
            int capacity = rentals.LastRentalLength;
            cursor.Reserve(capacity, "prefix", "uint8").Fill(17);

            // Seed the replacement-size bucket with used storage. Correctness does not depend on reuse:
            // whichever buffer Grow receives, its new tail must contain zeros rather than prior pool bytes.
            byte[] used = System.Buffers.ArrayPool<byte>.Shared.Rent(capacity * 2);
            used.AsSpan().Fill(0xA5);
            System.Buffers.ArrayPool<byte>.Shared.Return(used);
            Span<byte> tail = cursor.Reserve(capacity, "tail", "uint8");
            Assert.IsFalse(tail.ContainsAnyExcept((byte)0), "Growth must clear every newly exposed byte.");
            Assert.IsFalse(cursor.Written[..capacity].ContainsAnyExcept((byte)17), "Growth must retain the written prefix.");
        }
        finally
        {
            cursor.Dispose();
        }
    }

    /// <summary>Growth returns the old rental, keeps the replacement owned until disposal, and preserves existing bytes.</summary>
    [TestMethod]
    [DoNotParallelize]
    public void GrowthAndDisposal_ReturnTheirExactRentals()
    {
        using var returns = new PoolReturnListener();
        var cursor = new WriteCursor(null, "root");
        try
        {
            int firstRental = returns.LastRental;
            int capacity = returns.LastRentalLength;
            Assert.IsGreaterThan(0, capacity);
            returns.Watch(firstRental);
            cursor.Reserve(capacity, "prefix", "uint8").Fill(17);
            cursor.Reserve(1, "tail", "uint8")[0] = 29;
            int replacementRental = returns.LastRental;
            Assert.IsTrue(returns.Returned, "Growth returns the old buffer rather than retaining two rentals.");
            Assert.AreNotEqual(firstRental, replacementRental);
            Assert.AreEqual(17, cursor.Written[0]);
            Assert.AreEqual(29, cursor.Written[^1]);
            returns.Watch(replacementRental);
            Assert.IsFalse(returns.Returned);
        }
        finally
        {
            cursor.Dispose();
        }

        Assert.IsTrue(returns.Returned, "Disposal returns the replacement buffer that still belonged to the cursor.");
        Assert.IsFalse(cursor.IsGrowable);
    }
}
