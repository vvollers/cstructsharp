namespace CStructSharp.Tests;

/// <summary>Checks cancellation requested by caller data between an enclosing record and a nested composite.</summary>
[TestClass]
public class WriterNestedCancellationTests
{
    /// <summary>A nested composite observes cancellation before its static plan can write the nested bytes.</summary>
    [TestMethod]
    public void NestedComposite_CancelsAfterItsValueIsRetrieved()
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { uint8 count; uint8 padding[count]; child nested; };");
        using var cancellation = new CancellationTokenSource();
        var data = new CancellingDictionary(cancellation);
        using var destination = new MemoryStream();

        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() =>
            layout.Write(destination, "root", data, options: new WriteOptions { CancellationToken = cancellation.Token, }));

        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        CollectionAssert.AreEqual(new byte[] { 0, }, destination.ToArray());
    }

    /// <summary>Requests cancellation only when the writer reaches the nested member, after writing the count.</summary>
    private sealed class CancellingDictionary : Dictionary<string, object?>, IDictionary<string, object?>
    {
        private readonly CancellationTokenSource cancellation;

        /// <summary>Creates an ordinary root value whose nested-member lookup requests cancellation.</summary>
        /// <param name="cancellation">The cancellation source owned by the test.</param>
        public CancellingDictionary(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
            this["count"] = (byte)0;
            this["padding"] = Array.Empty<byte>();
            this["nested"] = new Dictionary<string, object?> { ["value"] = (byte)7, };
        }

        /// <summary>Returns the requested member and cancels immediately before returning the nested value.</summary>
        /// <param name="key">The member requested by the writer.</param>
        /// <param name="value">The stored value, or null for an absent member.</param>
        /// <returns>Whether the requested member exists.</returns>
        bool IDictionary<string, object?>.TryGetValue(string key, out object? value)
        {
            if (key == "nested")
            {
                this.cancellation.Cancel();
            }

            return this.TryGetValue(key, out value);
        }
    }
}
