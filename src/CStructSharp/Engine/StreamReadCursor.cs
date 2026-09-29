namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Reading;
using CStructSharp.Streams;

/// <summary>
///     The cursor over the operation's <see cref="ReadBudgetStream"/>, for every source the engine does not read from
///     memory: files, chunked and wrapper streams, hidden-buffer memory streams. Each member makes exactly the calls the
///     interpreter makes for the same step, so chunk granularity, charges, failures and final positions are identical
///     because the code is the same.
/// </summary>
internal readonly struct StreamReadCursor : IReadCursor
{
    private readonly ReadBudgetStream stream;

    /// <summary>Wraps the operation's budget stream; the stream stays owned by the operation.</summary>
    /// <param name="stream">The operation's budget stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public StreamReadCursor(ReadBudgetStream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <inheritdoc/>
    public long Position
    {
        get => this.stream.Position;
        set => this.stream.Position = value;
    }

    /// <inheritdoc/>
    public long Length => this.stream.Length;

    /// <inheritdoc/>
    public long MaxStringBytes => this.stream.MaxStringBytes;

    /// <inheritdoc/>
    public CancellationToken CancellationToken => this.stream.CancellationToken;

    /// <inheritdoc/>
    public void ThrowIfCancellationRequested() => this.stream.CancellationToken.ThrowIfCancellationRequested();

    /// <inheritdoc/>
    public void Skip(long count) => this.stream.Position = checked(this.stream.Position + count);

    /// <inheritdoc/>
    public void Align(long origin, int alignment)
        => this.stream.Position = origin + LayoutMath.AlignUp(this.stream.Position - origin, alignment);

    /// <inheritdoc/>
    public bool IsShortBy(long count) => this.stream.IsShortBy(count);

    /// <inheritdoc/>
    public bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes) => this.stream.TryReadSpan(count, out bytes);

    /// <inheritdoc/>
    public bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes) => this.stream.TryReadSpanWithinBudget(count, out bytes);

    /// <inheritdoc/>
    public bool TryReadBlockWithinBudget(Span<byte> destination) => this.stream.TryReadBlockWithinBudget(destination);

    /// <inheritdoc/>
    public bool TryPeekRemaining(out ReadOnlySpan<byte> bytes) => this.stream.TryPeekRemaining(out bytes);

    /// <inheritdoc/>
    public void Advance(int count) => this.stream.Advance(count);

    /// <inheritdoc/>
    public byte ReadByteExactly() => BinaryPrimitiveIO.ReadByteExactly(this.stream);

    /// <inheritdoc/>
    public void ReadExactly(Span<byte> destination) => BinaryPrimitiveIO.ReadExactlyOrThrow(this.stream, destination);

    /// <inheritdoc/>
    public ReadOnlySpan<byte> ReadFixed(Span<byte> scratch)
    {
        // The interpreter's order: a memory-backed stream serves the value in place; otherwise the codec's reader,
        // which takes one byte through ReadByte and a wider value through ReadExactly.
        if (this.stream.TryReadSpan(scratch.Length, out ReadOnlySpan<byte> direct))
        {
            return direct;
        }

        if (scratch.Length == 1)
        {
            scratch[0] = BinaryPrimitiveIO.ReadByteExactly(this.stream);
        }
        else
        {
            BinaryPrimitiveIO.ReadExactlyOrThrow(this.stream, scratch);
        }

        return scratch;
    }

    /// <inheritdoc/>
    public IList<object?> ReadPrimitiveArray(PrimitiveCodec codec, int count) => PrimitiveArrayReader.Read(this.stream, codec, count);

    /// <inheritdoc/>
    public string ReadTerminatedString(Encoding encoding, char terminator) => PrimitiveCodecs.ReadIntoString(this.stream, encoding, terminator);

    /// <inheritdoc/>
    public string ReadBoundedText(int byteCount, string type) => PrimitiveCodecs.ReadBoundedText(this.stream, byteCount, type);

    /// <inheritdoc/>
    public void FlushPosition() => this.stream.FlushPosition();
}
