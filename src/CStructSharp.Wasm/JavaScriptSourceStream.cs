namespace CStructSharpWeb.Wasm;

using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

/// <summary>A read-only seekable source with one bounded page cached in managed memory.</summary>
[SupportedOSPlatform("browser")]
internal sealed partial class JavaScriptSourceStream : Stream
{
    private const int PageSize = 64 * 1024;
    private readonly JSObject source;
    private readonly long length;
    private byte[] page = [];
    private long pageStart = -1;
    private long position;

    public JavaScriptSourceStream(JSObject source)
    {
        double size = source.GetPropertyAsDouble("size");
        if (!double.IsFinite(size) || size < 0 || size > 9007199254740991d || Math.Truncate(size) != size)
        {
            throw new ArgumentException("Source size must be a non-negative safe integer.", nameof(source));
        }

        this.source = source;
        this.length = (long)size;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => this.length;

    public override long Position
    {
        get => this.position;
        set => this.position = value >= 0 ? value : throw new IOException("Cannot seek before the source.");
    }

    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || this.position >= this.length)
        {
            return 0;
        }

        long start = this.position / PageSize * PageSize;
        if (this.pageStart != start)
        {
            int count = (int)Math.Min(PageSize, this.length - start);
            this.page = ReadPage(this.source, start, count);
            if (this.page.Length != count)
            {
                throw new IOException("The binary source returned an incomplete page.");
            }

            this.pageStart = start;
        }

        int pageOffset = (int)(this.position - start);
        int copied = Math.Min(buffer.Length, this.page.Length - pageOffset);
        this.page.AsSpan(pageOffset, copied).CopyTo(buffer);
        this.position += copied;
        return copied;
    }

    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return this.Read(value) == 0 ? -1 : value[0];
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long basis = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => this.position,
            SeekOrigin.End => this.length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        this.Position = checked(offset + basis);
        return this.Position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    [JSImport("read", "cstructsharp-source")]
    private static partial byte[] ReadPage(JSObject source, double offset, int count);
}
