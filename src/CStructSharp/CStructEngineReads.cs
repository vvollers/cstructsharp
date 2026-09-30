namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     The whole-root reads, nested parses and debug parses of the compiled engine, and its capture of the layout an update
///     compares.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Looks up the program of a parse once: a whole-root parse runs the root's program, a whole-root debug parse the
    ///     root's debug program, and a nested path the programs the path resolver finds as it walks.
    /// </summary>
    /// <param name="segments">The parsed path.</param>
    /// <param name="debug">Whether the parse records debug byte ranges.</param>
    /// <returns>
    ///     The root's program (its debug program for a debug parse) for a whole root; <see langword="null"/> for a nested
    ///     path, and for a root the layout does not declare, which the parse reports after resolving the variables.
    /// </returns>
    private ReadProgram? SelectParse(IReadOnlyList<PathSegment> segments, bool debug)
    {
        if (segments.Count == 1)
        {
            return debug
                       ? EnginePrograms.DebugRead(this.compilation, segments[0].Name)
                       : EnginePrograms.RootRead(this.compilation, segments[0].Name);
        }

        EnginePrograms.PathOperation(debug ? EngineOperation.DebugRead : EngineOperation.PathRead);
        return null;
    }

    /// <summary>
    ///     Parses the struct or union a nested path selects from a stream with the compiled engine: the caller's variables
    ///     are resolved into the layout's slots (a definition that cannot be resolved fails here, without a path), then the
    ///     source and settings are validated, the path resolved and the composite read - in a debug parse with every value
    ///     recorded under the path's names.
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The parsed path, more than one segment.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">Whether the parse records debug byte ranges.</param>
    /// <returns>The debug records (an empty shared list outside a debug parse) and the struct or union value.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructException">A definition cannot be resolved, the path cannot be resolved, or the input cannot be read.</exception>
    private (List<DebugData> DebugData, object Result) ParseNestedWithEngine(Stream stream, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables, in ReadOperationSettings options, bool debug)
    {
        DebugRecorder? recorder = debug ? new DebugRecorder(trace: false) : null;
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            object value = ReadEngine.ReadComposite(this, stream, segments, slots, options, recorder);
            return (recorder?.Records ?? NoDebugData, value);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>
    ///     Reads the struct or union a nested path selects from a pinned memory region with the compiled engine, as the
    ///     stream form does over a stream of the same bytes: the path is resolved, then the composite is read at its
    ///     address, recorded under the path's names in a debug parse.
    /// </summary>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path, more than one segment.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">Whether the parse records debug byte ranges.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The debug records (an empty shared list outside a debug parse) and the struct or union value.</returns>
    /// <exception cref="CStructException">A definition cannot be resolved, the path cannot be resolved, or the input cannot be read.</exception>
    private unsafe (List<DebugData> DebugData, object Result) ParseNestedWithEngine(byte* region, int length, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables, in ReadOperationSettings options, bool debug, out long position)
    {
        DebugRecorder? recorder = debug ? new DebugRecorder(trace: false) : null;
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            object value = ReadEngine.ReadComposite(this, region, length, segments, slots, options, recorder, out position);
            return (recorder?.Records ?? NoDebugData, value);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>
    ///     Reads the composite a path selects from a pinned memory region, optionally recording debug byte ranges, as
    ///     <c>ParseStreamCoreImpl</c> does over a stream of the same bytes: a nested path's composite, or a whole root's
    ///     value under its name.
    /// </summary>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="elementNameOrPath">The case-sensitive root name or nested path of a composite to read.</param>
    /// <param name="variables">The caller's layout variables, snapshotted before traversal.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="debug">Whether the parse records debug byte ranges.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The debug records (an empty shared list outside a debug parse) and the selected composite's value.</returns>
    /// <exception cref="CStructException">A definition cannot be resolved, the path cannot be resolved, or the input cannot be read.</exception>
    private unsafe (List<DebugData> DebugData, object Result) ParseRegionCore(byte* region, int length, string elementNameOrPath, in LayoutVariableInput variables, ReadOptions? options, bool debug, out long position)
    {
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        ReadProgram? root = this.SelectParse(segments, debug);
        if (segments.Count > 1)
        {
            return this.ParseNestedWithEngine(region, length, segments, variables, effectiveOptions, debug, out position);
        }

        DebugRecorder? recorder = debug ? new DebugRecorder(trace: false) : null;
        StructValue value = this.ReadRootWithEngine(region, length, segments, root, variables, effectiveOptions, recorder, out bool selected, out position);
        return (recorder?.Records ?? NoDebugData, selected ? value : this.SelectParsedRoot(value, segments));
    }

    /// <summary>
    ///     Runs the debug parse of a whole root from a stream with the compiled engine: the root's debug program records
    ///     every value read, and the records are returned with the value the root's name selects.
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">The root's debug program, or <see langword="null"/> for a root the layout does not declare.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The debug records and the selected value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot seek.</exception>
    /// <exception cref="CStructException">A definition cannot be resolved, the root is unknown, or the input cannot be read.</exception>
    private (List<DebugData> DebugData, object Result) ParseWithEngineDebug(Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram? program, in LayoutVariableInput variables, in ReadOperationSettings options)
    {
        var recorder = new DebugRecorder(trace: false);
        StructValue value = this.ReadRootWithEngine(stream, segments, program, variables, options, recorder, out bool selected);
        return (recorder.Records, selected ? value : this.SelectParsedRoot(value, segments));
    }

    /// <summary>
    ///     Reads a whole root from a stream with the compiled engine: the stream is checked (a debug parse needs it
    ///     seekable), the caller's variables resolved into the layout's slots (a definition that cannot be resolved fails
    ///     here, without a path), a root the layout does not declare is reported (without a path), then the source and
    ///     settings are validated and the root is read.
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">
    ///     The root's program, or its debug program for a debug parse; <see langword="null"/> for a root the layout does not
    ///     declare.
    /// </param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse; <see langword="null"/> for an ordinary read.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <returns>The selected value, or the root value holding the root's value under its name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">A debug parse's <paramref name="stream"/> cannot seek.</exception>
    /// <exception cref="CStructException">A definition cannot be resolved, the root is unknown, or the input cannot be read.</exception>
    private StructValue ReadRootWithEngine(Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram? program, in LayoutVariableInput variables, in ReadOperationSettings options, DebugRecorder? debug, out bool selected)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (debug is not null && !stream.CanSeek)
        {
            throw new ArgumentException("Debug mapping and address resolution require a seekable stream.", nameof(stream));
        }

        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            ReadProgram root = program ?? throw this.compiledModelQueries.UnknownRoot(segments[0].Name);
            return ReadEngine.ReadRoot(this, stream, segments, root, slots, options, debug, out selected);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>
    ///     Reads a whole root from a pinned memory region with the compiled engine: the caller's variables are resolved
    ///     into the layout's slots, a root the layout does not declare is reported (without a path), then the settings are
    ///     validated and the root is read.
    /// </summary>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">
    ///     The root's program, or its debug program for a debug parse; <see langword="null"/> for a root the layout does not
    ///     declare.
    /// </param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse; <see langword="null"/> for an ordinary read.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The selected value, or the root value holding the root's value under its name.</returns>
    /// <exception cref="CStructException">A definition cannot be resolved, the root is unknown, or the input cannot be read.</exception>
    private unsafe StructValue ReadRootWithEngine(byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram? program, in LayoutVariableInput variables, in ReadOperationSettings options, DebugRecorder? debug, out bool selected, out long position)
    {
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            ReadProgram root = program ?? throw this.compiledModelQueries.UnknownRoot(segments[0].Name);
            return ReadEngine.ReadRoot(this, region, length, segments, root, slots, options, debug, out selected, out position);
        }
        finally
        {
            slots.Dispose();
        }
    }
}
