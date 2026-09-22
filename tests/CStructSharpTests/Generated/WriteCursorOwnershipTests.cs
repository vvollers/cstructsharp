namespace CStructSharp.Tests.Generated;

using CStructSharp.Generated;

/// <summary>Checks the writer's pooled buffer lifetime across growth and final disposal.</summary>
[TestClass]
public class WriteCursorOwnershipTests
{
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
