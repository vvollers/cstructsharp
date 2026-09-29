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

/// <summary>The whole-root reads the compiled engine runs when <see cref="EngineSelector"/> selects it.</summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Makes the one engine decision of a parse: a whole-root parse asks for the root's program; a debug parse or a
    ///     nested path is declined (and recorded) for the interpreter.
    /// </summary>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="debug">Whether the parse records debug byte ranges.</param>
    /// <returns>The root's program when the engine runs the parse; otherwise <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the parse.</exception>
    private ReadProgram? SelectParse(in ReadOperationSettings options, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables, bool debug)
    {
        if (!debug && segments.Count == 1)
        {
            return EngineSelector.SelectRootRead(options.EngineSelection, this.compilation, segments[0].Name, variables, selectsValue: false);
        }

        EngineSelector.Decide(options.EngineSelection, debug ? EngineOperation.DebugRead : EngineOperation.PathRead);
        return null;
    }

    /// <summary>
    ///     Reads a whole root from a stream with the compiled engine, in the interpreter's order: the stream is checked,
    ///     the caller's variables resolved into the layout's slots (a definition that cannot be resolved fails here,
    ///     without a path), then the source and settings are validated and the root is read.
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">The root's eligible program.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <returns>The selected value, or the root value holding the root's value under its name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="CStructException">A definition cannot be resolved or the input cannot be read.</exception>
    private StructValue ReadRootWithEngine(Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram program, in LayoutVariableInput variables, in ReadOperationSettings options, out bool selected)
    {
        ArgumentNullException.ThrowIfNull(stream);
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.ReadRoot(this, stream, segments, program, slots, options, out selected);
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
    /// <param name="program">The root's eligible program.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The selected value, or the root value holding the root's value under its name.</returns>
    /// <exception cref="CStructException">A definition cannot be resolved or the input cannot be read.</exception>
    private unsafe StructValue ReadRootWithEngine(byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram program, in LayoutVariableInput variables, in ReadOperationSettings options, out bool selected, out long position)
    {
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.ReadRoot(this, region, length, segments, program, slots, options, out selected, out position);
        }
        finally
        {
            slots.Dispose();
        }
    }
}
