namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>
///     Provides synchronous zero-copy memory input and caller-owned memory output entry points. Pointer coordinates
///     are zero-based within the supplied input or newly serialized output region.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Parses a composite from a byte span without copying the complete input. A null path selects the first
    ///     source-order struct or union; pointer positions are zero-based within the supplied region.
    /// </summary>
    /// <param name="source">The complete byte region available to this operation.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>A dynamic struct object, lossless <see cref="UnionValue"/>, or selected nested value.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The region cannot provide or decode the required bytes.</exception>
    public dynamic Parse(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ParseMemoryCore(source, elementNameOrPath, variables, options);
    }

    /// <summary>
    ///     Parses a composite from read-only memory without copying the complete input. The operation is synchronous
    ///     and does not retain the caller's memory after it returns. Pointer positions are zero-based within the
    ///     supplied region, including when that memory is a slice of a larger allocation.
    /// </summary>
    /// <param name="source">The complete memory region available to this synchronous operation.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>A dynamic struct object, lossless <see cref="UnionValue"/>, or selected nested value.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The region cannot provide or decode the required bytes.</exception>
    public dynamic Parse(
        ReadOnlyMemory<byte> source,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ParseMemoryCore(source.Span, elementNameOrPath, variables, options);
    }

    /// <summary>
    ///     Reads one natural value from a byte span without copying the complete input. A null path selects the first
    ///     source-order struct or union.
    /// </summary>
    /// <param name="source">The complete byte region available to this operation.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value in its natural dynamic, scalar, collection, pointer, enum, or union representation.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The region cannot provide or decode the required bytes.</exception>
    public object? ReadValue(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore(source, elementNameOrPath, variables, options);
    }

    /// <summary>Reads one natural value from read-only memory without retaining the supplied region.</summary>
    /// <param name="source">The complete memory region available to this synchronous operation.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value in its natural dynamic, scalar, collection, pointer, enum, or union representation.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The region cannot provide or decode the required bytes.</exception>
    public object? ReadValue(
        ReadOnlyMemory<byte> source,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore(source.Span, elementNameOrPath, variables, options);
    }

    /// <summary>Reads and checks one typed value directly from a byte span.</summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="source">The complete byte region available to this operation.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value converted or bound to <typeparamref name="T"/>.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The bytes cannot be decoded or the result cannot be bound to <typeparamref name="T"/>.</exception>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore<T>(source, elementNameOrPath, variables, options);
    }

    /// <summary>Reads and checks one typed value directly from read-only memory.</summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="source">The complete memory region available to this synchronous operation.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value converted or bound to <typeparamref name="T"/>.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The bytes cannot be decoded or the result cannot be bound to <typeparamref name="T"/>.</exception>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlyMemory<byte> source,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadMemoryValueCore<T>(source.Span, elementNameOrPath, variables, options);
    }

    /// <summary>Attempts one typed span read and returns false only for expected CStructSharp domain failures.</summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="source">The complete byte region available to this operation.</param>
    /// <param name="value">Receives the typed result on success, or the default value of <typeparamref name="T"/> on failure.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> for a categorized CStructSharp layout, path, or read failure.</returns>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlySpan<byte> source,
        [MaybeNullWhen(false)] out T value,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        try
        {
            value = this.ReadMemoryValueCore<T>(source, elementNameOrPath, variables, options);
            return true;
        }
        catch (CStructException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Attempts one typed memory read and returns false only for expected CStructSharp domain failures.</summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="source">The complete memory region available to this synchronous operation.</param>
    /// <param name="value">Receives the typed result on success, or the default value of <typeparamref name="T"/> on failure.</param>
    /// <param name="elementNameOrPath">The optional case-sensitive declaration or nested path; <see langword="null"/> selects the first composite.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> for a categorized CStructSharp layout, path, or read failure.</returns>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlyMemory<byte> source,
        [MaybeNullWhen(false)] out T value,
        string? elementNameOrPath = null,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.TryReadValue(source.Span, out value, elementNameOrPath, variables, options);
    }

    /// <summary>
    ///     Serializes into caller-owned storage and returns the initialized prefix length. Excess capacity is left
    ///     unchanged; insufficient capacity fails with <see cref="CStructWriteException"/>. Pointer coordinates are
    ///     zero-based within the destination span. A failure after output begins may leave an initialized prefix
    ///     changed because caller-owned storage cannot be rolled back.
    /// </summary>
    /// <param name="destination">Caller-owned storage that receives the serialized prefix.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to serialize.</param>
    /// <param name="data">The scalar, dynamic object, POCO, collection, pointer, enum, or union value to encode.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, binding rules, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of bytes initialized at the start of <paramref name="destination"/>.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value is invalid or the destination is too small; an initialized prefix may remain changed.</exception>
    public int Serialize(
        Span<byte> destination,
        string elementNameOrPath,
        object data,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null)
    {
        return this.SerializeToMemoryCore(destination, elementNameOrPath, data, variables, options);
    }

    /// <summary>
    ///     Appends one serialized region directly to an <see cref="IBufferWriter{T}"/> and returns the number of bytes
    ///     appended. Pointer coordinates are relative to the start of this appended region. A later failure cannot
    ///     retract writer windows that have already been advanced; callers requiring rollback must stage the writer.
    /// </summary>
    /// <param name="destination">The caller-owned buffer writer to append to.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to serialize.</param>
    /// <param name="data">The scalar, dynamic object, POCO, collection, pointer, enum, or union value to encode.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional write limits, binding rules, and pointer settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of bytes appended to <paramref name="destination"/>.</returns>
    /// <exception cref="CStructPathException">The requested path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructWriteException">The value cannot be encoded; already advanced writer windows cannot be retracted.</exception>
    public long Serialize(
        IBufferWriter<byte> destination,
        string elementNameOrPath,
        object data,
        IReadOnlyDictionary<string, int>? variables = null,
        WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var stream = new BufferWriterStream(destination);
        this.WriteStreamCore(
            stream,
            elementNameOrPath,
            data,
            LayoutVariableInput.FromIntegers(variables),
            options);
        return stream.Complete();
    }

    /// <summary>Runs the existing composite parser over one synchronously pinned read-only region.</summary>
    private unsafe dynamic ParseMemoryCore(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.ParseStreamCore(
                stream,
                elementNameOrPath ?? this.GetFirstCompiledStructName(),
                LayoutVariableInput.FromIntegers(variables),
                options);
        }
    }

    /// <summary>Runs the existing natural-value reader over one synchronously pinned read-only region.</summary>
    private unsafe object? ReadMemoryValueCore(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.ReadValueCore(
                stream,
                elementNameOrPath ?? this.GetFirstCompiledStructName(),
                LayoutVariableInput.FromIntegers(variables),
                options);
        }
    }

    /// <summary>Runs the existing typed-value reader over one synchronously pinned read-only region.</summary>
    private unsafe T ReadMemoryValueCore<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.ReadValue<T>(
                stream,
                elementNameOrPath ?? this.GetFirstCompiledStructName(),
                variables,
                options);
        }
    }

    /// <summary>Runs the existing writer once against an initially empty logical extent over caller storage.</summary>
    private unsafe int SerializeToMemoryCore(
        Span<byte> destination,
        string elementNameOrPath,
        object data,
        IReadOnlyDictionary<string, int>? variables,
        WriteOptions? options)
    {
        fixed (byte* buffer = destination)
        {
            using var stream = new FixedBufferStream(buffer, destination.Length, writable: true);
            this.WriteStreamCore(
                stream,
                elementNameOrPath,
                data,
                LayoutVariableInput.FromIntegers(variables),
                options);
            return checked((int)stream.Length);
        }
    }
}
