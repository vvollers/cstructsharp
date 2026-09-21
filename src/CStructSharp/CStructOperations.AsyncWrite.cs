namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;

/// <summary>
///     The awaitable write forms. <see cref="WriteAsync"/> serializes the value first (the validation the synchronous
///     writer performs, nothing written on a failure) and writes the bytes with one
///     <see cref="Stream.WriteAsync(ReadOnlyMemory{byte}, CancellationToken)"/>. <see cref="UpdateAsync"/> needs a
///     seekable stream: the region from the current position is read into a buffer, the in-place update runs over it,
///     and only the byte ranges that changed are written back; the position is the origin afterwards. As in the
///     span form, a stored absolute pointer address counts from the region's origin.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>Serializes a root or selected value and writes it at the stream's current position.</summary>
    /// <param name="stream">The writable stream; the current position is the output origin.</param>
    /// <param name="path">The case-sensitive root name or nested field path to write.</param>
    /// <param name="value">The value to encode: a <see cref="Values.StructValue"/>, a dictionary, a registered mapped class, or a scalar for a scalar path.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, unknown-member policy, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">Ends the write before the bytes are sent or at the next boundary the writer checks; linked with <see cref="WriteOptions.CancellationToken"/>.</param>
    /// <returns>A task that completes when the bytes have been written.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value cannot be encoded; nothing was written.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask WriteAsync(
        Stream stream,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
        {
            throw new ArgumentException("Writing requires a writable stream.", nameof(stream));
        }

        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            WriteOptions? effective = token.CanBeCanceled ? (options ?? new WriteOptions()) with { CancellationToken = token, } : options;
            token.ThrowIfCancellationRequested();

            // The serialized bytes are exactly the value's size; a validation failure surfaces here, before any write.
            byte[] bytes = this.Serialize(path, value, variables, effective);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Locates a value in the stream's existing bytes and replaces it in place: the region from the current
    ///     position is read into a buffer, validated and updated there, and only the changed ranges are written back.
    ///     The stream must be seekable; the position is the origin afterwards.
    /// </summary>
    /// <param name="stream">The readable, writable, seekable stream; the current position is the region's origin.</param>
    /// <param name="path">The case-sensitive root name or nested field path to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">Optional per-operation integer layout variables.</param>
    /// <param name="options">Optional update limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">Ends the update before the read, during the staged validation, or before the write-back; linked with <see cref="WriteOptions.CancellationToken"/>.</param>
    /// <returns>A task that completes when the changed bytes have been written.</returns>
    /// <exception cref="ArgumentException">The stream cannot seek: the changed bytes are written back in place.</exception>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value cannot be encoded; the stream is unchanged.</exception>
    public async ValueTask UpdateAsync(
        Stream stream,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        UpdateOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException("UpdateAsync needs a readable, writable, seekable stream: the changed bytes are written back in place.", nameof(stream));
        }

        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            UpdateOptions? effective = token.CanBeCanceled ? (options ?? new UpdateOptions()) with { CancellationToken = token, } : options;
            token.ThrowIfCancellationRequested();
            long origin = stream.Position;
            var region = new ReadOptions { MaxTotalBytesRead = (effective ?? new UpdateOptions()).MaxTraversalBytesRead, };
            (byte[] buffer, int length) = await AsyncStreamBuffer.RentAsync(stream, region, token).ConfigureAwait(false);
            byte[] original = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
            try
            {
                buffer.AsSpan(0, length).CopyTo(original);
                this.Update(buffer.AsSpan(0, length), path, value, variables, effective);

                // Write back the runs of bytes the update changed, each at its stream coordinate.
                int index = 0;
                while (index < length)
                {
                    if (buffer[index] == original[index])
                    {
                        index++;
                        continue;
                    }

                    int start = index;
                    while (index < length && buffer[index] != original[index])
                    {
                        index++;
                    }

                    token.ThrowIfCancellationRequested();
                    stream.Position = origin + start;
                    await stream.WriteAsync(buffer.AsMemory(start, index - start), token).ConfigureAwait(false);
                }

                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(original);
                ArrayPool<byte>.Shared.Return(buffer);
                stream.Position = origin;
            }
        }
    }
}
