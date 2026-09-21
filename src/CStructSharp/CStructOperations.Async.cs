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
using CStructSharp.Values;

/// <summary>
///     The awaitable forms of the read operations. Each reads the stream into a pooled buffer with
///     <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> - a seekable stream up to its remaining
///     length, any stream up to <see cref="ReadOptions.MaxTotalBytesRead"/> plus one byte - and runs the synchronous
///     span reader over it, so values, limits, and failure texts are the stream reader's. A seekable stream ends
///     just after the value on success and at its origin on any failure; a non-seekable stream is consumed by what
///     was buffered, whatever the outcome. A <see cref="MemoryStream"/> that exposes its buffer is read in place
///     with no copy and the returned task is already complete. The token given here is linked with
///     <see cref="ReadOptions.CancellationToken"/>; it gates the I/O and the boundaries the synchronous reader checks.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>Reads a struct (a root or a nested struct selected by path) into a <see cref="StructValue"/>; see the class remarks for the stream rules.</summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">Ends the read while it waits for bytes or at the next boundary the reader checks.</param>
    /// <returns>The struct's values.</returns>
    /// <exception cref="CStructPathException">The path is invalid, or selects a union or scalar rather than a struct.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public ValueTask<StructValue> ParseAsync(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string root = this.RootOrDefault(path);
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) => (StructValue)this.ParseStreamCoreImpl(buffered, root, input, effective, debug: false).Result,
            static (value, _) => value);
    }

    /// <summary>Reads a struct and records the byte range of every value; the ranges are stream coordinates (the origin is added) for a seekable stream and buffer offsets for a non-seekable one.</summary>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public ValueTask<ParseResult> ParseWithDebugAsync(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string root = this.RootOrDefault(path);
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) =>
            {
                (List<DebugData> debug, object value) = this.ParseStreamCoreImpl(buffered, root, input, effective, debug: true);
                return new ParseResult((StructValue)value, debug);
            },
            static (result, origin) => origin == 0 ? result : new ParseResult(result.Value, Shift(result.Debug, origin)));
    }

    /// <summary>Reads the natural value of any selection (a struct, union, array, scalar, enum, or pointer part); see the class remarks for the stream rules.</summary>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public ValueTask<object?> ReadValueAsync(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string root = this.RootOrDefault(path);
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) => this.ReadValueCore(buffered, root, input, effective),
            static (value, _) => value);
    }

    /// <summary>Reads a selection and maps it to <typeparamref name="T"/> with the conversions of <c>Get&lt;T&gt;</c>; see the class remarks for the stream rules.</summary>
    /// <typeparam name="T">The requested value type.</typeparam>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public ValueTask<T> ReadValueAsync<T>(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string root = this.RootOrDefault(path);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) => this.ReadTypedValueCore<T>(buffered, root, variables, effective),
            static (value, _) => value);
    }

    /// <summary>Reads the natural value of any selection and records the byte range of every value read; see <see cref="ParseWithDebugAsync"/> for the coordinates.</summary>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public ValueTask<ReadResult> ReadValueWithDebugAsync(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string root = this.RootOrDefault(path);
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) =>
            {
                (List<DebugData> debug, object value) = this.ParseStreamCoreImpl(buffered, root, input, effective, debug: true);
                return new ReadResult(value, debug);
            },
            static (result, origin) => origin == 0 ? result : new ReadResult(result.Value, Shift(result.Debug, origin)));
    }

    /// <summary>
    ///     The non-throwing form of <see cref="ReadValueAsync{T}"/>: a categorized read, path, or limit failure becomes
    ///     a <see cref="ReadAttempt{T}"/> with <see cref="ReadAttempt{T}.Failure"/> set and a seekable stream back at
    ///     its origin. Cancellation and argument errors throw as everywhere else.
    /// </summary>
    /// <typeparam name="T">The requested value type.</typeparam>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public async ValueTask<ReadAttempt<T>> TryReadValueAsync<T>(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            T value = await this.ReadValueAsync<T>(stream, path, variables, options, cancellationToken).ConfigureAwait(false);
            return new ReadAttempt<T>(true, value, null);
        }
        catch (CStructException failure)
        {
            return new ReadAttempt<T>(false, default, failure);
        }
    }

    /// <summary>Finds a path's position without reading its value; a stream coordinate (the origin is added) for a seekable stream, a buffer offset for a non-seekable one. The stream ends at its origin either way.</summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path.</param>
    /// <param name="variables">Optional per-operation integer layout variables.</param>
    /// <param name="options">Optional read limits and pointer settings.</param>
    /// <param name="cancellationToken">Ends the read while it waits for bytes or at the next boundary the reader checks.</param>
    /// <returns>The position.</returns>
    public ValueTask<long> ResolveAddressAsync(
        Stream stream,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) => this.ResolveAddressCore(buffered, path, input, effective),
            static (address, origin) => address + origin,
            restoreOrigin: true);
    }

    /// <summary>Counts a fixed or runtime array's elements (or a terminated string's characters) without reading them; the stream ends at its origin.</summary>
    /// <inheritdoc cref="ResolveAddressAsync"/>
    /// <returns>The count.</returns>
    public ValueTask<int> GetArrayLengthAsync(
        Stream stream,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return this.ReadBufferedAsync(
            stream,
            options,
            cancellationToken,
            (buffered, effective) => this.GetDynamicArrayLengthCore(buffered, path, input, effective),
            static (count, _) => count,
            restoreOrigin: true);
    }

    /// <summary>Adds the stream origin to every debug range so the records are stream coordinates.</summary>
    private static List<DebugData> Shift(IReadOnlyList<DebugData> debug, long origin)
    {
        var shifted = new List<DebugData>(debug.Count);
        foreach (DebugData record in debug)
        {
            shifted.Add(record with { Start = record.Start + origin, End = record.End + origin, });
        }

        return shifted;
    }

    /// <summary>
    ///     Runs a synchronous stream operation over the buffered input: the memory-stream fast path when the stream
    ///     exposes its buffer, else a pooled copy read asynchronously. The result is passed through
    ///     <paramref name="finish"/> with the origin (a stream coordinate, or 0 for a non-seekable stream) so
    ///     ranges and addresses can be shifted. A seekable stream ends after the value, or at the origin when
    ///     <paramref name="restoreOrigin"/> asks for it or the operation failed.
    /// </summary>
    private async ValueTask<TResult> ReadBufferedAsync<TResult>(
        Stream stream,
        ReadOptions? options,
        CancellationToken cancellationToken,
        Func<Stream, ReadOptions?, TResult> operation,
        Func<TResult, long, TResult> finish,
        bool restoreOrigin = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Reading requires a readable stream.", nameof(stream));
        }

        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            ReadOptions? effective = token.CanBeCanceled ? (options ?? new ReadOptions()) with { CancellationToken = token, } : options;
            token.ThrowIfCancellationRequested();
            long origin = stream.CanSeek ? stream.Position : 0;
            if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment))
            {
                // In place: the bytes are already in memory, so the span reader runs over them with no copy.
                int offset = segment.Offset + (int)memory.Position;
                int available = segment.Offset + (int)memory.Length - offset;
                return this.RunOverBuffer(stream, origin, segment.Array!, offset, available, effective, operation, finish, restoreOrigin);
            }

            (byte[] buffer, int length) = await AsyncStreamBuffer.RentAsync(stream, effective, token).ConfigureAwait(false);
            try
            {
                return this.RunOverBuffer(stream, origin, buffer, 0, length, effective, operation, finish, restoreOrigin);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>The synchronous half: the span reader over the buffered bytes, then the stream's final position.</summary>
    private unsafe TResult RunOverBuffer<TResult>(
        Stream stream,
        long origin,
        byte[] buffer,
        int offset,
        int length,
        ReadOptions? effective,
        Func<Stream, ReadOptions?, TResult> operation,
        Func<TResult, long, TResult> finish,
        bool restoreOrigin)
    {
        try
        {
            TResult result;
            long consumed;
            fixed (byte* pointer = &System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(buffer))
            {
                using var buffered = new FixedBufferStream(pointer + offset, length, writable: false);
                result = operation(buffered, effective);
                consumed = buffered.Position;
            }

            if (stream.CanSeek)
            {
                stream.Position = restoreOrigin ? origin : origin + consumed;
            }

            return finish(result, origin);
        }
        catch (CStructException failure)
        {
            // The failure's offset is a buffer offset; a seekable stream reports stream coordinates, as its sync form does.
            failure.ShiftOffset(origin);
            if (stream.CanSeek)
            {
                stream.Position = origin;
            }

            throw;
        }
        catch
        {
            if (stream.CanSeek)
            {
                stream.Position = origin;
            }

            throw;
        }
    }
}
