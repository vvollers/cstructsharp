namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;

/// <summary>
///     A <see cref="MemoryReadCursor"/> with one deliberate, plausible defect: a short <see cref="ReadExactly"/> leaves the
///     position where the read started instead of at the end of the input. The cursor differential must report it,
///     which shows that it compares final positions after failures and not only values.
/// </summary>
internal struct PlantedReadCursor : IReadCursor
{
    private MemoryReadCursor inner;

    /// <summary>Wraps a memory cursor.</summary>
    /// <param name="inner">The correct cursor that every other member delegates to.</param>
    public PlantedReadCursor(MemoryReadCursor inner)
    {
        this.inner = inner;
    }

    /// <inheritdoc/>
    public long Position
    {
        readonly get => this.inner.Position;
        set => this.inner.Position = value;
    }

    /// <inheritdoc/>
    public readonly long Length => this.inner.Length;

    /// <inheritdoc/>
    public readonly long MaxStringBytes => this.inner.MaxStringBytes;

    /// <inheritdoc/>
    public readonly CancellationToken CancellationToken => this.inner.CancellationToken;

    /// <inheritdoc/>
    public readonly void ThrowIfCancellationRequested() => this.inner.ThrowIfCancellationRequested();

    /// <inheritdoc/>
    public void Skip(long count) => this.inner.Skip(count);

    /// <inheritdoc/>
    public void Align(long origin, int alignment) => this.inner.Align(origin, alignment);

    /// <inheritdoc/>
    public readonly bool IsShortBy(long count) => this.inner.IsShortBy(count);

    /// <inheritdoc/>
    public bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes) => this.inner.TryReadSpan(count, out bytes);

    /// <inheritdoc/>
    public bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes) => this.inner.TryReadSpanWithinBudget(count, out bytes);

    /// <inheritdoc/>
    public readonly bool TryReadBlockWithinBudget(Span<byte> destination) => this.inner.TryReadBlockWithinBudget(destination);

    /// <inheritdoc/>
    public readonly bool TryPeekRemaining(out ReadOnlySpan<byte> bytes) => this.inner.TryPeekRemaining(out bytes);

    /// <inheritdoc/>
    public void Advance(int count) => this.inner.Advance(count);

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count) => this.inner.Read(buffer, offset, count);

    /// <inheritdoc/>
    public object? ReadCustom(ICustomCodec codec) => this.inner.ReadCustom(codec);

    /// <inheritdoc/>
    public byte ReadByteExactly() => this.inner.ReadByteExactly();

    /// <summary>Reads exactly the span, but after a short read rewinds to the start: the planted defect.</summary>
    /// <param name="destination">The span to fill.</param>
    /// <exception cref="CStructReadException">The input ends before the span is full.</exception>
    public void ReadExactly(Span<byte> destination)
    {
        long start = this.inner.Position;
        try
        {
            this.inner.ReadExactly(destination);
        }
        catch (CStructReadException)
        {
            this.inner.Position = start;
            throw;
        }
    }

    /// <inheritdoc/>
    public void ReadExactlyOrEndOfStream(Span<byte> destination) => this.inner.ReadExactlyOrEndOfStream(destination);

    /// <inheritdoc/>
    public ReadOnlySpan<byte> ReadFixed(Span<byte> scratch) => this.inner.ReadFixed(scratch);

    /// <inheritdoc/>
    public IList<object?> ReadPrimitiveArray(PrimitiveCodec codec, int count) => this.inner.ReadPrimitiveArray(codec, count);

    /// <inheritdoc/>
    public string ReadTerminatedString(Encoding encoding, char terminator) => this.inner.ReadTerminatedString(encoding, terminator);

    /// <inheritdoc/>
    public string ReadBoundedText(int byteCount, string type) => this.inner.ReadBoundedText(byteCount, type);

    /// <inheritdoc/>
    public readonly void FlushPosition() => this.inner.FlushPosition();
}
