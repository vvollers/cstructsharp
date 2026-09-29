namespace CStructSharp.Compilation.Programs;

/// <summary>What a <see cref="WriteProgram"/> writes, which decides how an executor enters and leaves it.</summary>
internal enum WriteProgramKind : byte
{
    /// <summary>
    ///     A struct written from a value of its own: one nesting level, placed from its own first byte, ending with its
    ///     tail padding. Cached per composite and shared by every struct that holds it.
    /// </summary>
    Composite,

    /// <summary>
    ///     An anonymous struct member whose members are promoted into its parent: its values come from the parent's value
    ///     and it is not a nesting level, but it places its members from its own first byte and has its own conditional
    ///     selection, scope and tail padding, as the interpreter writes it.
    /// </summary>
    Promoted,

    /// <summary>A root: one struct, one typedef or enum field written standalone (no composite places it), or a <c>#define</c>.</summary>
    Root,

    /// <summary>
    ///     A union: one segment of steps per member (<see cref="WriteProgram.UnionEntries"/>), each writing that member
    ///     standalone from the union's first byte into a staged copy of the union's storage; the executor runs only the
    ///     selected member's segment. Cached per composite.
    /// </summary>
    Union,
}
