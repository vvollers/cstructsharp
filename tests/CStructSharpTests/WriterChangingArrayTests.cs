namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that a caller-owned list cannot grow past the output limit between count observations.</summary>
[TestClass]
public class WriterChangingArrayTests
{
    /// <summary>The final data-sized count is checked again before any element is written.</summary>
    /// <param name="suffix">The terminated or end-of-input array declarator.</param>
    [TestMethod]
    [DataRow("[]")]
    [DataRow("[EOF]")]
    public void ChangingListCount_IsCheckedBeforeWriting(string suffix)
    {
        var layout = new CStruct("struct root { uint8 values" + suffix + "; };");
        var values = new GrowingList();
        using var destination = new MemoryStream();

        // The first materialization count fits; the following count exposes the second available element.
        Assert.Throws<CStructWriteLimitException>(() => layout.Write(
            destination,
            "root",
            new Dictionary<string, object?> { ["values"] = values, },
            options: new WriteOptions { MaxArrayElements = 1 }));
        Assert.AreEqual(0L, destination.Length);
    }

    /// <summary>Models a caller-owned collection that adds an element when its count is first queried.</summary>
    private sealed class GrowingList : List<object>, IList<object>
    {
        private bool observed;

        /// <summary>Starts with one byte and adds a second byte after the first count observation.</summary>
        public GrowingList()
            : base([(byte)5,])
        {
        }

        /// <summary>Returns the current count, adding one element after its first observation.</summary>
        int ICollection<object>.Count
        {
            get
            {
                int count = this.Count;
                if (!this.observed)
                {
                    this.observed = true;
                    this.Add((byte)6);
                }

                return count;
            }
        }
    }
}
