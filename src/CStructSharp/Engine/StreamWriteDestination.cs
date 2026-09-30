namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>
///     The <see cref="IWriteDestination"/> of <c>Write</c> to a caller's stream and of <c>Serialize</c> to a buffer writer
///     (through its <see cref="BufferWriterStream"/>): every member calls the operation's <see cref="WriteBudgetStream"/>
///     directly, so the budget, the bytes left in the stream by a failure, the bytes a bitfield or a static plan reads back
///     (the stream's existing bytes, not zeroes), a buffer writer's committed windows and its refusals to revisit them, and
///     the final position are the budget stream's by construction.
/// </summary>
/// <remarks>
///     A struct, so the writer is compiled for it with direct calls to the sealed budget stream. The caller's stream is
///     kept beside the budget only for failure context, which reports the caller's stream's own position.
/// </remarks>
internal readonly struct StreamWriteDestination : IWriteDestination
{
    private readonly WriteBudgetStream budget;
    private readonly Stream caller;

    /// <summary>Wraps the operation's budget stream over the caller's stream.</summary>
    /// <param name="budget">The operation's budget stream over <paramref name="caller"/>; it never disposes the caller's stream.</param>
    /// <param name="caller">The caller's writable, seekable stream, whose position failure context reports.</param>
    public StreamWriteDestination(WriteBudgetStream budget, Stream caller)
    {
        this.budget = budget;
        this.caller = caller;
    }

    /// <inheritdoc/>
    public long Position
    {
        get => this.budget.Position;
        set => this.budget.Position = value;
    }

    /// <inheritdoc/>
    public long Length => this.budget.Length;

    /// <inheritdoc/>
    public bool CanRead => this.budget.CanRead;

    /// <summary>
    ///     Gets a value indicating whether a typed array or narrow text may be written as one block; never for a stream,
    ///     into which they are written element by element so a failure leaves the earlier elements in the stream.
    /// </summary>
    public bool AllowsBlocks => false;

    /// <inheritdoc/>
    public Stream Stream => this.budget;

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> bytes) => this.budget.Write(bytes);

    /// <inheritdoc/>
    public void WriteZeroes(int count) => this.budget.WriteZeroes(count);

    /// <inheritdoc/>
    public void EnsureStringBytes(long encodedByteCount) => this.budget.EnsureStringBytes(encodedByteCount);

    /// <inheritdoc/>
    public bool CanAffordBlock(int size, int chargedBytes) => this.budget.CanAffordBlock(size, chargedBytes);

    /// <inheritdoc/>
    public void WriteBlock(ReadOnlySpan<byte> block, int chargedBytes) => this.budget.WriteBlock(block, chargedBytes);

    /// <inheritdoc/>
    public int Read(Span<byte> buffer) => this.budget.Read(buffer);

    /// <inheritdoc/>
    public void AttachContext(CStructException exception, IReadOnlyList<PathSegment> segments) => ExceptionContext.Attach(exception, segments, this.caller);
}
