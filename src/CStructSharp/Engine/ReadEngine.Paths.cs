namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The path operations of the compiled engine: a selected read (<c>ReadValue</c> of a path, and
///     <c>ReadValueWithDebug</c>, which records what it reads), a parse of a nested
///     struct (<c>Parse</c> and the debug parses of a path), <c>ResolveAddress</c> and <c>GetArrayLength</c>. Each
///     resolves its path with the <see cref="TargetResolver"/> on the operation's cursor and slots, then reads what the
///     path selects: from its exact address, standalone, at the nesting and pointer depth the path reached.
/// </summary>
internal static partial class ReadEngine
{
    /// <summary>
    ///     Reads the natural value a path selects from a caller's stream: a bare root (a root array's count taken first, as
    ///     the path resolver takes it), or the member, element, pointer address or pointer target a longer path reaches. The
    ///     final position is written back to the stream; a failure carries the path and offset.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="root">The root's read program for a bare root; <see langword="null"/> for a nested path or an undeclared root.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The value, or <see langword="null"/> for a null pointer's <c>.value</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructException">The path cannot be resolved or the input cannot be read; the path and offset are attached.</exception>
    public static object? ReadValue(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram? root, VariableSlots slots, in ReadOperationSettings options)
    {
        ReadOperationSettings.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options, null);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                return RunValue(ref memory, ref state, segments, root, stream);
            }

            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            return RunValue(ref cursor, ref state, segments, root, stream);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Reads the natural value a path selects from a pinned memory region, the input's byte 0 at
    ///     <paramref name="region"/>: what <see cref="ReadValue(CStruct, Stream, IReadOnlyList{PathSegment}, ReadProgram, VariableSlots, in ReadOperationSettings)"/>
    ///     does over a stream of the same bytes, without the stream.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="root">The root's read program for a bare root; <see langword="null"/> for a nested path or an undeclared root.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The value, or <see langword="null"/> for a null pointer's <c>.value</c>.</returns>
    /// <exception cref="CStructException">The path cannot be resolved or the input cannot be read; the path and offset are attached.</exception>
    public static unsafe object? ReadValue(CStruct layout, byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram? root, VariableSlots slots, in ReadOperationSettings options, out long position)
    {
        ReadOperationSettings.ValidateSettings(options);
        var state = new ReadEngineState(layout, slots, options, null);
        try
        {
            var cursor = new MemoryReadCursor(region, length, 0, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, options.ContinuedInputLength);
            try
            {
                return RunValue(ref cursor, ref state, segments, root, null);
            }
            finally
            {
                position = cursor.Position;
            }
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Reads the natural value a path selects from a caller's stream as
    ///     <see cref="ReadValue(CStruct, Stream, IReadOnlyList{PathSegment}, ReadProgram, VariableSlots, in ReadOperationSettings)"/>
    ///     does, recording every value read (<c>ReadValueWithDebug</c>): a bare root through its debug program, and a longer
    ///     path's target under the path a whole-root debug parse gives it.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="root">The root's debug program for a bare root; <see langword="null"/> for a nested path or an undeclared root.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder that receives the records.</param>
    /// <returns>The value, or <see langword="null"/> for a null pointer's <c>.value</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructException">The path cannot be resolved or the input cannot be read; the path and offset are attached.</exception>
    public static object? ReadValueWithDebug(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram? root, VariableSlots slots, in ReadOperationSettings options, DebugRecorder debug)
    {
        ReadOperationSettings.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options, debug);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                return RunValue(ref memory, ref state, segments, root, stream);
            }

            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            return RunValue(ref cursor, ref state, segments, root, stream);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Reads the natural value a path selects from a pinned memory region with every value recorded: what
    ///     <see cref="ReadValueWithDebug(CStruct, Stream, IReadOnlyList{PathSegment}, ReadProgram, VariableSlots, in ReadOperationSettings, DebugRecorder)"/>
    ///     does over a stream of the same bytes, without the stream.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="root">The root's debug program for a bare root; <see langword="null"/> for a nested path or an undeclared root.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder that receives the records.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The value, or <see langword="null"/> for a null pointer's <c>.value</c>.</returns>
    /// <exception cref="CStructException">The path cannot be resolved or the input cannot be read; the path and offset are attached.</exception>
    public static unsafe object? ReadValueWithDebug(CStruct layout, byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram? root, VariableSlots slots, in ReadOperationSettings options, DebugRecorder debug, out long position)
    {
        ReadOperationSettings.ValidateSettings(options);
        var state = new ReadEngineState(layout, slots, options, debug);
        try
        {
            var cursor = new MemoryReadCursor(region, length, 0, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, options.ContinuedInputLength);
            try
            {
                return RunValue(ref cursor, ref state, segments, root, null);
            }
            finally
            {
                position = cursor.Position;
            }
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Reads the struct or union a nested path selects from a caller's stream (<c>Parse</c> and <c>ParseWithDebug</c>
    ///     of a path): the path is resolved, then the composite is read at its address, recorded under the path's names in
    ///     a debug parse.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The parsed path, more than one segment.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse; <see langword="null"/> for an ordinary parse.</param>
    /// <returns>The struct or union value.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructPathException">The path does not select a struct or union.</exception>
    /// <exception cref="CStructException">The path cannot be resolved or the input cannot be read; the path and offset are attached.</exception>
    public static object ReadComposite(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, VariableSlots slots, in ReadOperationSettings options, DebugRecorder? debug)
    {
        ReadOperationSettings.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options, debug);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                return RunComposite(ref memory, ref state, segments, stream);
            }

            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            return RunComposite(ref cursor, ref state, segments, stream);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Reads the struct or union a nested path selects from a pinned memory region, the input's byte 0 at
    ///     <paramref name="region"/>: what <see cref="ReadComposite(CStruct, Stream, IReadOnlyList{PathSegment}, VariableSlots, in ReadOperationSettings, DebugRecorder)"/>
    ///     does over a stream of the same bytes, without the stream.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path, more than one segment.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse; <see langword="null"/> for an ordinary parse.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The struct or union value.</returns>
    /// <exception cref="CStructPathException">The path does not select a struct or union.</exception>
    /// <exception cref="CStructException">The path cannot be resolved or the input cannot be read; the path and offset are attached.</exception>
    public static unsafe object ReadComposite(CStruct layout, byte* region, int length, IReadOnlyList<PathSegment> segments, VariableSlots slots, in ReadOperationSettings options, DebugRecorder? debug, out long position)
    {
        ReadOperationSettings.ValidateSettings(options);
        var state = new ReadEngineState(layout, slots, options, debug);
        try
        {
            var cursor = new MemoryReadCursor(region, length, 0, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, options.ContinuedInputLength);
            try
            {
                return RunComposite(ref cursor, ref state, segments, null);
            }
            finally
            {
                position = cursor.Position;
            }
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Returns the absolute position a path selects in a caller's stream (<c>ResolveAddress</c>), reading only what the
    ///     path's placement depends on. The stream's position is restored afterwards; a failure carries the path and the
    ///     offset the walk reached.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The selected storage's position (the pointed-to storage for a <c>.value</c> path).</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructException">The path cannot be resolved; the path and offset are attached.</exception>
    public static long ResolveAddress(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, VariableSlots slots, in ReadOperationSettings options)
    {
        ReadOperationSettings.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options, null);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                return RunAddress(ref memory, ref state, segments);
            }

            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            return RunAddress(ref cursor, ref state, segments);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Returns the position a path selects in a pinned memory region, the input's byte 0 at <paramref name="region"/>:
    ///     what <see cref="ResolveAddress(CStruct, Stream, IReadOnlyList{PathSegment}, VariableSlots, in ReadOperationSettings)"/>
    ///     does over a stream of the same bytes, without the stream.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The selected storage's position in bytes from the region's start (the pointed-to storage for a <c>.value</c> path).</returns>
    /// <exception cref="CStructException">The path cannot be resolved; the path and offset are attached.</exception>
    public static unsafe long ResolveAddress(CStruct layout, byte* region, int length, IReadOnlyList<PathSegment> segments, VariableSlots slots, in ReadOperationSettings options)
    {
        ReadOperationSettings.ValidateSettings(options);
        var state = new ReadEngineState(layout, slots, options, null);
        try
        {
            var cursor = new MemoryReadCursor(region, length, 0, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, options.ContinuedInputLength);
            return RunAddress(ref cursor, ref state, segments);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Returns the element count of the array a path selects, or the character count of its terminated string
    ///     (<c>GetArrayLength</c>), reading only what determines it. The stream's position is restored afterwards.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="path">The path as the caller spelled it, for the failure that it selects no array or string.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructPathException">The path does not select an array or string.</exception>
    /// <exception cref="CStructException">The path cannot be resolved or the string read; the path and offset are attached.</exception>
    public static int GetArrayLength(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, string path, VariableSlots slots, in ReadOperationSettings options)
    {
        ReadOperationSettings.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options, null);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                return RunLength(ref memory, ref state, segments, path);
            }

            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            return RunLength(ref cursor, ref state, segments, path);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Returns the element or character count a path selects in a pinned memory region, the input's byte 0 at
    ///     <paramref name="region"/>: what <see cref="GetArrayLength(CStruct, Stream, IReadOnlyList{PathSegment}, string, VariableSlots, in ReadOperationSettings)"/>
    ///     does over a stream of the same bytes, without the stream.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="path">The path as the caller spelled it, for the failure that it selects no array or string.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The count.</returns>
    /// <exception cref="CStructPathException">The path does not select an array or string.</exception>
    /// <exception cref="CStructException">The path cannot be resolved or the string read; the path and offset are attached.</exception>
    public static unsafe int GetArrayLength(CStruct layout, byte* region, int length, IReadOnlyList<PathSegment> segments, string path, VariableSlots slots, in ReadOperationSettings options)
    {
        ReadOperationSettings.ValidateSettings(options);
        var state = new ReadEngineState(layout, slots, options, null);
        try
        {
            var cursor = new MemoryReadCursor(region, length, 0, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, options.ContinuedInputLength);
            return RunLength(ref cursor, ref state, segments, path);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Runs a selected read and writes the final position back, then attaches the path and offset to a failure: a
    ///     failure of the resolution carries the position the walk reached, one of the read the position after it. With a
    ///     recorder on the state (a debug value read) the target is read with its records.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, which holds the recorder of a debug value read.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="root">
    ///     The root's read program (its debug program in a debug value read), which reads a bare root; <see langword="null"/>
    ///     for a nested path or an undeclared root.
    /// </param>
    /// <param name="stream">The caller's stream, whose position a failure reports; <see langword="null"/> for a memory region.</param>
    /// <returns>The value.</returns>
    private static object? RunValue<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments, ReadProgram? root, Stream? stream)
        where TCursor : struct, IReadCursor
    {
        try
        {
            try
            {
                if (segments.Count == 1)
                {
                    // A bare root is resolved as a path before it is read: a root the layout does not declare fails there, and
                    // a root array's count the resolver takes by its own rules (a runtime-sized, to-end or multidimensional
                    // one) is taken and checked first; any other root's checks, a terminated array's scan included, are the
                    // read's own.
                    if (root is null || (root.Fields is [{ } only,] && EnginePrograms.ResolvesCountFirst(only)))
                    {
                        _ = Resolve(ref cursor, ref state, segments, null);
                    }

                    StructValue value = ReadRootValue(ref cursor, ref state, root!, segments[0].Name, out bool selected);
                    return selected ? value : CStruct.ExtractOnlyValue(value, segments[0].Name);
                }

                // A debug read collects the names the target's records carry while the path is resolved.
                List<string>? prefix = state.Debug is not null ? [] : null;
                ResolvedPath target = Resolve(ref cursor, ref state, segments, prefix, readsTarget: true);
                return ReadTarget(ref cursor, ref state, target, prefix);
            }
            finally
            {
                cursor.FlushPosition();
            }
        }
        catch (CStructException exception)
        {
            AttachContext(exception, segments, ref cursor, stream);
            throw;
        }
    }

    /// <summary>Runs a nested parse: resolves the path, then reads the struct or union it selects; see <see cref="RunValue{TCursor}"/> for the failure context.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, which holds the recorder of a debug parse.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="stream">The caller's stream, whose position a failure reports; <see langword="null"/> for a memory region.</param>
    /// <returns>The struct or union value.</returns>
    private static object RunComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments, Stream? stream)
        where TCursor : struct, IReadCursor
    {
        try
        {
            try
            {
                List<string>? prefix = state.Debug is not null ? [] : null;
                ResolvedPath target = Resolve(ref cursor, ref state, segments, prefix);
                return ReadTargetComposite(ref cursor, ref state, target, segments, prefix);
            }
            finally
            {
                cursor.FlushPosition();
            }
        }
        catch (CStructException exception)
        {
            AttachContext(exception, segments, ref cursor, stream);
            throw;
        }
    }

    /// <summary>
    ///     Runs an address query: resolves the path, then restores the operation's starting position. A failure carries the
    ///     position the walk reached, attached before the position is restored.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="segments">The parsed path.</param>
    /// <returns>The selected storage's position.</returns>
    private static long RunAddress<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments)
        where TCursor : struct, IReadCursor
    {
        long original = cursor.Position;
        try
        {
            return Resolve(ref cursor, ref state, segments, null).Address;
        }
        finally
        {
            cursor.Position = original;
            cursor.FlushPosition();
        }
    }

    /// <summary>
    ///     Runs a length query: resolves the path, takes the count its array target carries, or reads its terminated string
    ///     and counts the characters; restores the starting position whatever happens. A failure carries the position it
    ///     happened at.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="path">The path as the caller spelled it.</param>
    /// <returns>The count.</returns>
    private static int RunLength<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments, string path)
        where TCursor : struct, IReadCursor
    {
        long original = cursor.Position;
        try
        {
            ResolvedPath target = Resolve(ref cursor, ref state, segments, null);
            CompiledField field = target.Effective ?? throw new CStructPathException("Path does not resolve to an array or string field: " + path);
            if (field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
            {
                return target.ArrayLength ?? throw new CStructPathException("Resolved array target has no compiled length.");
            }

            Span<byte> scratch = stackalloc byte[ScratchSize];
            if (field.Array.Kind == CompiledArrayKind.Flexible && field.IsCharacterArray)
            {
                if (field.TerminatedCodecId < 0)
                {
                    throw new InvalidOperationException("Resolved string target has no compiled reader.");
                }

                cursor.Position = target.Address;
                return ((string)ReadCodecValue(ref cursor, PrimitiveCodec.Resolve(field.DisplayTypeSpelling, field.LayoutLittleEndian), scratch)).Length;
            }

            if (field.Array.Kind == CompiledArrayKind.Scalar && field.Codec.IsTerminatedText)
            {
                if (field.CodecId < 0)
                {
                    throw new InvalidOperationException("Resolved named string target has no compiled reader.");
                }

                cursor.Position = target.Address;
                return ((string)ReadCodecValue(ref cursor, field.Codec, scratch)).Length;
            }

            throw new CStructPathException("Path does not resolve to an array or string: " + path);
        }
        catch (CStructException exception)
        {
            AttachContext(exception, segments, ref cursor, null);
            throw;
        }
        finally
        {
            cursor.Position = original;
            cursor.FlushPosition();
        }
    }

    /// <summary>
    ///     Resolves a path on the operation's cursor; a failure of the resolution carries the path and the position the walk
    ///     reached, attached before anything else happens.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="debugPrefix">The list receiving the debug path's names, or <see langword="null"/>.</param>
    /// <param name="readsTarget">Whether the target's value is read next (see <see cref="TargetResolver.Resolve{TCursor}"/>).</param>
    /// <returns>The resolved target.</returns>
    private static ResolvedPath Resolve<TCursor>(ref TCursor cursor, ref ReadEngineState state, IReadOnlyList<PathSegment> segments, List<string>? debugPrefix, bool readsTarget = false)
        where TCursor : struct, IReadCursor
    {
        try
        {
            return TargetResolver.Resolve(ref cursor, ref state, segments, debugPrefix, readsTarget);
        }
        catch (CStructException exception)
        {
            AttachContext(exception, segments, ref cursor, null);
            throw;
        }
    }

    /// <summary>
    ///     Attaches the path and a position to a failure that has none yet: the caller's stream's position (after the cursor
    ///     wrote its position back), or the cursor's; a position that cannot be read is left out, so the original failure
    ///     is never replaced.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="exception">The failure.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="stream">The caller's stream, or <see langword="null"/> to report the cursor's position.</param>
    private static void AttachContext<TCursor>(CStructException exception, IReadOnlyList<PathSegment> segments, ref TCursor cursor, Stream? stream)
        where TCursor : struct, IReadCursor
    {
        if (stream is not null)
        {
            ExceptionContext.Attach(exception, segments, stream);
            return;
        }

        long? position;
        try
        {
            position = cursor.Position;
        }
        catch (Exception)
        {
            // A secondary diagnostic failure must never replace the already-classified primary failure.
            position = null;
        }

        exception.AttachContext(ExceptionContext.FormatPath(segments), position);
    }

    /// <summary>
    ///     Reads what a resolved path selects: a pointer's stored address; a pointer's target (nothing for a null
    ///     pointer), following any levels left; a struct or union at its address; or the selected field, element or row
    ///     read standalone from its address (a bitfield from the unit its struct placed), at the nesting and pointer
    ///     depth the path reached.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The resolved target (not a root).</param>
    /// <param name="debugPrefix">The names a debug read records the target under; <see langword="null"/> outside a debug read.</param>
    /// <returns>The value.</returns>
    private static object? ReadTarget<TCursor>(ref TCursor cursor, ref ReadEngineState state, in ResolvedPath target, List<string>? debugPrefix)
        where TCursor : struct, IReadCursor
    {
        if (debugPrefix is not null)
        {
            return ReadRecordedTarget(ref cursor, ref state, target, DebugPath.FromNames(debugPrefix));
        }

        LayoutCompilation compilation = state.Layout.Compilation;
        TargetProgramCache programs = compilation.SlotTable.TargetPrograms;
        Span<byte> scratch = stackalloc byte[ScratchSize];
        if (target.Kind == ResolvedTargetKind.PointerAddress)
        {
            cursor.Position = target.Address;
            return ReadPointerAddress(ref cursor, ref state, scratch);
        }

        if (target.Kind == ResolvedTargetKind.PointerValue)
        {
            if (target.PointerTargetAddress == 0)
            {
                return null;
            }

            cursor.Position = target.Address;
            state.StructureDepth = target.ContainingStructureDepth;
            state.PointerDepth = target.PointerAccessorsConsumed;
            CompiledField pointer = target.Effective!;
            if (target.RemainingPointerDepth > 0)
            {
                // The levels left are read and followed in place from the target, as a pointer with that many levels.
                CompiledField levels = programs.GetPointerView(target.Declared!, target.Indexes, pointer, target.RemainingPointerDepth, state.Layout.PointerSize);
                ReadPointerTarget remaining = programs.GetPointerTarget(compilation, target.Declared!, target.Indexes, levels);
                return ReadPointerValue(ref cursor, ref state, remaining, target.RemainingPointerDepth, -1, scratch);
            }

            return ReadPointerTargetValue(ref cursor, ref state, programs.GetPointerTarget(compilation, target.Declared!, target.Indexes, pointer), 1, scratch);
        }

        bool composite = (!target.IsArray || target.SelectsArrayElement) &&
                         target.RemainingPointerDepth == 0 &&
                         target.TargetComposite is not null &&
                         target.Effective?.PointerDepth == 0;
        cursor.Position = target.Address;
        state.StructureDepth = target.ContainingStructureDepth;
        state.PointerDepth = target.PointerAccessorsConsumed;
        if (composite)
        {
            return ReadCompositeAt(ref cursor, ref state, target.TargetComposite!, null);
        }

        // The selected field is read standalone: resolution already placed it, so it is not aligned again.
        CompiledField selected = target.Effective!;
        if (target.BitStorageSize > 0)
        {
            // The unit must end at a representable position before anything of it is read.
            _ = checked(target.Address + target.BitStorageSize);
            state.SeededBitOffset = target.BitOffset;
            state.SeededUnitSize = target.BitStorageSize;
        }

        ReadProgram program = programs.GetSelection(compilation, target.Declared!, target.Indexes, selected);
        var container = new StructValue(program.Shape);
        RunFrame(ref cursor, ref state, program, container);
        return CStruct.ExtractOnlyValue(container, selected.Name);
    }

    /// <summary>
    ///     Reads what a resolved path selects as <see cref="ReadTarget{TCursor}"/> does, recording every value read under
    ///     the path a whole-root debug parse gives it (<c>ReadValueWithDebug</c>): a pointer's stored address as one
    ///     record over its storage, under the pointer's path; a pointer's target under the selected path, which ends in
    ///     <c>value</c>, as <see cref="ReadRecordedPointerTarget{TCursor}"/> records it, and nothing for a null pointer; a
    ///     struct or union through its debug program; and a selected field,
    ///     element or row through its debug read - a scalar as one record, an array as one record per element (a struct
    ///     element's members under the element's path), a bitfield as one record over its storage unit.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="target">The resolved target (not a root).</param>
    /// <param name="path">The target's debug path, as the resolver named it.</param>
    /// <returns>The value.</returns>
    private static object? ReadRecordedTarget<TCursor>(ref TCursor cursor, ref ReadEngineState state, in ResolvedPath target, DebugPath? path)
        where TCursor : struct, IReadCursor
    {
        DebugRecorder debug = state.Debug!;
        LayoutCompilation compilation = state.Layout.Compilation;
        TargetProgramCache programs = compilation.SlotTable.TargetPrograms;
        Span<byte> scratch = stackalloc byte[ScratchSize];
        if (target.Kind == ResolvedTargetKind.PointerAddress)
        {
            cursor.Position = target.Address;
            long address = ReadPointerAddress(ref cursor, ref state, scratch);
            debug.Record(target.Address, cursor.Position, path, address, target.Effective!.DisplayTypeSpelling);
            return address;
        }

        if (target.Kind == ResolvedTargetKind.PointerValue)
        {
            if (target.PointerTargetAddress == 0)
            {
                return null;
            }

            cursor.Position = target.Address;
            state.StructureDepth = target.ContainingStructureDepth;
            state.PointerDepth = target.PointerAccessorsConsumed;
            CompiledField pointer = target.Effective!;

            // The target is recorded as a whole-root debug parse records a followed target; its path already ends in `value`.
            int levels = target.RemainingPointerDepth;
            CompiledField reached = levels > 0 ? programs.GetPointerView(target.Declared!, target.Indexes, pointer, levels, state.Layout.PointerSize) : pointer;
            ReadPointerTarget description = programs.GetDebugPointerTarget(compilation, target.Declared!, target.Indexes, reached);
            return ReadRecordedPointerTarget(ref cursor, ref state, description, levels, levels > 0 ? -1 : 1, path, scratch);
        }

        bool composite = (!target.IsArray || target.SelectsArrayElement) &&
                         target.RemainingPointerDepth == 0 &&
                         target.TargetComposite is not null &&
                         target.Effective?.PointerDepth == 0;
        cursor.Position = target.Address;
        state.StructureDepth = target.ContainingStructureDepth;
        state.PointerDepth = target.PointerAccessorsConsumed;
        if (composite)
        {
            return ReadCompositeAt(ref cursor, ref state, target.TargetComposite!, path);
        }

        CompiledField selected = target.Effective!;
        if (target.BitStorageSize > 0)
        {
            // As in ReadTarget: the unit must end at a representable position before anything of it is read.
            _ = checked(target.Address + target.BitStorageSize);
            state.SeededBitOffset = target.BitOffset;
            state.SeededUnitSize = target.BitStorageSize;
        }

        // The one member of the selection's debug read takes the selected path, indexes included, for its records.
        ReadProgram program = programs.GetDebugSelection(compilation, target.Declared!, target.Indexes, selected);
        var container = new StructValue(program.Shape);
        debug.Selected = path;
        try
        {
            RunFrame(ref cursor, ref state, program, container);
        }
        finally
        {
            debug.Selected = null;
        }

        return CStruct.ExtractOnlyValue(container, selected.Name);
    }

    /// <summary>
    ///     Reads the struct or union a nested parse selects at its address, after checking that the path selects one:
    ///     not a pointer's storage, no pointer level left, not a whole array or row of them, and a composite type.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The resolved target.</param>
    /// <param name="segments">The parsed path, which the failure names.</param>
    /// <param name="debugPrefix">The names a debug parse records the composite under, or <see langword="null"/>.</param>
    /// <returns>The struct or union value.</returns>
    /// <exception cref="CStructPathException">The path does not select one struct or union.</exception>
    private static object ReadTargetComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, in ResolvedPath target, IReadOnlyList<PathSegment> segments, List<string>? debugPrefix)
        where TCursor : struct, IReadCursor
    {
        bool stopsAtPointerStorage = target.Kind == ResolvedTargetKind.PointerAddress ||
                                     (target.Kind is ResolvedTargetKind.Field or ResolvedTargetKind.ArrayElement && target.Effective?.PointerDepth > 0);

        // A whole array (or a row of one) of structs is a list, which only a value read returns.
        bool wholeArray = target.IsArray && !target.SelectsArrayElement;
        if (stopsAtPointerStorage || wholeArray || target.RemainingPointerDepth > 0 || target.TargetComposite is not { } composite)
        {
            throw new CStructPathException(
                $"'{ExceptionContext.FormatPath(segments)}' does not select a struct or union; use ReadValue or ReadValueWithDebug for a scalar, array, or pointer value.");
        }

        cursor.Position = target.Address;
        state.StructureDepth = target.ContainingStructureDepth;
        state.PointerDepth = target.PointerAccessorsConsumed;
        return ReadCompositeAt(ref cursor, ref state, composite, debugPrefix is null ? null : DebugPath.FromNames(debugPrefix));
    }

    /// <summary>
    ///     Reads a struct or union at the position of a resolved address: a union as a union value of its own, a struct
    ///     through its program (or its static plan); in a debug parse through its debug program, recorded under
    ///     <paramref name="path"/>.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the composite's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="composite">The struct or union.</param>
    /// <param name="path">The debug path of a debug parse; ignored otherwise.</param>
    /// <returns>The value.</returns>
    private static object ReadCompositeAt<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledCompositeType composite, DebugPath? path)
        where TCursor : struct, IReadCursor
    {
        SlotTable table = state.Slots.Table;
        if (state.Debug is not null)
        {
            ReadProgram recorded = table.DebugReadPrograms.GetComposite(state.Layout.Compilation, composite);
            if (recorded.Kind == ReadProgramKind.Union)
            {
                return ReadRecordedUnion(ref cursor, ref state, recorded, promoted: false, path);
            }

            var recordedValue = new StructValue(recorded.Shape);
            ReadRecordedComposite(ref cursor, ref state, recorded, recordedValue, path);
            return recordedValue;
        }

        ReadProgram program = table.ReadPrograms.GetComposite(state.Layout.Compilation, composite);
        if (program.Kind == ReadProgramKind.Union)
        {
            return ReadUnion(ref cursor, ref state, program, promoted: false);
        }

        var value = new StructValue(program.Shape);
        ReadComposite(ref cursor, ref state, program, value);
        return value;
    }
}
