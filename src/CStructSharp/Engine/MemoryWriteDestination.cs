namespace CStructSharp.Engine;

using System;
using System.IO;

/// <summary>
///     The <see cref="IWriteDestination"/> of <c>Serialize</c>: a caller's span or a new array, through the operation's
///     <see cref="MemoryWriteBuffer"/>. A struct, so the writer is compiled for it with direct calls to the sealed buffer.
/// </summary>
internal readonly struct MemoryWriteDestination : IWriteDestination
{
    private readonly MemoryWriteBuffer buffer;

    /// <summary>Wraps the operation's buffer.</summary>
    /// <param name="buffer">The buffer; its owner disposes it.</param>
    public MemoryWriteDestination(MemoryWriteBuffer buffer)
    {
        this.buffer = buffer;
    }

    /// <inheritdoc/>
    public long Position
    {
        get => this.buffer.Position;
        set => this.buffer.Position = value;
    }

    /// <inheritdoc/>
    public long Length => this.buffer.Length;

    /// <inheritdoc/>
    public Stream Stream => this.buffer;

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<byte> bytes) => this.buffer.Write(bytes);

    /// <inheritdoc/>
    public void WriteZeroes(int count) => this.buffer.WriteZeroes(count);

    /// <inheritdoc/>
    public void EnsureStringBytes(long encodedByteCount) => this.buffer.EnsureStringBytes(encodedByteCount);

    /// <inheritdoc/>
    public bool CanAffordBlock(int size, int chargedBytes) => this.buffer.CanAffordBlock(size, chargedBytes);

    /// <inheritdoc/>
    public void WriteBlock(ReadOnlySpan<byte> block, int chargedBytes) => this.buffer.WriteBlock(block, chargedBytes);

    /// <inheritdoc/>
    public int Read(Span<byte> buffer) => this.buffer.Read(buffer);
}
