namespace CStructSharp.Tests;

using CStructSharp.Streams;

/// <summary>Verifies sparse reads stop at the captured extent and combine adjacent baseline gaps.</summary>
[TestClass]
public class SparseReadBoundaryTests
{
    /// <summary>A very large virtual position returns end-of-input without arithmetic wraparound or baseline access.</summary>
    [TestMethod]
    public void FarPastEnd_DoesNotReadOrRepositionBaseline()
    {
        using var baseline = new ObservedSource();
        using var staging = new SparseUpdateStream(baseline, 0);
        baseline.Position = 1;
        staging.Position = long.MaxValue;
        Assert.AreEqual(0, staging.Read(new byte[1]));
        Assert.AreEqual(long.MaxValue, staging.Position);
        Assert.AreEqual(1L, baseline.Position);
        Assert.HasCount(0, baseline.ReadLengths);
    }

    /// <summary>A byte read exactly at the captured end requires no physical read or cursor movement.</summary>
    [TestMethod]
    public void ExactEnd_DoesNotReadBaselineByte()
    {
        using var baseline = new ObservedSource();
        using var staging = new SparseUpdateStream(baseline, 3);
        baseline.Position = 1;
        Assert.AreEqual(-1, staging.ReadByte());
        Assert.AreEqual(0, baseline.ByteReads);
        Assert.AreEqual(1L, baseline.Position);
        Assert.AreEqual(3L, staging.Position);
    }

    /// <summary>One contiguous baseline gap is read once, requesting only bytes before the captured end.</summary>
    [TestMethod]
    public void BaselineGap_IsOneExtentBoundedRead()
    {
        using var baseline = new ObservedSource();
        using var staging = new SparseUpdateStream(baseline, 1);
        byte[] bytes = [99, 99, 99, 99,];
        Assert.AreEqual(2, staging.Read(bytes));
        CollectionAssert.AreEqual(new byte[] { 12, 13, 99, 99, }, bytes);
        CollectionAssert.AreEqual(new int[] { 2, }, baseline.ReadLengths);
        Assert.AreEqual(3L, staging.Position);
    }

    /// <summary>Records baseline I/O so speculative reads and fragmented gap requests remain observable.</summary>
    private sealed class ObservedSource : MemoryStream
    {
        /// <summary>Creates a three-byte, caller-owned source.</summary>
        public ObservedSource()
            : base([11, 12, 13,])
        {
        }

        /// <summary>Gets the number of single-byte reads.</summary>
        public int ByteReads { get; private set; }

        /// <summary>Gets the requested lengths of physical block reads.</summary>
        public List<int> ReadLengths { get; } = [];

        /// <summary>Records a block request before returning available bytes.</summary>
        /// <param name="buffer">Destination span whose length is the requested byte count.</param>
        /// <returns>The number of bytes copied from the source.</returns>
        public override int Read(Span<byte> buffer)
        {
            this.ReadLengths.Add(buffer.Length);
            return base.Read(buffer);
        }

        /// <summary>Records a single-byte request before reading.</summary>
        /// <returns>The next byte, or minus one at end-of-input.</returns>
        public override int ReadByte()
        {
            this.ByteReads++;
            return base.ReadByte();
        }
    }
}
