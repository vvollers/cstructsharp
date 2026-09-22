namespace CStructSharp.Tests;

using CStructSharp.Streams;

/// <summary>Checks that optimized read preflights respect source capabilities, remaining bytes and cached memory extent.</summary>
[TestClass]
public class ReadPreflightSourceTests
{
    /// <summary>A known memory window uses its captured extent without repeatedly querying the caller's stream.</summary>
    [TestMethod]
    public void ExposedMemory_CachesItsLength()
    {
        using var source = new ObservedStream(exposed: true, seekable: true);
        using var reader = new ReadBudgetStream(source, 100, 100);
        int initialReads = source.LengthReads;
        Assert.AreEqual(3L, reader.Length);
        reader.Position = 1;
        Assert.AreEqual(3L, reader.Length);
        Assert.AreEqual(initialReads, source.LengthReads);
    }

    /// <summary>Length metadata does not make a forward-only stream eligible for seekable block preflight.</summary>
    [TestMethod]
    public void ForwardOnlyWithMetadata_DeclinesBlockWithoutReading()
    {
        using var source = new ObservedStream(exposed: false, seekable: false);
        using var reader = new ReadBudgetStream(source, 100, 100);
        Assert.IsFalse(reader.TryReadBlockWithinBudget(new byte[1]));
        Assert.AreEqual(0, source.ReadCalls);
        Assert.AreEqual(0L, source.Position);
    }

    /// <summary>An ample byte budget cannot hide a request larger than the source's remaining extent.</summary>
    [TestMethod]
    public void SeekableShortfall_DeclinesBlockWithoutReading()
    {
        using var source = new ObservedStream(exposed: false, seekable: true);
        source.Position = 1;
        using var reader = new ReadBudgetStream(source, 100, 100);
        Assert.IsFalse(reader.TryReadBlockWithinBudget(new byte[3]));
        Assert.AreEqual(0, source.ReadCalls);
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>Provides readable metadata independently of seeking and records physical read and length queries.</summary>
    private sealed class ObservedStream : MemoryStream
    {
        private readonly bool seekable;

        /// <summary>Creates a three-byte source with independently chosen exposure and seeking capabilities.</summary>
        /// <param name="exposed">Whether the backing array can be borrowed.</param>
        /// <param name="seekable">Whether callers may use seeking optimizations.</param>
        public ObservedStream(bool exposed, bool seekable)
            : base([11, 12, 13,], 0, 3, writable: false, publiclyVisible: exposed)
        {
            this.seekable = seekable;
        }

        /// <summary>Gets the declared seeking capability, independently of the available length metadata.</summary>
        public override bool CanSeek => this.seekable;

        /// <summary>Gets the source length and records each query.</summary>
        public override long Length
        {
            get
            {
                this.LengthReads++;
                return base.Length;
            }
        }

        /// <summary>Gets the number of source length queries.</summary>
        public int LengthReads { get; private set; }

        /// <summary>Gets the number of physical read calls.</summary>
        public int ReadCalls { get; private set; }

        /// <summary>Reads source bytes while recording the physical call.</summary>
        /// <param name="buffer">Destination span.</param>
        /// <returns>The number of bytes copied.</returns>
        public override int Read(Span<byte> buffer)
        {
            this.ReadCalls++;
            return base.Read(buffer);
        }

        /// <summary>Reads source bytes while recording the physical call.</summary>
        /// <param name="buffer">Destination array.</param>
        /// <param name="offset">First destination byte index.</param>
        /// <param name="count">Maximum bytes to copy.</param>
        /// <returns>The number of bytes copied.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            this.ReadCalls++;
            return base.Read(buffer, offset, count);
        }
    }
}
