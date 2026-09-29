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
///     The whole-root reads and debug parses the compiled engine runs when <see cref="EngineSelector"/> selects it, and its
///     capture of the layout an update compares.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Makes the one engine decision of a parse: a whole-root parse asks for the root's program, a whole-root debug parse
    ///     for the root's debug program, and a nested path for its root's program (its debug program for a debug parse),
    ///     which proves every struct the path can reach readable.
    /// </summary>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="debug">Whether the parse records debug byte ranges.</param>
    /// <returns>The root's program (its debug program for a debug parse) when the engine runs the parse; otherwise <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the parse.</exception>
    private ReadProgram? SelectParse(in ReadOperationSettings options, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables, bool debug)
    {
        if (segments.Count == 1)
        {
            return debug
                       ? EngineSelector.SelectDebugRead(options.EngineSelection, this.compilation, segments[0].Name, variables)
                       : EngineSelector.SelectRootRead(options.EngineSelection, this.compilation, segments[0].Name, variables);
        }

        return EngineSelector.SelectPathRead(options.EngineSelection, this.compilation, segments[0].Name, variables, debug ? EngineOperation.DebugRead : EngineOperation.PathRead, debug);
    }

    /// <summary>
    ///     Parses the struct or union a nested path selects from a stream with the compiled engine, in the interpreter's
    ///     order: the caller's variables are resolved into the layout's slots (a definition that cannot be resolved fails
    ///     here, without a path), then the source and settings are validated, the path resolved and the composite read - in a
    ///     debug parse with every value recorded under the path's names.
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
    ///     Runs the debug parse of a whole root from a stream with the compiled engine: the root's debug program records
    ///     every value read, and the records are returned with the value the root's name selects.
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">The root's eligible debug program.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The debug records and the selected value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot seek.</exception>
    /// <exception cref="CStructException">A definition cannot be resolved or the input cannot be read.</exception>
    private (List<DebugData> DebugData, object Result) ParseWithEngineDebug(Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram program, in LayoutVariableInput variables, in ReadOperationSettings options)
    {
        var recorder = new DebugRecorder(trace: false);
        StructValue value = this.ReadRootWithEngine(stream, segments, program, variables, options, recorder, out bool selected);
        return (recorder.Records, selected ? value : this.SelectParsedRoot(value, segments));
    }

    /// <summary>
    ///     Reads a whole root from a stream with the compiled engine, in the interpreter's order: the stream is checked (a
    ///     debug parse needs it seekable), the caller's variables resolved into the layout's slots (a definition that cannot
    ///     be resolved fails here, without a path), then the source and settings are validated and the root is read.
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">The root's eligible program, or its debug program for a debug parse.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse; <see langword="null"/> for an ordinary read.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <returns>The selected value, or the root value holding the root's value under its name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">A debug parse's <paramref name="stream"/> cannot seek.</exception>
    /// <exception cref="CStructException">A definition cannot be resolved or the input cannot be read.</exception>
    private StructValue ReadRootWithEngine(Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram program, in LayoutVariableInput variables, in ReadOperationSettings options, DebugRecorder? debug, out bool selected)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (debug is not null && !stream.CanSeek)
        {
            throw new ArgumentException("Debug mapping and address resolution require a seekable stream.", nameof(stream));
        }

        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.ReadRoot(this, stream, segments, program, slots, options, debug, out selected);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>
    ///     Reads a whole root from a pinned memory region with the compiled engine, in the interpreter's order: the
    ///     caller's variables are resolved into the layout's slots, then the settings are validated and the root is read.
    /// </summary>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">The root's eligible program, or its debug program for a debug parse.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse; <see langword="null"/> for an ordinary read.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The selected value, or the root value holding the root's value under its name.</returns>
    /// <exception cref="CStructException">A definition cannot be resolved or the input cannot be read.</exception>
    private unsafe StructValue ReadRootWithEngine(byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram program, in LayoutVariableInput variables, in ReadOperationSettings options, DebugRecorder? debug, out bool selected, out long position)
    {
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.ReadRoot(this, region, length, segments, program, slots, options, debug, out selected, out position);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>
    ///     The compiled engine's counterpart of
    ///     <see cref="CaptureUpdateLayout(Stream, long, CStructElement, Dictionary{string, Expr}, ReadOperationSettings)"/>:
    ///     the same reading of the root with its debug program, giving the same records and conditional-layout trace entry
    ///     for entry, or <see langword="null"/> when the engine cannot read the root (an ineligible root, or expression
    ///     variables that make every field captured).
    /// </summary>
    /// <remarks>
    ///     An update runs on one implementation from start to end: the engine's update captures its layouts with the engine
    ///     (<c>ReadEngine.CaptureLayout</c>), the interpreter's with the interpreter. The differential tests hold this
    ///     counterpart to the interpreter's capture for every conditional root and terminated value they sweep.
    /// </remarks>
    /// <param name="stream">The data, the original or a staged copy.</param>
    /// <param name="origin">The root's position.</param>
    /// <param name="rootName">The root's name.</param>
    /// <param name="variables">The operation's layout variables.</param>
    /// <param name="options">The read settings.</param>
    /// <returns>Each value's path and byte range, then each conditional member's name, position and selection; or <see langword="null"/>.</returns>
    /// <exception cref="Diagnostics.CStructException">A definition cannot be resolved or the data cannot be read.</exception>
    internal (string Path, long Start, long End)[]? CaptureUpdateLayoutWithEngine(
        Stream stream, long origin, string rootName, in LayoutVariableInput variables, in ReadOperationSettings options)
    {
        if (EngineSelector.CapturesEveryField(this.compilation, variables) || this.compilation.GetRootDebugReadProgram(rootName).Program is not { } program)
        {
            return null;
        }

        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.CaptureLayout(this, stream, origin, program, slots, options);
        }
        finally
        {
            slots.Dispose();
        }
    }
}
