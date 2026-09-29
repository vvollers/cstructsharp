namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
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
        string path = elementNameOrPath ?? this.compiledModelQueries.GetFirstCompiledStructName();
        if (!debug && this.TryReadFixedRoot(source, path, variables, options, out StructValue? direct, out _))
        {
            return (direct, NoDebugData);
        }

        var input = LayoutVariableInput.FromIntegers(variables);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(path);
        ReadProgram? engineRoot = this.SelectParse(settings, segments, input, debug);
        fixed (byte* buffer = source)
        {
            // The engine reads the pinned region directly; the interpreter reads it through a read-only region stream.
            if (engineRoot is not null)
            {
                StructValue value = this.ReadRootWithEngine(buffer, source.Length, segments, engineRoot, input, settings, out bool selected, out _);
                return (selected ? value : this.SelectParsedRoot(value, segments), NoDebugData);
            }

            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            (List<DebugData> records, object result) = this.ParseWithInterpreter(stream, segments, input, settings, debug);
            return (result, records);
        }
    }

    /// <summary>Runs the existing natural-value reader over one synchronously pinned read-only region.</summary>
    private unsafe object? ReadMemoryValueCore(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options)
    {
        string path = elementNameOrPath ?? this.compiledModelQueries.GetFirstCompiledStructName();
        if (this.TryReadFixedRoot(source, path, variables, options, out StructValue? direct, out _))
        {
            return direct;
        }

        var input = LayoutVariableInput.FromIntegers(variables);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(path);
        ReadProgram? engineRoot = this.SelectValueRead(settings, segments, input);
        fixed (byte* buffer = source)
        {
            if (engineRoot is not null)
            {
                return this.ReadRootValueWithEngine(buffer, source.Length, segments, engineRoot, input, settings, out _);
            }

            using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
            return this.ReadValueWithInterpreter(stream, segments, input, settings);
        }
    }

    /// <summary>Runs the existing typed-value reader over one synchronously pinned read-only region.</summary>
    private unsafe T ReadMemoryValueCore<T>(
        ReadOnlySpan<byte> source,
        string? elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options)
    {
        string path = elementNameOrPath ?? this.compiledModelQueries.GetFirstCompiledStructName();
        if (this.TryReadFixedRoot(source, path, variables, options, out T direct))
        {
            return direct;
        }

        var input = LayoutVariableInput.FromIntegers(variables);
        IReadOnlyList<PathSegment> segments = this.ParsePath(path);
        long position = 0;
        fixed (byte* buffer = source)
        {
            try
            {
                // The natural value is converted to T exactly as ReadTypedValueCore converts it; a failure of the read
                // or the conversion carries the path and the position the read reached.
                ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);
                object? naturalValue;
                if (this.SelectValueRead(settings, segments, input) is { } engineRoot)
                {
                    naturalValue = this.ReadRootValueWithEngine(buffer, source.Length, segments, engineRoot, input, settings, out position);
                }
                else
                {
                    using var stream = new FixedBufferStream(buffer, source.Length, writable: false);
                    try
                    {
                        naturalValue = this.ReadValueWithInterpreter(stream, segments, input, settings);
                    }
                    finally
                    {
                        position = stream.Position;
                    }
                }

                return (T)TypedValueConverter.Convert(naturalValue, typeof(T), ExceptionContext.FormatPath(segments))!;
            }
            catch (CStructException exception)
            {
                ExceptionContext.Attach(exception, segments, position);
                throw;
            }
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
        if (this.TryWriteFixedRoot(destination, elementNameOrPath, data, variables, options, out int written))
        {
            return written;
        }

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
