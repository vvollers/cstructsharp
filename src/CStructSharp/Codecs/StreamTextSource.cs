namespace CStructSharp.Codecs;

using System;
using System.IO;
using System.Threading;
using CStructSharp.Streams;

/// <summary>
///     Presents any <see cref="Stream"/> to the shared text readers. A <see cref="ReadBudgetStream"/> contributes its
///     string limit, token and remaining read budget, and a rewind through it gives back the charge of the bytes it
///     rewinds; any other stream has no limit, token or budget. Every member is a single call on the stream.
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
    public long RemainingReadBudget => this.stream is ReadBudgetStream budget ? budget.RemainingReadBudget : long.MaxValue;

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count) => this.stream.Read(buffer, offset, count);

    /// <inheritdoc/>
    public void ReadExactly(Span<byte> buffer) => this.stream.ReadExactly(buffer);

    /// <inheritdoc/>
    public void Rewind(int count)
    {
        if (this.stream is ReadBudgetStream budget)
        {
            budget.Rewind(count);
        }
        else
        {
            this.stream.Position -= count;
        }
    }
}
