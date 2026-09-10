namespace CStructSharp.Tests;

/// <summary>Exercises sparse copy-on-write behavior independently from compiled layout semantics.</summary>
[TestClass]
public class SparseUpdateStreamTests
{
    /// <summary>
    ///     This internal staging stream stores proposed changes before they reach the destination.
    /// </summary>
    /// <remarks>
    ///     It must report normal readable, seekable, writable capabilities while rejecting invalid positions, length
    ///     changes, and buffer arguments. A consistent stream contract is necessary because ordinary field writers
    ///     operate on this temporary layer.
    /// </remarks>
    [TestMethod]
    public void Contract_ValidatesCapabilitiesPositionsLengthsAndBuffers()
    {
        using var baseline = new TrackingStream(new byte[] { 1, 2, });
        using var staging = new SparseUpdateStream(baseline, 0);

        Assert.IsTrue(staging.CanRead);
        Assert.IsTrue(staging.CanSeek);
        Assert.IsTrue(staging.CanWrite);
        Assert.AreEqual(2L, staging.Length);
        Assert.Throws<CStructWriteException>(() => new SparseUpdateStream(baseline, 3));
        Assert.Throws<CStructWriteException>(() => staging.Position = -1);
        Assert.Throws<ArgumentNullException>(() => staging.CommitTo(null!));
        Assert.Throws<ArgumentNullException>(() => staging.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.Read(new byte[1], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.Read(new byte[1], 0, -1));
        Assert.Throws<ArgumentException>(() => staging.Read(new byte[1], 1, 1));
        Assert.Throws<ArgumentNullException>(() => staging.Write(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.Write(new byte[1], -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.Write(new byte[1], 0, -1));
        Assert.Throws<ArgumentException>(() => staging.Write(new byte[1], 1, 1));
        staging.SetLength(2);
        Assert.Throws<CStructWriteException>(() => staging.SetLength(1));
    }

    /// <summary>
    ///     Seeking from the beginning, current position, or end must produce ordinary absolute offsets within the
    ///     staging view.
    /// </summary>
    /// <remarks>
    ///     Negative targets, invalid origins, and arithmetic overflow must fail. Staging must not change the meaning of
    ///     a field address merely because it stores changes separately.
    /// </remarks>
    [TestMethod]
    public void Seek_UsesVirtualAbsoluteCoordinates()
    {
        using var baseline = new MemoryStream(new byte[3]);
        using var staging = new SparseUpdateStream(baseline, 0);

        Assert.AreEqual(1L, staging.Seek(1, SeekOrigin.Begin));
        Assert.AreEqual(2L, staging.Seek(1, SeekOrigin.Current));
        Assert.AreEqual(1L, staging.Seek(-2, SeekOrigin.End));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.Seek(0, (SeekOrigin)99));
        Assert.Throws<CStructWriteException>(() => staging.Seek(-1, SeekOrigin.Begin));
        staging.Position = 1;
        Assert.Throws<CStructWriteException>(() => staging.Seek(long.MaxValue, SeekOrigin.Current));
    }

    /// <summary>
    ///     Overlapping staged writes must use the most recent bytes, producing 0,10,20,21,4,5,99,7 when read back.
    /// </summary>
    /// <remarks>
    ///     The baseline remains unwritten until commit. Commit combines adjacent changed bytes into ranges starting at
    ///     1 and 6, rather than replaying overwritten intermediate values.
    /// </remarks>
    [TestMethod]
    public void OverlayAndCommit_UseLastWriteWinsCoalescedRanges()
    {
        using var baseline = new TrackingStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, });
        using var staging = new SparseUpdateStream(baseline, 1);
        staging.Write(new byte[] { 10, 11, 12, });
        staging.Position = 2;
        staging.Write(new byte[] { 20, 21, });
        staging.Position = 6;
        staging.WriteByte(99);

        staging.Position = 0;
        var overlay = new byte[8];
        staging.ReadExactly(overlay);
        CollectionAssert.AreEqual(new byte[] { 0, 10, 20, 21, 4, 5, 99, 7, }, overlay);
        Assert.AreEqual(0, baseline.WriteCalls);

        using var destination = new TrackingStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, });
        staging.CommitTo(destination);

        CollectionAssert.AreEqual(overlay, destination.Snapshot());
        CollectionAssert.AreEqual(new long[] { 1, 6, }, destination.WriteStarts);
        CollectionAssert.AreEqual(new int[] { 3, 1, }, destination.WriteLengths);
        Assert.AreEqual(0, destination.FlushCalls);
    }

    /// <summary>
    ///     The baseline has two bytes, so writing at its end or increasing its length would extend existing storage.
    /// </summary>
    /// <remarks>
    ///     Both requests must fail and make zero underlying writes. This is the mechanism that keeps UpdateStream a
    ///     replacement operation rather than an append operation.
    /// </remarks>
    [TestMethod]
    public void Write_RejectsExtensionAndPreservesBaseline()
    {
        using var baseline = new TrackingStream(new byte[] { 1, 2, });
        using var staging = new SparseUpdateStream(baseline, 2);

        Assert.Throws<CStructWriteException>(() => staging.WriteByte(3));
        Assert.Throws<CStructWriteException>(() => staging.SetLength(3));

        CollectionAssert.AreEqual(new byte[] { 1, 2, }, baseline.Snapshot());
        Assert.AreEqual(0, baseline.WriteCalls);
    }

    /// <summary>
    ///     A staged replacement changes the middle byte from 2 to 9.
    /// </summary>
    /// <remarks>
    ///     Reading the combined view must return 1,9,3 and fetch only offsets 0 and 2 from the baseline. Disposing the
    ///     staging view must leave the caller-owned baseline open.
    /// </remarks>
    [TestMethod]
    public void Read_UsesStagedBytesAndOnlyReadsBaselineGaps()
    {
        using var baseline = new TrackingStream(new byte[] { 1, 2, 3, });
        var staging = new SparseUpdateStream(baseline, 1);
        staging.WriteByte(9);
        staging.Position = 0;

        var bytes = new byte[3];
        Assert.AreEqual(3, staging.Read(bytes, 0, bytes.Length));
        CollectionAssert.AreEqual(new byte[] { 1, 9, 3, }, bytes);
        CollectionAssert.AreEqual(new long[] { 0, 2, }, baseline.ReadStarts);

        staging.Dispose();
        Assert.IsTrue(baseline.CanRead);
    }

    /// <summary>
    ///     Single-byte reads must see original 1, staged 9, and original 3 in sequence.
    /// </summary>
    /// <remarks>
    ///     The next read returns -1 at end of stream, while bulk reads there return zero. The cursor must remain at
    ///     length 3 rather than advancing past the end.
    /// </remarks>
    [TestMethod]
    public void ReadByte_AdvancesAcrossBaselineOverlayAndEnd()
    {
        using var baseline = new MemoryStream(new byte[] { 1, 2, 3, });
        using var staging = new SparseUpdateStream(baseline, 1);
        staging.WriteByte(9);
        staging.Position = 0;

        Assert.AreEqual(1, staging.ReadByte());
        Assert.AreEqual(9, staging.ReadByte());
        Assert.AreEqual(3, staging.ReadByte());
        Assert.AreEqual(-1, staging.ReadByte());
        Assert.AreEqual(3L, staging.Position);
        Assert.AreEqual(0, staging.Read(Span<byte>.Empty));
        Assert.AreEqual(0, staging.Read(new byte[1], 0, 1));
    }

    /// <summary>
    ///     Three adjacent changed bytes start at 1023 and cross an internal storage-chunk boundary.
    /// </summary>
    /// <remarks>
    ///     Commit must still write them as one range, with the isolated change at 2049 kept separate. Internal chunking
    ///     must not alter the final bytes or split logically adjacent output unnecessarily.
    /// </remarks>
    [TestMethod]
    public void Commit_CoalescesAcrossChunkBoundaries()
    {
        using var baseline = new MemoryStream(new byte[2052]);
        using var staging = new SparseUpdateStream(baseline, 1023);
        staging.Write(new byte[] { 1, 2, 3, });
        staging.Position = 2049;
        staging.WriteByte(9);
        using var destination = new TrackingStream(new byte[2052]);

        staging.CommitTo(destination);

        CollectionAssert.AreEqual(new long[] { 1023, 2049, }, destination.WriteStarts);
        CollectionAssert.AreEqual(new int[] { 3, 1, }, destination.WriteLengths);
        byte[] result = destination.Snapshot();
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, result[1023..1026]);
        Assert.AreEqual(9, result[2049]);
    }

    /// <summary>
    ///     The destination accepts the first changed range but throws on the second.
    /// </summary>
    /// <remarks>
    ///     The first change must remain, the second must not appear, and the original I/O exception must be retained.
    ///     Staging prevents validation failures from mutating data, but cannot guarantee rollback after a physical
    ///     destination failure.
    /// </remarks>
    [TestMethod]
    public void CommitFailure_BetweenRangesRetainsOnlyCommittedPrefix()
    {
        using var baseline = new MemoryStream(new byte[6]);
        using var staging = new SparseUpdateStream(baseline, 1);
        staging.WriteByte(0xA1);
        staging.Position = 4;
        staging.WriteByte(0xA4);
        var cause = new IOException("injected second commit failure");
        using var destination = new TrackingStream(new byte[6]) { Failure = cause, FailWriteCall = 2, };

        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => staging.CommitTo(destination));

        Assert.AreSame(cause, failure.InnerException);
        CollectionAssert.AreEqual(new byte[] { 0, 0xA1, 0, 0, 0, 0, }, destination.Snapshot());
        Assert.AreEqual(2, destination.WriteCalls);
    }

    /// <summary>
    ///     Commit tries to position the destination at offset 2, but the stream throws before writing.
    /// </summary>
    /// <remarks>
    ///     The error must preserve that seek failure and attempted offset even if reading diagnostic position also
    ///     fails. No destination bytes may change because the first physical write was never reached.
    /// </remarks>
    [TestMethod]
    public void CommitFailure_WhileSeekingRetainsCauseAndAttemptedOffset()
    {
        using var baseline = new MemoryStream(new byte[4]);
        using var staging = new SparseUpdateStream(baseline, 2);
        staging.WriteByte(0xA2);
        var cause = new IOException("injected commit seek failure");
        using var destination = new TrackingStream(new byte[4])
        {
            PositionFailure = cause,
            PositionReadFailure = new IOException("injected diagnostic position failure"),
        };

        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => staging.CommitTo(destination));

        Assert.AreSame(cause, failure.InnerException);
        Assert.AreEqual(2L, failure.Offset);
        Assert.AreEqual(0, destination.WriteCalls);
        CollectionAssert.AreEqual(new byte[4], destination.Snapshot());
    }

    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream inner = new();

        public TrackingStream(byte[] bytes)
        {
            this.inner.Write(bytes, 0, bytes.Length);
            this.inner.Position = 0;
        }

        public Exception? Failure { get; init; }

        public int FailWriteCall { get; init; } = -1;

        public Exception? PositionFailure { get; init; }

        public Exception? PositionReadFailure { get; init; }

        public int FlushCalls { get; private set; }

        public List<int> WriteLengths { get; } = [];

        public List<long> WriteStarts { get; } = [];

        public List<long> ReadStarts { get; } = [];

        public int WriteCalls { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.PositionReadFailure is null ? this.inner.Position : throw this.PositionReadFailure;
            set
            {
                if (this.PositionFailure is not null)
                {
                    throw this.PositionFailure;
                }

                this.inner.Position = value;
            }
        }

        public override void Flush()
        {
            this.FlushCalls++;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            this.ReadStarts.Add(this.inner.Position);
            return this.inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            this.ReadStarts.Add(this.inner.Position);
            return this.inner.Read(buffer);
        }

        public override int ReadByte()
        {
            this.ReadStarts.Add(this.inner.Position);
            return this.inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteStarts.Add(this.inner.Position);
            this.WriteLengths.Add(count);
            this.WriteCalls++;
            if (this.WriteCalls == this.FailWriteCall)
            {
                throw this.Failure ?? new IOException("injected commit failure");
            }

            this.inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] copy = buffer.ToArray();
            this.Write(copy, 0, copy.Length);
        }

        public byte[] Snapshot()
        {
            return this.inner.ToArray();
        }
    }
}
