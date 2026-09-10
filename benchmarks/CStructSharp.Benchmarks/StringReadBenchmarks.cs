namespace CStructSharp.Benchmarks;

using System.Dynamic;
using System.Text;
using BenchmarkDotNet.Attributes;

/// <summary>
///     Compares reading a large terminated string from a fully-buffered <see cref="MemoryStream"/> against reading
///     the same field from a stream that never returns more than a few bytes per physical <c>Read</c> call, similar
///     to a raw <see cref="System.Net.Sockets.NetworkStream"/> or pipe stream with no internal buffering. The
///     terminator scan chunks its own internal read requests regardless of the source, so both cases should benefit
///     from far fewer physical read calls than one per encoded byte.
/// </summary>
[BenchmarkCategory("StringRead")]
public class StringReadBenchmarks
{
    private CStruct terminatedStringLayout = null!;
    private byte[] largeStringBytes = null!;
    private MemoryStream bufferedStream = null!;
    private NonBufferingStream nonBufferingStream = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.terminatedStringLayout = new CStruct("struct root { ascii_string_zero value; };");
        string value = new string('A', 1024 * 1024);
        this.largeStringBytes = [.. Encoding.ASCII.GetBytes(value), 0x00,];
        this.bufferedStream = new MemoryStream(this.largeStringBytes, writable: false);
        this.nonBufferingStream = new NonBufferingStream(this.largeStringBytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.bufferedStream.Dispose();
        this.nonBufferingStream.Dispose();
    }

    [Benchmark(Baseline = true)]
    public ExpandoObject ParseLargeTerminatedStringFromMemoryStream()
    {
        this.bufferedStream.Position = 0;
        return this.terminatedStringLayout.ParseStream(this.bufferedStream, "root");
    }

    [Benchmark]
    public ExpandoObject ParseLargeTerminatedStringFromNonBufferingStream()
    {
        this.nonBufferingStream.Position = 0;
        return this.terminatedStringLayout.ParseStream(this.nonBufferingStream, "root");
    }

    /// <summary>
    ///     A minimal read-only, seekable stream that never returns more than a handful of bytes per physical
    ///     <see cref="Read(byte[], int, int)"/> call, standing in for a raw network or pipe stream that does not
    ///     buffer internally.
    /// </summary>
    private sealed class NonBufferingStream : Stream
    {
        private const int MaxBytesPerRead = 4;
        private readonly MemoryStream inner;

        public NonBufferingStream(byte[] bytes)
        {
            this.inner = new MemoryStream(bytes, writable: false);
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.inner.Read(buffer, offset, Math.Min(count, MaxBytesPerRead));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("This stream is read-only.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("This stream is read-only.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
