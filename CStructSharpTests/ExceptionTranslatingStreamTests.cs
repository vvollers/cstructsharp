namespace CStructSharp.Tests;

/// <summary>Exercises every physical I/O translation performed by the read and write budget stream boundaries.</summary>
[TestClass]
public class ExceptionTranslatingStreamTests
{
    /// <summary>Names each Stream member that can produce a physical failure in the translation matrix.</summary>
    private enum FaultPoint
    {
        None,
        Length,
        PositionGet,
        PositionSet,
        Flush,
        ReadArray,
        ReadSpan,
        ReadByte,
        Seek,
        SetLength,
        WriteArray,
        WriteSpan,
        WriteByte,
    }

    /// <summary>Wraps each source-stream I/O shape as a read failure and retains the physical cause.</summary>
    [TestMethod]
    public void ReadBudgetStream_TranslatesEveryPhysicalIoShape()
    {
        (FaultPoint Point, Action<ReadBudgetStream> Operation)[] cases =
        [
            (FaultPoint.Length, stream => _ = stream.Length),
            (FaultPoint.PositionGet, stream => _ = stream.Position),
            (FaultPoint.PositionSet, stream => stream.Position = 1),
            (FaultPoint.Flush, stream => stream.Flush()),
            (FaultPoint.ReadArray, stream => _ = stream.Read(new byte[1], 0, 1)),
            (FaultPoint.ReadSpan, stream => _ = stream.Read(new byte[1].AsSpan())),
            (FaultPoint.ReadByte, stream => _ = stream.ReadByte()),
            (FaultPoint.Seek, stream => _ = stream.Seek(0, SeekOrigin.Begin)),
        ];

        foreach ((FaultPoint point, Action<ReadBudgetStream> operation) in cases)
        {
            var cause = new IOException("injected " + point);
            using var inner = new SelectiveFaultStream(cause) { Point = point, };
            using var stream = new ReadBudgetStream(inner, new ReadOptions());

            CStructReadException failure = Assert.Throws<CStructReadException>(
                () => operation(stream),
                point.ToString());
            Assert.AreSame(cause, failure.InnerException, point.ToString());
            Assert.AreEqual(CStructErrorCode.ReadFailed, failure.Code, point.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure.Message), point.ToString());
            Assert.AreEqual(point == FaultPoint.PositionGet ? null : 0L, failure.Offset, point.ToString());
        }
    }

    /// <summary>Distinguishes existing-data reads from destination writes for every forwarded writer operation.</summary>
    [TestMethod]
    public void WriteBudgetStream_TranslatesEveryPhysicalIoShape()
    {
        var constructorCause = new IOException("injected constructor length");
        using (var inner = new SelectiveFaultStream(constructorCause) { Point = FaultPoint.Length, })
        {
            CStructWriteException failure = Assert.Throws<CStructWriteException>(
                () => new WriteBudgetStream(inner, new WriteOptions()));
            Assert.AreSame(constructorCause, failure.InnerException);
            Assert.AreEqual(0L, failure.Offset);
        }

        (FaultPoint Point, bool IsRead, Action<WriteBudgetStream> Operation)[] cases =
        [
            (FaultPoint.Length, false, stream => _ = stream.Length),
            (FaultPoint.PositionGet, false, stream => _ = stream.Position),
            (FaultPoint.PositionSet, false, stream => stream.Position = 1),
            (FaultPoint.Flush, false, stream => stream.Flush()),
            (FaultPoint.ReadArray, true, stream => _ = stream.Read(new byte[1], 0, 1)),
            (FaultPoint.ReadSpan, true, stream => _ = stream.Read(new byte[1].AsSpan())),
            (FaultPoint.ReadByte, true, stream => _ = stream.ReadByte()),
            (FaultPoint.Seek, false, stream => _ = stream.Seek(0, SeekOrigin.Begin)),
            (FaultPoint.SetLength, false, stream => stream.SetLength(1)),
            (FaultPoint.WriteArray, false, stream => stream.Write(new byte[1], 0, 1)),
            (FaultPoint.WriteSpan, false, stream => stream.Write(new byte[1].AsSpan())),
            (FaultPoint.WriteByte, false, stream => stream.WriteByte(1)),
        ];

        foreach ((FaultPoint point, bool isRead, Action<WriteBudgetStream> operation) in cases)
        {
            var cause = new IOException("injected " + point);
            using var inner = new SelectiveFaultStream(cause);
            using var stream = new WriteBudgetStream(inner, new WriteOptions());
            inner.Point = point;

            CStructException failure = isRead
                                           ? Assert.Throws<CStructReadException>(() => operation(stream), point.ToString())
                                           : Assert.Throws<CStructWriteException>(() => operation(stream), point.ToString());
            Assert.AreSame(cause, failure.InnerException, point.ToString());
            Assert.AreEqual(
                isRead ? CStructErrorCode.ReadFailed : CStructErrorCode.WriteFailed,
                failure.Code,
                point.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(failure.Message), point.ToString());
            Assert.AreEqual(point == FaultPoint.PositionGet ? null : 0L, failure.Offset, point.ToString());
        }
    }

    /// <summary>Behaves like a tiny seekable stream except at one explicitly selected operation.</summary>
    private sealed class SelectiveFaultStream(IOException cause) : Stream
    {
        private long length;
        private long position;

        public FaultPoint Point { get; set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length =>
            this.Point == FaultPoint.Length ? throw cause : this.length;

        public override long Position
        {
            get => this.Point == FaultPoint.PositionGet ? throw cause : this.position;
            set
            {
                if (this.Point == FaultPoint.PositionSet)
                {
                    throw cause;
                }

                this.position = value;
            }
        }

        public override void Flush()
        {
            if (this.Point == FaultPoint.Flush)
            {
                throw cause;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.Point == FaultPoint.ReadArray)
            {
                throw cause;
            }

            return 0;
        }

        public override int Read(Span<byte> buffer)
        {
            if (this.Point == FaultPoint.ReadSpan)
            {
                throw cause;
            }

            return 0;
        }

        public override int ReadByte()
        {
            return this.Point == FaultPoint.ReadByte ? throw cause : -1;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (this.Point == FaultPoint.Seek)
            {
                throw cause;
            }

            this.position = offset;
            return this.position;
        }

        public override void SetLength(long value)
        {
            if (this.Point == FaultPoint.SetLength)
            {
                throw cause;
            }

            this.length = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (this.Point == FaultPoint.WriteArray)
            {
                throw cause;
            }

            this.position += count;
            this.length = Math.Max(this.length, this.position);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (this.Point == FaultPoint.WriteSpan)
            {
                throw cause;
            }

            this.position += buffer.Length;
            this.length = Math.Max(this.length, this.position);
        }

        public override void WriteByte(byte value)
        {
            if (this.Point == FaultPoint.WriteByte)
            {
                throw cause;
            }

            this.position++;
            this.length = Math.Max(this.length, this.position);
        }
    }
}
