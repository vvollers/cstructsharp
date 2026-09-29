namespace CStructSharp.Writing;

using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Engine;
using CStructSharp.Syntax;

/// <summary>
///     What a write settled before it writes (<c>CStruct.PrepareWrite</c>): its options, path and root, and either the
///     compiled engine's program with the variables as slots or the interpreter's variable dictionary.
/// </summary>
/// <param name="Options">The snapshotted, validated options; <see cref="UpdateOptions"/> switch on update semantics.</param>
/// <param name="Segments">The parsed path; the first segment names the root.</param>
/// <param name="ChildSegments">The segments after the root for a nested path, or <see langword="null"/> for a whole root.</param>
/// <param name="RootElement">The root's declaration.</param>
/// <param name="Variables">
///     The resolved variables as a dictionary: the interpreter's, or - for a nested path the engine writes - the ones the
///     path's index checks evaluate against; <see langword="null"/> when the engine writes a whole root.
/// </param>
/// <param name="Slots">The engine's variables when <paramref name="Program"/> is set; the writer disposes them.</param>
/// <param name="Program">
///     The program the engine runs - the root's, or for a nested path the selected member's written on its own - or
///     <see langword="null"/> when the interpreter writes.
/// </param>
internal readonly record struct WritePreparation(
    WriteOptions Options,
    IReadOnlyList<PathSegment> Segments,
    PathSegment[]? ChildSegments,
    CStructElement RootElement,
    Dictionary<string, Expr>? Variables,
    VariableSlots Slots,
    WriteProgram? Program);
