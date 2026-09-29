namespace CStructSharp.Codecs;

using System;
using System.IO;
using System.Threading;

/// <summary>
///     The byte access the shared text readers of <see cref="PrimitiveCodecs"/> need: chunked and exact reads, a
///     rewind of unconsumed look-ahead, and the operation's string limit and token. Implemented by a wrapper over any
///     <see cref="Stream"/> (<see cref="StreamTextSource"/>) and by the engine's memory cursor, so both run the same
///     reader and consume, charge and fail alike.
/// </summary>
/// <remarks>Implementations are structs passed by reference, so each generic reader is compiled per source.</remarks>
internal interface ITextReadSource
{
    /// <summary>Gets the per-string encoded-byte limit, or <see langword="null"/> when the source sets none.</summary>
    long? StringByteLimit { get; }

    /// <summary>Gets the token checked before each chunk of a terminated string.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Reads up to <paramref name="count"/> bytes, advancing past and charging only the bytes returned.</summary>
    /// <param name="buffer">The array that receives the bytes.</param>
    /// <param name="offset">The index in <paramref name="buffer"/> of the first byte stored.</param>
    /// <param name="count">The largest number of bytes to read.</param>
    /// <returns>The number of bytes read, 0 at the end of the input.</returns>
    int Read(byte[] buffer, int offset, int count);

    /// <summary>Reads exactly <c>buffer.Length</c> bytes, as <see cref="Stream.ReadExactly(Span{byte})"/> does.</summary>
    /// <param name="buffer">The destination; its length is the number of bytes to read.</param>
    /// <exception cref="EndOfStreamException">The input ends first; the bytes that were there are consumed.</exception>
    void ReadExactly(Span<byte> buffer);

    /// <summary>Moves the position back by <paramref name="count"/> bytes that were read but not consumed.</summary>
    /// <param name="count">The positive number of bytes to give back; the budget keeps their charge.</param>
    void Rewind(int count);
}
