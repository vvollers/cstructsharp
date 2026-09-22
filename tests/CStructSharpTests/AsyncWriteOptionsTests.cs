namespace CStructSharp.Tests;

/// <summary>Checks that an async write without cancellation retains the synchronous option-snapshot behavior.</summary>
[TestClass]
public class AsyncWriteOptionsTests
{
    /// <summary>A non-cancellable wrapper does not invoke a caller-defined record copy constructor an extra time.</summary>
    /// <returns>Completion after the write and comparison with synchronous snapshot behavior.</returns>
    [TestMethod]
    public async Task NonCancellableWrite_DoesNotAddAnOptionsCopy()
    {
        var layout = new CStruct("typedef uint8 item;");
        int[] copies = [0,];
        var options = new ObservedOptions(copies);
        byte[] expected = layout.Serialize("item", (byte)7, options: options);
        int synchronousCopies = copies[0];
        Assert.IsGreaterThan(0, synchronousCopies);

        copies[0] = 0;
        using var destination = new MemoryStream();
        await layout.WriteAsync(destination, "item", (byte)7, options: options);

        // Compare with the synchronous operation instead of hard-coding its internal number of snapshots.
        Assert.AreEqual(synchronousCopies, copies[0]);
        CollectionAssert.AreEqual(expected, destination.ToArray());
    }

    /// <summary>A caller-defined options record whose ordinary copy constructor has an observable side effect.</summary>
    private sealed record ObservedOptions : WriteOptions
    {
        /// <summary>Creates the immutable options while retaining a shared diagnostic counter.</summary>
        /// <param name="copies">A one-element counter incremented by each copy constructor call.</param>
        public ObservedOptions(int[] copies)
        {
            this.Copies = copies;
        }

        /// <summary>Preserves base options and records the caller-observable copy.</summary>
        /// <param name="original">The options being snapshotted.</param>
        private ObservedOptions(ObservedOptions original)
            : base(original)
        {
            this.Copies = original.Copies;
            this.Copies[0]++;
        }

        private int[] Copies { get; }
    }
}
