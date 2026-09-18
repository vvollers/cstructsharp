namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The public operations of a compiled layout. Every operation exists for a <see cref="Stream"/> (reading
///     starts at its current position) and for in-memory input (<see cref="ReadOnlySpan{T}"/>,
///     <see cref="ReadOnlyMemory{T}"/>, or a byte array, whose start is coordinate zero), with the same parameters
///     in the same order: the input, the root name or path, then the optional variables and options. An omitted
///     or <see langword="null"/> path selects the first struct or union the layout declares.
/// </summary>
public sealed partial class CStruct
{
    // ------------------------------------------------------------------------------------------------------ Parse

    /// <summary>Reads a struct (a root or a nested struct selected by path) into a <see cref="StructValue"/>.</summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The struct's values, readable as <c>dynamic</c>, by name, or through <c>StructValue.Get&lt;T&gt;</c>.</returns>
    /// <exception cref="CStructPathException">The path is invalid, or selects a union or scalar rather than a struct (use <see cref="ReadValue(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/> for those).</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public StructValue Parse(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return RequireStruct(this.ParseStreamCore(stream, this.RootOrDefault(path), LayoutVariableInput.FromIntegers(variables), options), path);
    }

    /// <summary>Reads a struct from a byte span; pointer positions are zero-based within the span.</summary>
    /// <param name="source">The complete byte region available to this operation.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The struct's values.</returns>
    /// <exception cref="CStructPathException">The path is invalid, or selects a union or scalar rather than a struct.</exception>
    /// <exception cref="CStructReadException">The region cannot provide or decode the required bytes.</exception>
    public StructValue Parse(
        ReadOnlySpan<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return RequireStruct(this.ParseMemoryCore(source, path, variables, options, debug: false).Value, path);
    }

    /// <summary>Reads a struct from read-only memory; the memory is not retained after the call returns.</summary>
    /// <inheritdoc cref="Parse(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public StructValue Parse(
        ReadOnlyMemory<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.Parse(source.Span, path, variables, options);
    }

    /// <summary>Reads a struct from a byte array without copying it.</summary>
    /// <inheritdoc cref="Parse(ReadOnlySpan{byte}, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public StructValue Parse(
        byte[] source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.Parse(source.AsSpan(), path, variables, options);
    }

    // --------------------------------------------------------------------------------------------- ParseWithDebug

    /// <summary>
    ///     Reads a struct exactly like <see cref="Parse(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    ///     and also records the byte range of every value read. Debug reads need a seekable stream.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The struct's values and the debug records.</returns>
    /// <exception cref="CStructPathException">The path is invalid, or selects a union or scalar rather than a struct.</exception>
    /// <exception cref="CStructReadException">The stream is not seekable or cannot provide or decode the required bytes.</exception>
    public ParseResult ParseWithDebug(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        (List<DebugData> debug, object value) = this.ParseStreamWithDebugCore(stream, this.RootOrDefault(path), LayoutVariableInput.FromIntegers(variables), options);
        return new ParseResult(RequireStruct(value, path), debug);
    }

    /// <summary>Reads a struct from a byte span and records the byte range of every value read.</summary>
    /// <inheritdoc cref="ParseWithDebug(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public ParseResult ParseWithDebug(
        ReadOnlySpan<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        (object value, List<DebugData> debug) = this.ParseMemoryCore(source, path, variables, options, debug: true);
        return new ParseResult(RequireStruct(value, path), debug);
    }

    /// <summary>Reads a struct from read-only memory and records the byte range of every value read.</summary>
    /// <inheritdoc cref="ParseWithDebug(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public ParseResult ParseWithDebug(
        ReadOnlyMemory<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ParseWithDebug(source.Span, path, variables, options);
    }

    /// <summary>Reads a struct from a byte array and records the byte range of every value read.</summary>
    /// <inheritdoc cref="ParseWithDebug(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public ParseResult ParseWithDebug(
        byte[] source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.ParseWithDebug(source.AsSpan(), path, variables, options);
    }

    // -------------------------------------------------------------------------------------------------- ReadValue

    /// <summary>
    ///     Reads one value - a root, a field, an array element, a pointer accessor, or a nested object - in its
    ///     natural representation without materializing unrelated siblings.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct or union.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>A <see cref="StructValue"/>, <see cref="UnionValue"/>, scalar, string, <see cref="PrimitiveArray{T}"/> or list, <see cref="Pointer"/>, or <see cref="EnumValueResult"/>.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public object? ReadValue(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadValueCore(stream, this.RootOrDefault(path), LayoutVariableInput.FromIntegers(variables), options);
    }

    /// <summary>Reads one value from a byte span in its natural representation.</summary>
    /// <inheritdoc cref="ReadValue(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public object? ReadValue(
        ReadOnlySpan<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore(source, path, variables, options);
    }

    /// <summary>Reads one value from read-only memory in its natural representation.</summary>
    /// <inheritdoc cref="ReadValue(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public object? ReadValue(
        ReadOnlyMemory<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore(source.Span, path, variables, options);
    }

    /// <summary>Reads one value from a byte array in its natural representation.</summary>
    /// <inheritdoc cref="ReadValue(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public object? ReadValue(
        byte[] source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.ReadMemoryValueCore(source.AsSpan(), path, variables, options);
    }

    // ------------------------------------------------------------------------------------------ ReadValueWithDebug

    /// <summary>
    ///     Reads one value exactly like <see cref="ReadValue(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    ///     and also records the byte range of every value read. Debug reads need a seekable stream.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct or union.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The value at the path and the debug records.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream is not seekable or cannot provide or decode the required bytes.</exception>
    public ReadResult ReadValueWithDebug(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        (List<DebugData> debug, object value) = this.ParseStreamWithDebugCore(stream, this.RootOrDefault(path), LayoutVariableInput.FromIntegers(variables), options);
        return new ReadResult(value, debug);
    }

    /// <summary>Reads one value from a byte span and records the byte range of every value read.</summary>
    /// <inheritdoc cref="ReadValueWithDebug(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public ReadResult ReadValueWithDebug(
        ReadOnlySpan<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        (object value, List<DebugData> debug) = this.ParseMemoryCore(source, path, variables, options, debug: true);
        return new ReadResult(value, debug);
    }

    /// <summary>Reads one value from read-only memory and records the byte range of every value read.</summary>
    /// <inheritdoc cref="ReadValueWithDebug(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public ReadResult ReadValueWithDebug(
        ReadOnlyMemory<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadValueWithDebug(source.Span, path, variables, options);
    }

    /// <summary>Reads one value from a byte array and records the byte range of every value read.</summary>
    /// <inheritdoc cref="ReadValueWithDebug(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public ReadResult ReadValueWithDebug(
        byte[] source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.ReadValueWithDebug(source.AsSpan(), path, variables, options);
    }

    /// <summary>
    ///     Reads one value and maps it to <typeparamref name="T"/>: a scalar with a checked conversion, or a
    ///     struct bound to a class or record whose public writable members match the field names without regard
    ///     to case. Unsupported or lossy conversions fail with <see cref="CStructReadException"/>.
    /// </summary>
    /// <typeparam name="T">The destination type; a POCO needs a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct or union.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value converted or bound to <typeparamref name="T"/>.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The bytes cannot be decoded or the result cannot be bound to <typeparamref name="T"/>.</exception>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        Stream stream,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadTypedValueCore<T>(stream, this.RootOrDefault(path), variables, options);
    }

    /// <summary>Reads one value from a byte span and maps it to <typeparamref name="T"/>.</summary>
    /// <inheritdoc cref="ReadValue{T}(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlySpan<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore<T>(source, path, variables, options);
    }

    /// <summary>Reads one value from read-only memory and maps it to <typeparamref name="T"/>.</summary>
    /// <inheritdoc cref="ReadValue{T}(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlyMemory<byte> source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore<T>(source.Span, path, variables, options);
    }

    /// <summary>Reads one value from a byte array and maps it to <typeparamref name="T"/>.</summary>
    /// <inheritdoc cref="ReadValue{T}(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        byte[] source,
        string? path = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.ReadMemoryValueCore<T>(source.AsSpan(), path, variables, options);
    }

    // ----------------------------------------------------------------------------------------------- TryReadValue

    /// <summary>
    ///     Attempts <see cref="ReadValue{T}(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>.
    ///     An expected layout, path, read, or conversion failure returns <see langword="false"/> and restores the
    ///     stream position from before the attempt; invalid arguments and unexpected failures still throw.
    /// </summary>
    /// <typeparam name="T">The destination type; a POCO needs a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="stream">The readable, seekable stream whose position is restored after an expected failure.</param>
    /// <param name="path">The case-sensitive root name or nested path; <see langword="null"/> selects the first declared struct or union.</param>
    /// <param name="value">Receives the typed result on success, or the default value of <typeparamref name="T"/> on failure.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> for a categorized CStructSharp failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        Stream stream,
        string? path,
        [MaybeNullWhen(false)] out T value,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Reading values requires a readable, seekable stream.", nameof(stream));
        }

        long initialPosition = stream.Position;
        try
        {
            value = this.ReadValue<T>(stream, path, variables, options);
            return true;
        }
        catch (CStructException)
        {
            stream.Position = initialPosition;
            value = default;
            return false;
        }
    }

    /// <summary>Attempts a typed read from a byte span; an expected CStructSharp failure returns <see langword="false"/>.</summary>
    /// <inheritdoc cref="TryReadValue{T}(Stream, string?, out T, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlySpan<byte> source,
        string? path,
        [MaybeNullWhen(false)] out T value,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        try
        {
            value = this.ReadMemoryValueCore<T>(source, path, variables, options);
            return true;
        }
        catch (CStructException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Attempts a typed read from read-only memory; an expected CStructSharp failure returns <see langword="false"/>.</summary>
    /// <inheritdoc cref="TryReadValue{T}(Stream, string?, out T, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlyMemory<byte> source,
        string? path,
        [MaybeNullWhen(false)] out T value,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.TryReadValue(source.Span, path, out value, variables, options);
    }

    /// <summary>Attempts a typed read from a byte array; an expected CStructSharp failure returns <see langword="false"/>.</summary>
    /// <inheritdoc cref="TryReadValue{T}(Stream, string?, out T, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        byte[] source,
        string? path,
        [MaybeNullWhen(false)] out T value,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.TryReadValue(source.AsSpan(), path, out value, variables, options);
    }

    // --------------------------------------------------------------------------------------- ResolveAddress, length

    /// <summary>
    ///     Finds the position of a path without reading its value. A path ending in <c>.value</c> resolves to the
    ///     pointer target; one ending in <c>.address</c> resolves to the pointer field itself. The stream position is
    ///     restored afterwards.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive root name or nested path to locate.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional traversal limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The absolute stream position of the selected field or pointer target.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide the bytes required for traversal.</exception>
    public long ResolveAddress(
        Stream stream,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ResolveAddressCore(stream, path, LayoutVariableInput.FromIntegers(variables), options);
    }

    /// <summary>Finds the offset of a path within a byte span without reading its value.</summary>
    /// <inheritdoc cref="ResolveAddress(Stream, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <returns>The zero-based offset of the selected field or pointer target within <paramref name="source"/>.</returns>
    public unsafe long ResolveAddress(
        ReadOnlySpan<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.ResolveAddressCore(stream, path, LayoutVariableInput.FromIntegers(variables), options);
        }
    }

    /// <summary>Finds the offset of a path within read-only memory without reading its value.</summary>
    /// <inheritdoc cref="ResolveAddress(ReadOnlySpan{byte}, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public long ResolveAddress(
        ReadOnlyMemory<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ResolveAddress(source.Span, path, variables, options);
    }

    /// <summary>Finds the offset of a path within a byte array without reading its value.</summary>
    /// <inheritdoc cref="ResolveAddress(ReadOnlySpan{byte}, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public long ResolveAddress(
        byte[] source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.ResolveAddress(source.AsSpan(), path, variables, options);
    }

    /// <summary>
    ///     Returns the element count of the array, or the character count of the string, a path selects, reading
    ///     only what determines it. The stream position is restored afterwards.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive path of an array or string field.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of elements in the selected array or of characters in the selected string.</returns>
    /// <exception cref="CStructPathException">The path is invalid or does not select an array or string.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide the bytes required to resolve the count.</exception>
    public int GetArrayLength(
        Stream stream,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.GetDynamicArrayLengthCore(stream, path, LayoutVariableInput.FromIntegers(variables), options);
    }

    /// <summary>Returns the element or character count a path selects within a byte span.</summary>
    /// <inheritdoc cref="GetArrayLength(Stream, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public unsafe int GetArrayLength(
        ReadOnlySpan<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.GetDynamicArrayLengthCore(stream, path, LayoutVariableInput.FromIntegers(variables), options);
        }
    }

    /// <summary>Returns the element or character count a path selects within read-only memory.</summary>
    /// <inheritdoc cref="GetArrayLength(ReadOnlySpan{byte}, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    public int GetArrayLength(
        ReadOnlyMemory<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.GetArrayLength(source.Span, path, variables, options);
    }

    /// <summary>Returns the element or character count a path selects within a byte array.</summary>
    /// <inheritdoc cref="GetArrayLength(ReadOnlySpan{byte}, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public int GetArrayLength(
        byte[] source,
        string path,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return this.GetArrayLength(source.AsSpan(), path, variables, options);
    }

    // ------------------------------------------------------------------------------------------ Serialize, Write

    /// <summary>
    ///     Creates a new byte array holding <paramref name="value"/> encoded as the declaration or nested field
    ///     <paramref name="path"/> selects. The value may be a <see cref="StructValue"/> from a parse, a dictionary,
    ///     an anonymous object, a POCO, or a scalar for a scalar path.
    /// </summary>
    /// <param name="path">The case-sensitive root name or nested field path to serialize.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, binding rules, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>An exactly sized array; no partial output survives a failure.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value cannot be encoded.</exception>
    public byte[] Serialize(
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null)
    {
        return this.SerializeCore(path, value, LayoutVariableInput.FromIntegers(variables), options);
    }

    /// <summary>
    ///     Serializes into caller-owned storage and returns the number of bytes initialized at its start. Excess
    ///     capacity is left unchanged; insufficient capacity fails with <see cref="CStructWriteException"/> after
    ///     a prefix may already have been written. Pointer coordinates are zero-based within the destination.
    /// </summary>
    /// <param name="destination">Caller-owned storage that receives the encoded bytes.</param>
    /// <param name="path">The case-sensitive root name or nested field path to serialize.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, binding rules, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of bytes written at the start of <paramref name="destination"/>.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value is invalid or the destination is too small; an initialized prefix may remain.</exception>
    public int Serialize(
        Span<byte> destination,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null)
    {
        return this.SerializeToMemoryCore(destination, path, value, variables, options);
    }

    /// <summary>
    ///     Appends the encoded value to an <see cref="IBufferWriter{T}"/> and returns the number of bytes appended.
    ///     Pointer coordinates are relative to the start of the appended region; windows already advanced cannot
    ///     be retracted after a later failure.
    /// </summary>
    /// <param name="destination">The caller-owned buffer writer to append to.</param>
    /// <param name="path">The case-sensitive root name or nested field path to serialize.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, binding rules, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of bytes appended.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value cannot be encoded.</exception>
    public long Serialize(
        IBufferWriter<byte> destination,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var stream = new BufferWriterStream(destination);
        this.WriteStreamCore(stream, path, value, LayoutVariableInput.FromIntegers(variables), options);
        return stream.Complete();
    }

    /// <summary>
    ///     Writes the encoded value to a writable, seekable stream starting at its current position. Fields written
    ///     before a later failure remain in the stream.
    /// </summary>
    /// <param name="stream">The writable, seekable destination; writing starts at its current position.</param>
    /// <param name="path">The case-sensitive root name or nested field path to write.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, binding rules, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value cannot be encoded; earlier fields may already be written.</exception>
    public void Write(
        Stream stream,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null)
    {
        this.WriteStreamCore(stream, path, value, LayoutVariableInput.FromIntegers(variables), options);
    }

    // ----------------------------------------------------------------------------------------------------- Update

    /// <summary>
    ///     Replaces one value that already exists in the data: the path is located, the replacement is validated
    ///     against the existing storage, and only then are its bytes committed. Later fields never move, so the
    ///     replacement must fit the existing storage plan. The stream position is restored afterwards.
    /// </summary>
    /// <param name="stream">The readable, writable, seekable stream whose current position is the operation origin.</param>
    /// <param name="path">The case-sensitive path of the value to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional traversal limits, pointer rules, and union handling; <see langword="null"/> uses the documented defaults.</param>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The replacement does not fit or cannot be encoded; the stream is unchanged unless a physical commit failed.</exception>
    public void Update(
        Stream stream,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        UpdateOptions? options = null)
    {
        this.UpdateStreamCore(stream, path, value, LayoutVariableInput.FromIntegers(variables), options);
    }

    /// <summary>
    ///     Replaces one value in place inside a byte span (a byte array binds here too). Pointer coordinates are
    ///     zero-based within the span, and the span's length cannot change.
    /// </summary>
    /// <param name="data">The bytes to change in place.</param>
    /// <param name="path">The case-sensitive path of the value to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional traversal limits, pointer rules, and union handling; <see langword="null"/> uses the documented defaults.</param>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The replacement does not fit or cannot be encoded; the span is unchanged.</exception>
    public unsafe void Update(
        Span<byte> data,
        string path,
        object value,
        IReadOnlyDictionary<string, int>? variables = null,
        UpdateOptions? options = null)
    {
        fixed (byte* buffer = data)
        {
            using var stream = new FixedBufferStream(buffer, data.Length, writable: true);
            stream.SetLength(data.Length);
            this.UpdateStreamCore(stream, path, value, LayoutVariableInput.FromIntegers(variables), options);
        }
    }

    // ---------------------------------------------------------------------------------------------------- helpers

    /// <summary>The path the caller gave, or the first declared struct or union when none was given.</summary>
    private string RootOrDefault(string? path)
    {
        return path ?? this.compiledModelQueries.GetFirstCompiledStructName();
    }

    /// <summary>
    ///     <see cref="Parse(Stream, string?, IReadOnlyDictionary{string, int}?, ReadOptions?)"/> returns structs
    ///     only; a union or scalar at the path is a path error that points to <c>ReadValue</c>.
    /// </summary>
    private static StructValue RequireStruct(object? value, string? path)
    {
        return value switch
        {
            StructValue structValue => structValue,
            UnionValue union => throw new CStructPathException(
                $"'{path ?? union.UnionName}' is a union; use ReadValue or ReadValueWithDebug to read it as a UnionValue."),
            _ => throw new CStructPathException(
                $"'{path}' does not select a struct; use ReadValue or ReadValueWithDebug for a scalar, array, or pointer value."),
        };
    }
}
