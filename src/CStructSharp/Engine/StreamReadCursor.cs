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
///     memory: files, chunked and wrapper streams, hidden-buffer memory streams. Each member calls the stream's own
///     reads, so chunk granularity, charges, failures and final positions are the stream's.
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
    public void Charge(long count) => this.stream.Charge(count);

    /// <inheritdoc/>
    public int ScanTerminated(int elementSize, int maximumElements, string fieldName)
    {
        // Update reads span or exposable memory-stream input through this stream in memory mode: scan it in place.
        if (this.stream.TryPeekRemaining(out ReadOnlySpan<byte> remaining))
        {
            return DynamicArrayExtent.ScanSpan(remaining, elementSize, maximumElements, this.stream.RemainingReadBudget, fieldName);
        }

        return DynamicArrayExtent.ScanStream(this.stream, elementSize, maximumElements, fieldName);
    }

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
    public int Read(byte[] buffer, int offset, int count) => this.stream.Read(buffer, offset, count);

    /// <inheritdoc/>
    public object? ReadCustom(ICustomCodec codec) => CustomCodecAdapter.Read(codec, this.stream);

    /// <inheritdoc/>
    public byte ReadByteExactly() => BinaryPrimitiveIO.ReadByteExactly(this.stream);

    /// <inheritdoc/>
    public void ReadExactly(Span<byte> destination) => BinaryPrimitiveIO.ReadExactlyOrThrow(this.stream, destination);

    /// <inheritdoc/>
    public void ReadExactlyOrEndOfStream(Span<byte> destination) => this.stream.ReadExactly(destination);

    /// <inheritdoc/>
    public ReadOnlySpan<byte> ReadFixed(Span<byte> scratch)
    {
        // A memory-backed stream serves the value in place; from any other stream a one-byte value is
        // read through ReadByteExactly and a wider one through ReadExactlyOrThrow, which word their short-read failures.
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
