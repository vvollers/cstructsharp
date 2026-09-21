namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>
///     The <see cref="ReadOnlySequence{T}"/> forms of the read operations, for input that arrives in segments (a
///     <c>PipeReader</c>, a chain of pooled buffers). A single-segment sequence takes the span path with no copy; a
///     multi-segment one is copied into a pooled buffer bounded by <see cref="ReadOptions.MaxTotalBytesRead"/> plus
///     one byte, so a sequence longer than the budget fails with the budget text, as a stream would. Coordinates are
///     zero-based at the sequence's start.
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
}
