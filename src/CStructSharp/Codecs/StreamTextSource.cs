namespace CStructSharp.Codecs;

using System;
using System.IO;
using System.Threading;
using CStructSharp.Streams;

/// <summary>
///     Presents any <see cref="Stream"/> to the shared text readers. A <see cref="ReadBudgetStream"/> contributes its
///     string limit and token; any other stream has neither. Every member is a single call on the stream.
/// </summary>
internal readonly struct StreamTextSource : ITextReadSource
{
    private readonly Stream stream;

    /// <summary>Wraps <paramref name="stream"/> without taking ownership of it.</summary>
    /// <param name="stream">The source stream.</param>
    public StreamTextSource(Stream stream)
    {
        this.stream = stream;
    }

    /// <inheritdoc/>
    public long? StringByteLimit => this.stream is ReadBudgetStream budget ? budget.MaxStringBytes : null;

    /// <inheritdoc/>
    public CancellationToken CancellationToken => this.stream is ReadBudgetStream budget ? budget.CancellationToken : default;

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count) => this.stream.Read(buffer, offset, count);

    /// <inheritdoc/>
    public void ReadExactly(Span<byte> buffer) => this.stream.ReadExactly(buffer);

    /// <inheritdoc/>
    public void Rewind(int count) => this.stream.Position -= count;
}
