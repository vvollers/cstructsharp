namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>
///     The <see cref="ReadOnlySequence{T}"/> forms of the read operations, for input that arrives in segments (a
///     <c>PipeReader</c>, a chain of pooled buffers). A single-segment sequence takes the span path with no copy; a
///     multi-segment one is copied into a pooled buffer bounded by <see cref="ReadOptions.MaxTotalBytesRead"/> plus
///     one byte, so a sequence longer than the budget fails with the budget text, as a stream would. Coordinates are
///     zero-based at the sequence's start.
///     <para>
///         <c>ParseMany</c> and <c>ParseManyAsync</c> read a sequence of records - one root struct after another
///         until the input ends - each parsed on the <c>MoveNext</c> that reaches it, with the read limits applied per
///         record. Trailing bytes shorter than one record are a failure on the <c>MoveNext</c> that meets them, with
///         the text a <c>T v[EOF]</c> array uses for a partial element when the root has a fixed size; a caller who
///         expects them slices first. A failure names the record by its index before the path (<c>[3].header.length</c>).
///         In the memory, sequence, and asynchronous forms each record is its own region, so a stored absolute pointer
///         address counts from the record's first byte; the synchronous stream form runs the stream reader, so there
///         it counts from the stream's first byte, as in <c>Parse(Stream)</c>.
///     </para>
/// </summary>
public sealed partial class CStruct
{
    /// <inheritdoc cref="Parse(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public StructValue Parse(
        ReadOnlySequence<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.Parse(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.Parse(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="ParseWithDebug(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public ParseResult ParseWithDebug(
        ReadOnlySequence<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.ParseWithDebug(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.ParseWithDebug(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="ReadValue(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public object? ReadValue(
        ReadOnlySequence<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.ReadValue(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.ReadValue(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="ReadValue{T}(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public T ReadValue<T>(
        ReadOnlySequence<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.ReadValue<T>(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.ReadValue<T>(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="ReadValueWithDebug(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public ReadResult ReadValueWithDebug(
        ReadOnlySequence<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.ReadValueWithDebug(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.ReadValueWithDebug(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="TryReadValue{T}(ReadOnlySpan{byte}, string?, out T, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public bool TryReadValue<T>(
        ReadOnlySequence<byte> source,
        string? path,
        [MaybeNullWhen(false)] out T value,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        try
        {
            value = this.ReadValue<T>(source, path, variables, options);
            return true;
        }
        catch (CStructException)
        {
            value = default;
            return false;
        }
    }

    /// <inheritdoc cref="ResolveAddress(ReadOnlySpan{byte}, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public long ResolveAddress(
        ReadOnlySequence<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.ResolveAddress(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.ResolveAddress(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="GetArrayLength(ReadOnlySpan{byte}, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public int GetArrayLength(
        ReadOnlySequence<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        if (source.IsSingleSegment)
        {
            return this.GetArrayLength(source.FirstSpan, path, variables, options);
        }

        byte[] buffer = ReadCursor.CopySequence(source, options, out int length);
        try
        {
            return this.GetArrayLength(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ------------------------------------------------------------------------------------------------- ParseMany

    /// <summary>Reads the records of <paramref name="source"/> lazily: one root struct after another until the memory ends; see the class remarks for the trailing-bytes and pointer rules.</summary>
    /// <param name="source">The bytes of the records, with nothing else after them.</param>
    /// <param name="path">The case-sensitive name of the root struct each record is; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings, applied to every record; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The records, parsed as they are enumerated.</returns>
    /// <exception cref="CStructPathException"><paramref name="path"/> does not name a struct declaration (a union or scalar root is read with <c>ReadValue</c>).</exception>
    /// <exception cref="CStructReadException">A record cannot be read, or the trailing bytes are shorter than one record; raised by the enumeration that reaches it.</exception>
    public IEnumerable<StructValue> ParseMany(
        ReadOnlyMemory<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        RecordRoot root = this.ResolveRecordRoot(path);
        return RecordSequence.FromMemory(source, root.Size, root.Name, options, RecordParser.Reader(this, root, input));
    }

    /// <summary>Reads the records of a <see cref="ReadOnlySequence{T}"/>: a single segment is read in place, a chain of segments through one pooled copy that lives as long as the enumeration.</summary>
    /// <inheritdoc cref="ParseMany(ReadOnlyMemory{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public IEnumerable<StructValue> ParseMany(
        ReadOnlySequence<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        RecordRoot root = this.ResolveRecordRoot(path);
        return RecordSequence.FromSequence(source, root.Size, root.Name, options, RecordParser.Reader(this, root, input));
    }

    /// <summary>
    ///     Reads the records of a seekable stream from its current position to its end with the stream reader, one
    ///     record per enumeration step, byte-exact: the stream is left after the last record read, or where a failed
    ///     read stopped. A stream that cannot seek is read with <c>ParseManyAsync</c>.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the first record's start.</param>
    /// <param name="path">The case-sensitive name of the root struct each record is; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings, applied to every record; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The records, parsed as they are enumerated.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot be read or cannot seek.</exception>
    /// <exception cref="CStructPathException"><paramref name="path"/> does not name a struct declaration.</exception>
    /// <exception cref="CStructReadException">A record cannot be read, or the trailing bytes are shorter than one record; raised by the enumeration that reaches it.</exception>
    public IEnumerable<StructValue> ParseMany(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Parsing requires a readable, seekable stream.", nameof(stream));
        }

        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        return RecordParser.FromStream(this, stream, this.ResolveRecordRoot(path), input, options);
    }

    /// <summary>
    ///     Reads the records of a stream with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>. A
    ///     root with a fixed size is read exactly one record at a time, so any readable stream serves, byte-exact; a
    ///     runtime-sized root is read through a pooled window of the bytes left (at most
    ///     <see cref="ReadOptions.MaxTotalBytesRead"/> plus one) that refills from the start of a record it could not
    ///     hold, which needs a seekable stream. After each record a seekable stream sits at the record's end.
    /// </summary>
    /// <param name="stream">The readable stream whose current position is the first record's start.</param>
    /// <param name="path">The case-sensitive name of the root struct each record is; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings, applied to every record; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">Ends the enumeration while it waits for bytes, between records, or at the next boundary the reader checks; linked with <see cref="ReadOptions.CancellationToken"/>.</param>
    /// <returns>The records, parsed as they are enumerated.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot be read, or cannot seek while the root has no fixed size.</exception>
    /// <exception cref="CStructPathException"><paramref name="path"/> does not name a struct declaration.</exception>
    /// <exception cref="CStructReadException">A record cannot be read, or the trailing bytes are shorter than one record; raised by the enumeration that reaches it.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public IAsyncEnumerable<StructValue> ParseManyAsync(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Reading requires a readable stream.", nameof(stream));
        }

        LayoutVariableInput input = LayoutVariableInput.FromIntegers(variables);
        RecordRoot root = this.ResolveRecordRoot(path);
        if (root.Size is null && !stream.CanSeek)
        {
            throw new ArgumentException("Records of a runtime-sized struct are read through a window that refills from a record's start, which needs a seekable stream; a fixed-size root is read from any stream.", nameof(stream));
        }

        return RecordSequence.FromStreamAsync(stream, root.Size, root.Name, options, RecordParser.Reader(this, root, input), cancellationToken);
    }

    /// <summary>One record of a sequence: the root parse of the stream form, returning the struct it selects.</summary>
    internal StructValue ParseRecordCore(Stream stream, string root, LayoutVariableInput variables, ReadOptions? options)
    {
        return RequireStruct(this.ParseStreamCore(stream, root, variables, options), root);
    }

    /// <summary>
    ///     The struct a record sequence is made of, checked before the first record: a struct declaration by name
    ///     (a union or scalar fails as <c>Parse</c> fails; a member path is not a record), with its fixed size when
    ///     the layout itself fixes it - the size <c>GetStructSizeInBytes</c> and a generated <c>Sizes</c> constant
    ///     report; a struct whose extent depends on an operation variable or its own data is runtime-sized.
    /// </summary>
    private RecordRoot ResolveRecordRoot(string? path)
    {
        string name = this.RootOrDefault(path);
        if (this.ParsePath(name).Count != 1)
        {
            throw new CStructPathException($"'{name}' selects a member; a sequence of records is read by the name of its struct declaration.");
        }

        CompiledCompositeType composite;
        try
        {
            composite = this.compiledSizeQueries.GetCompiledComposite(this.compilation.GetStruct(name));
        }
        catch (CStructPathException) when (this.compiledModelQueries.TryGetCompiledDeclaration(name, out _))
        {
            throw new CStructPathException($"'{name}' does not select a struct; use ReadValue or ReadValueWithDebug for a scalar, array, or pointer value.");
        }

        if (composite.IsUnion)
        {
            throw new CStructPathException($"'{name}' is a union; use ReadValue or ReadValueWithDebug to read it as a UnionValue.");
        }

        int? size;
        try
        {
            size = this.compiledSizeQueries.GetCompiledStructSizeInBytes(composite, this.staticLayoutVariables, requireFixedSize: true);
        }
        catch (CStructLayoutException)
        {
            size = null;
        }

        return new RecordRoot(name, size);
    }
}
