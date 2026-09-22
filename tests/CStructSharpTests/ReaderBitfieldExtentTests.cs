namespace CStructSharp.Tests;

/// <summary>Checks selected-bitfield extent arithmetic before any physical input is touched.</summary>
[TestClass]
public class ReaderBitfieldExtentTests
{
    /// <summary>A two-byte union view cannot start one byte below the largest stream position.</summary>
    [TestMethod]
    public void SelectedBitfield_RejectsAnUnrepresentableEndBeforeReading()
    {
        var layout = new CStruct("union root { uint16 bits : 3; };");
        using var source = new NearLimitSource();

        // The layout extent, not an allocated buffer, crosses Int64.MaxValue.
        Assert.Throws<OverflowException>(() => layout.ReadValue(source, "root.bits"));
        Assert.AreEqual(0, source.ReadCalls);
        Assert.AreEqual(long.MaxValue - 1, source.Position);
    }

    /// <summary>Advertises a near-limit cursor and rejects physical reads that preflight should prevent.</summary>
    private sealed class NearLimitSource : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => long.MaxValue;

        public override long Position { get; set; } = long.MaxValue - 1;

        /// <summary>Gets the number of physical reads attempted.</summary>
        public int ReadCalls { get; private set; }

        /// <summary>Has no buffered writes to flush.</summary>
        public override void Flush()
        {
        }

        /// <summary>Routes array reads through the same physical-read rejection.</summary>
        /// <param name="buffer">The caller's destination.</param>
        /// <param name="offset">The destination start.</param>
        /// <param name="count">The requested byte count.</param>
        /// <returns>Never returns because physical reads are forbidden by this probe.</returns>
        /// <exception cref="IOException">Every valid read request is rejected.</exception>
        public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

        /// <summary>Records an unexpected physical read and fails it.</summary>
        /// <param name="buffer">The requested destination window.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Every read fails so missing preflight cannot appear successful.</exception>
        public override int Read(Span<byte> buffer)
        {
            this.ReadCalls++;
            throw new IOException("A read started before validating the selected extent.");
        }

        /// <summary>Moves the synthetic cursor using checked stream-coordinate arithmetic.</summary>
        /// <param name="offset">The displacement in bytes.</param>
        /// <param name="origin">The displacement origin.</param>
        /// <returns>The new stream position.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The origin is unknown.</exception>
        /// <exception cref="OverflowException">The resulting position cannot fit in Int64.</exception>
        /// <exception cref="IOException">The resulting position is negative.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            long start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => this.Position,
                SeekOrigin.End => this.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            long position = checked(start + offset);
            if (position < 0)
            {
                throw new IOException("A stream position cannot be negative.");
            }

            this.Position = position;
            return this.Position;
        }

        /// <summary>Rejects resizing the read-only source.</summary>
        /// <param name="value">The unsupported length.</param>
        /// <exception cref="NotSupportedException">The source is read-only.</exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Rejects writes to the read-only source.</summary>
        /// <param name="buffer">The unsupported input bytes.</param>
        /// <param name="offset">The input start.</param>
        /// <param name="count">The input count.</param>
        /// <exception cref="NotSupportedException">The source is read-only.</exception>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
