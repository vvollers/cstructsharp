namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     Provides synchronous zero-copy memory input and caller-owned memory output entry points. Pointer coordinates
///     are zero-based within the supplied input or newly serialized output region.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>Runs the existing composite parser over one synchronously pinned read-only region.</summary>
    private unsafe (object Value, List<DebugData> Debug) ParseMemoryCore(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options,
        bool debug)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            (List<DebugData> records, object value) = this.ParseStreamCoreImpl(
                stream,
                elementNameOrPath ?? this.compiledModelQueries.GetFirstCompiledStructName(),
                LayoutVariableInput.FromIntegers(variables),
                options,
                debug);
            return (value, records);
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
                elementNameOrPath ?? this.compiledModelQueries.GetFirstCompiledStructName(),
                LayoutVariableInput.FromIntegers(variables),
                options);
        }
    }

    /// <summary>Runs the existing typed-value reader over one synchronously pinned read-only region.</summary>
    private unsafe T ReadMemoryValueCore<T>(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options)
    {
        fixed (byte* buffer = source)
        {
            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.ReadTypedValueCore<T>(
                stream,
                elementNameOrPath ?? this.compiledModelQueries.GetFirstCompiledStructName(),
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
