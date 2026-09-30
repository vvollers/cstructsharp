namespace CStructSharp.Writing;

using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Engine;
using CStructSharp.Syntax;

/// <summary>
///     What a write settled before it writes (<c>CStruct.PrepareWrite</c>): its options, path and root, the variables as
///     the compiled engine's slots, and the program the engine runs.
/// </summary>
/// <param name="Options">The snapshotted, validated options; <see cref="UpdateOptions"/> switch on update semantics.</param>
/// <param name="Segments">The parsed path; the first segment names the root.</param>
/// <param name="ChildSegments">The segments after the root for a nested path, or <see langword="null"/> for a whole root.</param>
/// <param name="RootElement">The root's declaration.</param>
/// <param name="Variables">
///     The resolved variables as a dictionary, which a nested path's index checks evaluate against; <see langword="null"/>
///     for a whole root.
/// </param>
/// <param name="Slots">The operation's variables; the writer disposes them.</param>
/// <param name="Program">
///     The program the engine runs - the root's, or for a nested path the selected member's written on its own - or
///     <see langword="null"/> when the path selects no writable member or the root has no binary storage.
/// </param>
/// <param name="Unwritable">
///     When <paramref name="Program"/> is <see langword="null"/>, the failure the write reports once the value is
///     normalized; otherwise <see langword="null"/>.
/// </param>
internal readonly record struct WritePreparation(
    WriteOptions Options,
    IReadOnlyList<PathSegment> Segments,
    PathSegment[]? ChildSegments,
    CStructElement RootElement,
    Dictionary<string, Expr>? Variables,
    VariableSlots Slots,
    WriteProgram? Program,
    string? Unwritable);
