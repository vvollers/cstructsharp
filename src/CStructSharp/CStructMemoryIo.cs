namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>
///     Provides synchronous zero-copy memory input and caller-owned memory output entry points. Pointer coordinates
///     are zero-based within the supplied input or newly serialized output region.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Parses the composite a path selects from one synchronously pinned read-only region: a whole fixed root directly,
    ///     anything else with the compiled engine - a whole root straight over the region, a nested path through a
    ///     read-only stream over it, on which the path is resolved.
    /// </summary>
    /// <param name="source">The input; coordinate zero is its first byte.</param>
    /// <param name="elementNameOrPath">The root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">The caller's integer layout variables, or <see langword="null"/>.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="debug">Whether to record debug byte ranges.</param>
    /// <returns>The selected composite and the debug records (a shared empty list outside a debug parse).</returns>
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
        ReadProgram? engineRoot = this.SelectParse(segments, debug);
        fixed (byte* buffer = source)
        {
            // A whole root is read straight from the pinned region; a nested path is resolved on a read-only stream over
            // it, so its failures report the stream's positions.
            if (segments.Count > 1)
            {
                using var region = new FixedBufferStream(buffer, source.Length, writable: false);
                (List<DebugData> nestedRecords, object nested) = this.ParseNestedWithEngine(region, segments, input, settings, debug);
                return (nested, nestedRecords);
            }

            DebugRecorder? recorder = debug ? new DebugRecorder(trace: false) : null;
            StructValue value = this.ReadRootWithEngine(buffer, source.Length, segments, engineRoot, input, settings, recorder, out bool selected, out _);
            return (selected ? value : this.SelectParsedRoot(value, segments), recorder?.Records ?? NoDebugData);
        }
    }

    /// <summary>
    ///     Reads the natural value a path selects from one synchronously pinned read-only region: a whole fixed root
    ///     directly, anything else with the compiled engine straight over the region.
    /// </summary>
    /// <param name="source">The input; coordinate zero is its first byte.</param>
    /// <param name="elementNameOrPath">The root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">The caller's integer layout variables, or <see langword="null"/>.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The value, or <see langword="null"/> for a null pointer's <c>.value</c>.</returns>
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
        ReadProgram? engineRoot = this.SelectValueRead(segments);
        fixed (byte* buffer = source)
        {
            return this.ReadValueWithEngine(buffer, source.Length, segments, engineRoot, input, settings, out _);
        }
    }

    /// <summary>
    ///     Reads the value a path selects from one synchronously pinned read-only region and maps it to
    ///     <typeparamref name="T"/>: a whole fixed root directly, anything else with the compiled engine straight over the
    ///     region, converted as <see cref="ReadTypedValueCore{T}"/> converts it.
    /// </summary>
    /// <typeparam name="T">The CLR type the value is converted to.</typeparam>
    /// <param name="source">The input; coordinate zero is its first byte.</param>
    /// <param name="elementNameOrPath">The root name or nested path; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="variables">The caller's integer layout variables, or <see langword="null"/>.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The converted value.</returns>
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
                object? naturalValue = this.ReadValueWithEngine(buffer, source.Length, segments, this.SelectValueRead(segments), input, settings, out position);

                return (T)TypedValueConverter.Convert(naturalValue, typeof(T), ExceptionContext.FormatPath(segments))!;
            }
            catch (CStructException exception)
            {
                ExceptionContext.Attach(exception, segments, position);
                throw;
            }
        }
    }

    /// <summary>
    ///     Serializes into caller storage: the direct fixed-root path first, then the compiled engine into the pinned
    ///     storage, whose logical extent starts empty.
    /// </summary>
    /// <param name="destination">The caller's storage; the value is written from its first byte.</param>
    /// <param name="elementNameOrPath">The root name or nested path to write.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The caller's integer layout variables, or <see langword="null"/>.</param>
    /// <param name="options">The write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The number of bytes written at the storage's start.</returns>
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

        WritePreparation request = this.PrepareWrite(null, elementNameOrPath, LayoutVariableInput.FromIntegers(variables), options);
        fixed (byte* buffer = destination)
        {
            try
            {
                return WriteEngine.SerializeToSpan(this, request, buffer, destination.Length, data);
            }
            finally
            {
                request.Slots.Dispose();
            }
        }
    }
}
