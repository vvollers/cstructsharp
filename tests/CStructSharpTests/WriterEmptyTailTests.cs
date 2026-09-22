namespace CStructSharp.Tests;

using CStructSharp.Reading;

/// <summary>Checks that a general write with no tail bytes avoids unnecessary destination-position queries.</summary>
[TestClass]
public class WriterEmptyTailTests
{
    /// <summary>An empty record reads its entry position and, only when aligned, compares its final boundary once.</summary>
    /// <param name="aligned">Whether the layout requests a final alignment check.</param>
    /// <param name="expectedReads">The required number of physical position queries.</param>
    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(true, 2)]
    public void EmptyGeneralWrite_AvoidsRedundantPositionQueries(bool aligned, int expectedReads)
    {
        var layout = new CStruct("struct root {};", aligned: aligned);
        using var destination = new PositionCountingStream();
        bool previous = StaticReadPlan.DisabledForTesting;
        StaticReadPlan.DisabledForTesting = true;
        try
        {
            layout.Write(destination, "root", new Dictionary<string, object?>());
        }
        finally
        {
            StaticReadPlan.DisabledForTesting = previous;
        }

        Assert.AreEqual(expectedReads, destination.PositionReads);
        Assert.AreEqual(0L, destination.Length);
    }

    /// <summary>Counts physical position reads without changing normal seekable-memory-stream behavior.</summary>
    private sealed class PositionCountingStream : MemoryStream
    {
        /// <summary>Gets the number of queries made to the destination position.</summary>
        public int PositionReads { get; private set; }

        /// <summary>Gets the current byte position while counting the query, or sets it without counting.</summary>
        public override long Position
        {
            get
            {
                this.PositionReads++;
                return base.Position;
            }

            set => base.Position = value;
        }
    }
}
