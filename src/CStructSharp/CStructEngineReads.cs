namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>The whole-root reads the compiled engine runs when <see cref="EngineSelector"/> selects it.</summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Reads a whole root with the compiled engine, in the interpreter's order: the stream is checked, the caller's
    ///     variables resolved into the layout's slots (a definition that cannot be resolved fails here, without a path),
    ///     then the source and settings are validated and the root is read (<see cref="ReadEngine.ReadRoot"/>).
    /// </summary>
    /// <param name="stream">The caller's source, positioned at the root.</param>
    /// <param name="segments">The one-segment path that names the root.</param>
    /// <param name="program">The root's eligible program.</param>
    /// <param name="variables">The caller's layout variables, integers.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The root value, holding the root's value under its name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="Diagnostics.CStructException">A definition cannot be resolved or the input cannot be read.</exception>
    private StructValue ReadRootWithEngine(Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram program, LayoutVariableInput variables, ReadOperationSettings options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.ReadRoot(this, stream, segments, program, slots, options);
        }
        finally
        {
            slots.Dispose();
        }
    }
}
