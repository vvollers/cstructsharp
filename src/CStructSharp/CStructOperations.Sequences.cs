namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The <see cref="ReadOnlySequence{T}"/> forms of the read operations, for input that arrives in segments (a
///     <c>PipeReader</c>, a chain of pooled buffers). A single-segment sequence takes the span path with no copy; a
///     multi-segment one is copied into a pooled buffer - first at most <see cref="ReadOptions.MaxTotalBytesRead"/>
///     plus one byte - that grows, and the read runs again, while the reader needs bytes past it (alignment padding,
///     a pointer target, a <c>T v[EOF]</c> count move past bytes without charging them). Results, failures and offsets
///     are therefore those of the span form over the whole sequence. Coordinates are zero-based at the sequence's start.
///     <para>
///         <c>ParseMany</c> and <c>ParseManyAsync</c> read a sequence of records - one root struct after another
///         until the input ends - each parsed on the <c>MoveNext</c> that reaches it, with the read limits applied per
///         record. Trailing bytes shorter than one record are a failure on the <c>MoveNext</c> that meets them, with
///         the text a <c>T v[EOF]</c> array uses for a partial element when the root has a fixed size; a caller who
///         expects them slices first. A failure names the record by its index before the path (<c>[3].header.length</c>).
///         In the memory, sequence, and asynchronous forms each record is its own region, so a stored absolute pointer
///         address counts from the record's first byte; the synchronous stream form parses each record from the caller's
///         stream as <c>Parse(Stream)</c> does, so there it counts from the stream's first byte.
///     </para>
/// </summary>
public sealed partial class CStruct
{
    /// <summary>The span form of a read operation that a sequence form runs over its buffered copy.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="layout">The layout that reads.</param>
    /// <param name="source">The buffered bytes.</param>
    /// <param name="path">The operation's path.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The read options for this run.</param>
    /// <returns>The operation's result.</returns>
    private delegate T SpanOperation<T>(CStruct layout, ReadOnlySpan<byte> source, string? path, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.Parse(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.Parse(bytes, selected, values, effective));
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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.ParseWithDebug(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.ParseWithDebug(bytes, selected, values, effective));
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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.ReadValue(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.ReadValue(bytes, selected, values, effective));
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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.ReadValue<T>(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.ReadValue<T>(bytes, selected, values, effective));
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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.ReadValueWithDebug(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.ReadValueWithDebug(bytes, selected, values, effective));
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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.ResolveAddress(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.ResolveAddress(bytes, selected!, values, effective));
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

        // A whole copy runs the span form directly; a partial one runs it through the lambda over the first and larger copies.
        byte[] buffer = BufferedInput.CopySequence(source, options, out int length, out long continuation);
        try
        {
            return continuation == BufferedInput.WholeInput
                       ? this.GetArrayLength(new ReadOnlySpan<byte>(buffer, 0, length), path, variables, options)
                       : this.ReadPartialSequence(source, path, variables, options, buffer, length, continuation, static (layout, bytes, selected, values, effective) => layout.GetArrayLength(bytes, selected!, values, effective));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     Reads a multi-segment sequence whose first copy holds only part of it (see the class remarks): the span
    ///     operation runs over the first copy and, while it needs bytes past its copy, over larger ones. The operations are
    ///     static lambdas and their state travels in a <see cref="SequenceOperation{T}"/> struct, so a read allocates
    ///     nothing more.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="source">The multi-segment sequence.</param>
    /// <param name="path">The operation's path, passed to <paramref name="read"/>.</param>
    /// <param name="variables">The caller's layout variables, passed to <paramref name="read"/>.</param>
    /// <param name="options">The caller's read options.</param>
    /// <param name="buffer">The first copy (<see cref="BufferedInput.CopySequence"/>), which stays the caller's.</param>
    /// <param name="length">The bytes the first copy holds.</param>
    /// <param name="continuation">The sequence's length, as the copy reported it.</param>
    /// <param name="read">The span form of the operation.</param>
    /// <returns>The span form's result for the whole sequence.</returns>
    private T ReadPartialSequence<T>(ReadOnlySequence<byte> source, string? path, IReadOnlyDictionary<string, int>? variables, ReadOptions? options, byte[] buffer, int length, long continuation, SpanOperation<T> read)
        => BufferedInput.ReadPartialSequence<SequenceOperation<T>, T>(source, options, new SequenceOperation<T>(this, path, variables, read), buffer, length, continuation);

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
    ///     Reads the records of a seekable stream from its current position to its end as <c>Parse(Stream)</c> does, one
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
    ///     runtime-sized root is read through a pooled window of the bytes left (first
    ///     <see cref="ReadOptions.MaxTotalBytesRead"/> plus one byte) that refills from the start of a record it could
    ///     not hold and grows when a record that starts the window needs bytes past it, which needs a seekable stream.
    ///     After each record a seekable stream sits at the record's end.
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
    /// <param name="stream">The stream positioned at the record's first byte; it is left after the record.</param>
    /// <param name="root">The record struct's declaration name.</param>
    /// <param name="variables">The caller's layout variables, snapshotted for this record.</param>
    /// <param name="options">
    ///     Optional read limits and pointer settings; <see langword="null"/> uses the defaults.
    /// </param>
    /// <returns>The decoded record.</returns>
    /// <exception cref="CStructPathException">The name selects a union or scalar rather than a struct.</exception>
    internal StructValue ParseRecordCore(Stream stream, string root, LayoutVariableInput variables, ReadOptions? options)
    {
        return RequireStruct(this.ParseStreamCore(stream, root, variables, options), root);
    }

    /// <summary>
    ///     Parses one record of a memory sequence straight from its pinned region, as
    ///     <see cref="ParseRecordCore(Stream, string, LayoutVariableInput, ReadOptions?)"/> does over a read-only stream of
    ///     the same bytes, and reports how many bytes the record took.
    /// </summary>
    /// <param name="region">The record's first byte; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The bytes from the record's start to the end of the input.</param>
    /// <param name="root">The record struct's name.</param>
    /// <param name="variables">The caller's layout variables, snapshotted before traversal.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="consumed">The bytes the record took, from its first byte to where its read ended.</param>
    /// <returns>The record.</returns>
    /// <exception cref="CStructException">The record cannot be read; the path and the offset within the region are attached.</exception>
    internal unsafe StructValue ParseRecordCore(byte* region, int length, string root, LayoutVariableInput variables, ReadOptions? options, out long consumed)
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(root);
        StructValue value = this.ReadRootWithEngine(region, length, segments, this.SelectParse(segments, false), variables, settings, null, out bool selected, out consumed);
        return RequireStruct(selected ? value : this.SelectParsedRoot(value, segments), root);
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

    /// <summary>A span operation with its arguments, run by the sequence forms' growth path over each copy.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="Layout">The layout that reads.</param>
    /// <param name="Path">The operation's path.</param>
    /// <param name="Variables">The caller's layout variables.</param>
    /// <param name="Operation">The span form of the operation.</param>
    private readonly record struct SequenceOperation<T>(CStruct Layout, string? Path, IReadOnlyDictionary<string, int>? Variables, SpanOperation<T> Operation) : IBufferedReader<T>
    {
        /// <summary>Runs the operation over one copy; the sequence forms report no end position.</summary>
        /// <param name="source">The copied bytes.</param>
        /// <param name="options">The read options for this run.</param>
        /// <param name="consumed">Always 0.</param>
        /// <returns>The operation's result.</returns>
        public T Read(ReadOnlySpan<byte> source, ReadOptions? options, out long consumed)
        {
            consumed = 0;
            return this.Operation(this.Layout, source, this.Path, this.Variables, options);
        }
    }
}
