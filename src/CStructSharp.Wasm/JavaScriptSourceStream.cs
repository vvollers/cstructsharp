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

    /// <summary>Wraps a JavaScript source whose <c>size</c> property gives its length in bytes.</summary>
    /// <param name="source">
    ///     The JavaScript source object; the stream reads it through the <c>cstructsharp-source</c> <c>read</c> import
    ///     and does not dispose it.
    /// </param>
    /// <exception cref="ArgumentException"><c>size</c> is not a non-negative safe integer.</exception>
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

    /// <summary>Gets <see langword="true"/>: the source is readable.</summary>
    public override bool CanRead => true;

    /// <summary>Gets <see langword="true"/>: any position can be read by fetching its page.</summary>
    public override bool CanSeek => true;

    /// <summary>Gets <see langword="false"/>: the source is read-only.</summary>
    public override bool CanWrite => false;

    /// <summary>Gets the source size in bytes, fixed when the stream was created.</summary>
    public override long Length => this.length;

    /// <summary>Gets or sets the current byte position; a position past the end reads nothing.</summary>
    /// <exception cref="IOException">The value is negative.</exception>
    public override long Position
    {
        get => this.position;
        set => this.position = value >= 0 ? value : throw new IOException("Cannot seek before the source.");
    }

    /// <summary>
    ///     Copies bytes at the current position into part of an array; see <see cref="Read(Span{byte})"/>.
    /// </summary>
    /// <param name="buffer">The destination array.</param>
    /// <param name="offset">The first array index to fill.</param>
    /// <param name="count">The greatest number of bytes to copy.</param>
    /// <returns>The number of bytes copied; 0 at the end of the source.</returns>
    public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

    /// <summary>
    ///     Copies bytes at the current position from the cached 64 KiB page, fetching that page from JavaScript first
    ///     when the position lies outside it, and advances the position. One call never crosses a page boundary.
    /// </summary>
    /// <param name="buffer">The destination bytes.</param>
    /// <returns>The number of bytes copied, at most the page remainder; 0 at the end of the source.</returns>
    /// <exception cref="IOException">JavaScript returned fewer bytes than the page requested.</exception>
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

    /// <summary>Reads one byte at the current position and advances past it.</summary>
    /// <returns>The byte value, or -1 at the end of the source.</returns>
    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return this.Read(value) == 0 ? -1 : value[0];
    }

    /// <summary>Moves the current position relative to the start, the current position, or the end.</summary>
    /// <param name="offset">The signed byte distance from <paramref name="origin"/>.</param>
    /// <param name="origin">The reference point of <paramref name="offset"/>.</param>
    /// <returns>The new position.</returns>
    /// <exception cref="IOException">The resulting position is negative.</exception>
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

    /// <summary>Does nothing: the stream never writes.</summary>
    public override void Flush()
    {
    }

    /// <summary>Always throws: the source length is fixed.</summary>
    /// <param name="value">The requested length (unused).</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Always throws: the source is read-only.</summary>
    /// <param name="buffer">The bytes to write (unused).</param>
    /// <param name="offset">The first array index to write (unused).</param>
    /// <param name="count">The number of bytes to write (unused).</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    [JSImport("read", "cstructsharp-source")]
    private static partial byte[] ReadPage(JSObject source, double offset, int count);
}
