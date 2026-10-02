namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The awaitable forms of the read operations. Each reads the stream into a pooled buffer with
///     <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> - first a seekable stream up to its remaining
///     length, any stream up to <see cref="ReadOptions.MaxTotalBytesRead"/> plus one byte - and runs the synchronous
///     span reader over it; while the reader needs bytes past the buffer (alignment padding, a pointer target, a
///     <c>T v[EOF]</c> count move past bytes without charging them) the buffer grows and the reader runs again. So
///     values, limits, failure texts and offsets are those of the span form over the stream's remaining bytes. A
///     seekable stream ends just after the value on success and at its origin on any failure; a non-seekable stream is
///     consumed by what was buffered, whatever the outcome - which exceeds the budget plus one byte only when the value
///     addresses bytes past it. A <see cref="MemoryStream"/> that exposes its buffer is read in place
///     with no copy and the returned task is already complete. The token given here is linked with
///     <see cref="ReadOptions.CancellationToken"/>; it gates the I/O and the boundaries the synchronous reader checks.
///     Because the buffered region starts at the origin, a stored absolute pointer address counts from the origin -
///     as in the span and memory forms - where the synchronous stream form counts from the stream's first byte; a
///     stream whose addresses are absolute stream positions is read from position 0 or through the synchronous form.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>A synchronous read over borrowed bytes that the async forms run once the input is buffered.</summary>
    /// <typeparam name="TResult">The decoded result type.</typeparam>
    /// <param name="region">The first borrowed byte, the operation's coordinate 0; pinned for the call.</param>
    /// <param name="length">The borrowed byte count.</param>
    /// <param name="options">The read settings with the linked cancellation token.</param>
    /// <param name="consumed">Where the read ended, in bytes from <paramref name="region"/>.</param>
    /// <returns>The result in buffer coordinates.</returns>
    private unsafe delegate TResult RegionOperation<TResult>(byte* region, int length, ReadOptions? options, out long consumed);

    /// <summary>Reads a struct (a root or a nested struct selected by path) into a <see cref="StructValue"/>; see the class remarks for the stream rules.</summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">Ends the read while it waits for bytes or at the next boundary the reader checks.</param>
    /// <returns>The struct's values.</returns>
    /// <exception cref="CStructPathException">The path is invalid, or selects a union, an array or a scalar rather than one struct.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public unsafe ValueTask<StructValue> ParseAsync(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) => (StructValue)this.ParseRegionCore(region, length, root, input, effective, debug: false, out consumed).Result,
            static (value, _) => value);
    }

    /// <summary>Reads a struct and records the byte range of every value; the ranges are stream coordinates (the origin is added) for a seekable stream and buffer offsets for a non-seekable one.</summary>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public unsafe ValueTask<ParseResult> ParseWithDebugAsync(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) =>
            {
                (List<DebugData> debug, object value) = this.ParseRegionCore(region, length, root, input, effective, debug: true, out consumed);
                return new ParseResult((StructValue)value, debug);
            },
            static (result, origin) => origin == 0 ? result : new ParseResult(result.Value, Shift(result.Debug, origin)));
    }

    /// <summary>Reads the natural value of any selection (a struct, union, array, scalar, enum, or pointer part); see the class remarks for the stream rules.</summary>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public unsafe ValueTask<object?> ReadValueAsync(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) => this.ReadValueCore(region, length, root, input, effective, out consumed),
            static (value, _) => value);
    }

    /// <summary>Reads a selection and maps it to <typeparamref name="T"/> with the conversions of <c>Get&lt;T&gt;</c>; see the class remarks for the stream rules.</summary>
    /// <typeparam name="T">The requested value type.</typeparam>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public unsafe ValueTask<T> ReadValueAsync<T>(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) => this.ReadTypedValueCore<T>(region, length, root, variables, effective, out consumed),
            static (value, _) => value);
    }

    /// <summary>Reads the natural value of any selection and records the byte range of every value read; see <see cref="ParseWithDebugAsync"/> for the coordinates.</summary>
    /// <inheritdoc cref="ParseAsync(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?, CancellationToken)"/>
    public unsafe ValueTask<ReadResult> ReadValueWithDebugAsync(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) => this.ReadValueWithDebugCore(region, length, root, input, effective, out consumed),
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
    public unsafe ValueTask<long> ResolveAddressAsync(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) =>
            {
                consumed = 0;
                return this.ResolveAddressCore(region, length, path, input, effective);
            },
            static (address, origin) => address + origin,
            restoreOrigin: true);
    }

    /// <summary>Counts a fixed or runtime array's elements (or a terminated string's characters) without reading them; the stream ends at its origin.</summary>
    /// <inheritdoc cref="ResolveAddressAsync"/>
    /// <returns>The count.</returns>
    public unsafe ValueTask<int> GetArrayLengthAsync(
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
            (byte* region, int length, ReadOptions? effective, out long consumed) =>
            {
                consumed = 0;
                return this.GetDynamicArrayLengthCore(region, length, path, input, effective);
            },
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
    /// <typeparam name="TResult">The decoded result type.</typeparam>
    /// <param name="stream">Caller-owned input; its current position is the origin when seekable.</param>
    /// <param name="options">Read budgets and cancellation settings.</param>
    /// <param name="cancellationToken">Additional cancellation signal, linked for this operation.</param>
    /// <param name="operation">Synchronous decoder over the borrowed bytes, which reports where it ended.</param>
    /// <param name="finish">Maps buffer-relative results to caller-visible coordinates.</param>
    /// <param name="restoreOrigin">Whether success consumes no bytes in a seekable source.</param>
    /// <returns>The decoded and coordinate-adjusted result.</returns>
    /// <remarks>Failed restoration never replaces an earlier failure. Rentals are returned before the error escapes.</remarks>
    private async ValueTask<TResult> ReadBufferedAsync<TResult>(
        Stream stream,
        ReadOptions? options,
        CancellationToken cancellationToken,
        RegionOperation<TResult> operation,
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
            try
            {
                if (stream is MemoryStream memory && memory.TryGetBuffer(out ArraySegment<byte> segment))
                {
                    // In place: the bytes are already in memory, so the span reader runs over them with no copy.
                    int offset = segment.Offset + (int)memory.Position;
                    int available = segment.Offset + (int)memory.Length - offset;
                    return this.RunOverBuffer(stream, origin, segment.Array!, offset, available, effective, operation, finish, restoreOrigin);
                }

                // Acquisition can advance the source before it throws, so it belongs inside the restoration scope. The
                // first fill and every growth step yield the same tuple, so one await serves both: the state machine a
                // stream that reads asynchronously boxes holds nothing for the growth path.
                ValueTask<(byte[] Buffer, int Length, int Capacity)> pending = AsyncStreamBuffer.RentAsync(stream, effective, token);
                while (true)
                {
                    (byte[] buffer, int length, int capacity) = await pending.ConfigureAwait(false);
                    long continuation = BufferedInput.Continuation(stream, origin, length, capacity, buffer);
                    if (continuation == BufferedInput.WholeInput)
                    {
                        try
                        {
                            return this.RunOverBuffer(stream, origin, buffer, 0, length, effective, operation, finish, restoreOrigin);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    // Only part of the input is buffered: run once, and on a shortfall await the growth step and run again.
                    if (this.TryRunOverPartialBuffer(stream, origin, buffer, length, continuation, effective, operation, finish, restoreOrigin, out TResult? result, out pending))
                    {
                        return result;
                    }
                }
            }
            catch
            {
                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = origin;
                    }
                }
                catch
                {
                    // A broken seek operation must not replace the original read/cancellation failure.
                }

                throw;
            }
        }
    }

    /// <summary>
    ///     One run over a buffer that holds only the first part of the stream's input: the synchronous half of
    ///     <see cref="ReadBufferedAsync"/>'s growth loop, kept apart from its common path so a buffer that holds the
    ///     whole input carries no exception handler for the growth signal. The buffer's ownership moves to this method.
    /// </summary>
    /// <typeparam name="TResult">The decoded result type.</typeparam>
    /// <param name="stream">Caller-owned source, positioned just after the buffered bytes.</param>
    /// <param name="origin">Starting byte position in the source.</param>
    /// <param name="buffer">The buffer, rented from <see cref="ArrayPool{T}.Shared"/>.</param>
    /// <param name="length">The bytes it holds.</param>
    /// <param name="continuation">What <see cref="BufferedInput.Continuation(Stream, long, int, int)"/> reported for the buffer.</param>
    /// <param name="effective">Read settings with the linked cancellation token.</param>
    /// <param name="operation">Synchronous decoder over the borrowed bytes, which reports where it ended.</param>
    /// <param name="finish">Adjusts result coordinates using the origin.</param>
    /// <param name="restoreOrigin">Whether successful queries restore rather than consume.</param>
    /// <param name="result">The result with caller-visible coordinates when the run succeeded.</param>
    /// <param name="growth">
    ///     When the run needed bytes past the buffer: the growth step, which now owns the buffer; the caller awaits it
    ///     and runs again over the larger buffer.
    /// </param>
    /// <returns>Whether the run succeeded; on success and on failure the buffer has been returned to the pool.</returns>
    private bool TryRunOverPartialBuffer<TResult>(
        Stream stream,
        long origin,
        byte[] buffer,
        int length,
        long continuation,
        ReadOptions? effective,
        RegionOperation<TResult> operation,
        Func<TResult, long, TResult> finish,
        bool restoreOrigin,
        [MaybeNullWhen(false)] out TResult result,
        out ValueTask<(byte[] Buffer, int Length, int Capacity)> growth)
    {
        bool owned = true;
        try
        {
            result = this.RunOverBuffer(stream, origin, buffer, 0, length, BufferedInput.Continue(effective, continuation), operation, finish, restoreOrigin);
            growth = default;
            return true;
        }
        catch (BufferedInputShortfallException shortfall)
        {
            // The effective options carry the operation's token whenever it can be cancelled.
            owned = false;
            growth = BufferedInput.GrowStreamAsync(stream, origin, buffer, length, shortfall.NeededLength, effective?.CancellationToken ?? default);
            result = default;
            return false;
        }
        finally
        {
            if (owned)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>Runs a region operation over borrowed bytes, pinned for the call.</summary>
    /// <typeparam name="TResult">The decoded result type.</typeparam>
    /// <param name="source">The borrowed bytes; offset 0 is the operation's coordinate 0.</param>
    /// <param name="operation">The synchronous decoder.</param>
    /// <param name="options">The read settings for this run.</param>
    /// <param name="consumed">Where the read ended, in bytes from the first byte.</param>
    /// <returns>The result in buffer coordinates.</returns>
    private static unsafe TResult RunRegion<TResult>(ReadOnlySpan<byte> source, RegionOperation<TResult> operation, ReadOptions? options, out long consumed)
    {
        fixed (byte* pointer = &System.Runtime.InteropServices.MemoryMarshal.GetReference(source))
        {
            return operation(pointer, source.Length, options, out consumed);
        }
    }

    /// <summary>Sets a seekable stream's final position after a successful operation and moves the result into caller coordinates.</summary>
    /// <typeparam name="TResult">The decoded result type.</typeparam>
    /// <param name="stream">Caller-owned source whose successful final position is updated.</param>
    /// <param name="origin">Starting byte position in the source.</param>
    /// <param name="result">The result in buffer coordinates.</param>
    /// <param name="consumed">Where the read ended, in bytes from the origin.</param>
    /// <param name="finish">Adjusts result coordinates using the origin.</param>
    /// <param name="restoreOrigin">Whether successful queries restore rather than consume.</param>
    /// <returns>The result with caller-visible coordinates.</returns>
    private static TResult Complete<TResult>(Stream stream, long origin, TResult result, long consumed, Func<TResult, long, TResult> finish, bool restoreOrigin)
    {
        if (stream.CanSeek)
        {
            stream.Position = restoreOrigin ? origin : origin + consumed;
        }

        return finish(result, origin);
    }

    /// <summary>The synchronous half of the in-place path: the span reader over the exposed bytes, then the stream's final position.</summary>
    /// <typeparam name="TResult">The decoded result type.</typeparam>
    /// <param name="stream">Caller-owned source whose successful final position is updated.</param>
    /// <param name="origin">Starting byte position in the source.</param>
    /// <param name="buffer">Borrowed input array; ownership stays with the caller.</param>
    /// <param name="offset">First input byte within the array.</param>
    /// <param name="length">Available input byte count.</param>
    /// <param name="effective">Read settings with the linked cancellation token.</param>
    /// <param name="operation">Synchronous decoder over the borrowed bytes, which reports where it ended.</param>
    /// <param name="finish">Adjusts result coordinates using the origin.</param>
    /// <param name="restoreOrigin">Whether successful queries restore rather than consume.</param>
    /// <returns>The result with caller-visible coordinates.</returns>
    /// <remarks>The enclosing operation restores position after failures; this method shifts diagnostic offsets only.</remarks>
    private TResult RunOverBuffer<TResult>(
        Stream stream,
        long origin,
        byte[] buffer,
        int offset,
        int length,
        ReadOptions? effective,
        RegionOperation<TResult> operation,
        Func<TResult, long, TResult> finish,
        bool restoreOrigin)
    {
        try
        {
            TResult result = RunRegion(new ReadOnlySpan<byte>(buffer, offset, length), operation, effective, out long consumed);
            return Complete(stream, origin, result, consumed, finish, restoreOrigin);
        }
        catch (CStructException failure)
        {
            // The failure's offset is a buffer offset; a seekable stream reports stream coordinates, as its sync form does.
            failure.ShiftOffset(origin);
            throw;
        }
    }
}
