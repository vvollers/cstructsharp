namespace CStructSharp.Tests;

using System.Collections;
using CStructSharp.Diagnostics;

/// <summary>Checks ownership of the original-byte snapshot retained during an asynchronous update.</summary>
[TestClass]
public class AsyncUpdateRentalTests
{
    /// <summary>The original-byte snapshot is returned after either successful staging or a value-validation failure.</summary>
    /// <param name="invalidValue">Whether to supply a number outside the selected byte field's range.</param>
    /// <returns>Completion after update and exact buffer-return assertions.</returns>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Update_ReturnsItsOriginalSnapshot(bool invalidValue)
    {
        var layout = new CStruct("typedef uint8 item;");
        using var destination = new MemoryStream(new byte[17]) { Position = 1, };
        using var returns = new PoolReturnListener();
        var variables = new ObservedVariables(returns);
        if (invalidValue)
        {
            // Validation fails after both the input rental and original-byte snapshot have been acquired.
            await Assert.ThrowsAsync<CStructWriteException>(async () =>
                await layout.UpdateAsync(destination, "item", 256, variables));
        }
        else
        {
            await layout.UpdateAsync(destination, "item", (byte)7, variables);
        }

        Assert.IsTrue(variables.Observed);
        Assert.IsTrue(returns.Returned, "The exact original-byte snapshot must be returned, not just the input buffer.");
        Assert.AreEqual(1L, destination.Position);
        byte[] expected = new byte[17];
        if (!invalidValue)
        {
            expected[1] = 7;
        }

        CollectionAssert.AreEqual(expected, destination.ToArray());
    }

    /// <summary>Observes the last rental when update validation first inspects caller variables.</summary>
    /// <param name="returns">Pool diagnostics already enabled before the update starts.</param>
    private sealed class ObservedVariables(PoolReturnListener returns) : IReadOnlyDictionary<string, int>
    {
        public bool Observed { get; private set; }

        /// <summary>Reports an empty variable set after identifying the update's original-byte snapshot.</summary>
        public int Count
        {
            get
            {
                if (!this.Observed)
                {
                    // Acquisition requests 17 bytes and receives a 32-byte bucket. The subsequent snapshot
                    // requests the actual 16-byte region. No staging rentals precede variable inspection.
                    Assert.AreEqual(16, returns.LastRentalLength);
                    Assert.AreNotEqual(0, returns.LastRental);
                    returns.Watch(returns.LastRental);
                    this.Observed = true;
                }

                return 0;
            }
        }

        public IEnumerable<string> Keys => Array.Empty<string>();

        public IEnumerable<int> Values => Array.Empty<int>();

        public int this[string key] => throw new KeyNotFoundException();

        /// <summary>Reports that the empty fixture contains no key.</summary>
        /// <param name="key">The unused candidate name.</param>
        /// <returns>False.</returns>
        public bool ContainsKey(string key) => false;

        /// <summary>Reports a missing value without inventing an override.</summary>
        /// <param name="key">The unused candidate name.</param>
        /// <param name="value">Zero, unused because the lookup fails.</param>
        /// <returns>False.</returns>
        public bool TryGetValue(string key, out int value)
        {
            value = 0;
            return false;
        }

        /// <summary>Enumerates no variable overrides.</summary>
        /// <returns>An empty key/value enumerator.</returns>
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
            => ((IEnumerable<KeyValuePair<string, int>>)Array.Empty<KeyValuePair<string, int>>()).GetEnumerator();

        /// <summary>Provides the same empty enumeration through the non-generic interface.</summary>
        /// <returns>An empty enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
