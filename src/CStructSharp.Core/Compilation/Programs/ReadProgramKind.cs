namespace CStructSharp.Compilation.Programs;

/// <summary>What a <see cref="ReadProgram"/> reads, which decides how an executor enters and leaves it.</summary>
internal enum ReadProgramKind : byte
{
    /// <summary>
    ///     A struct read into a value of its own: one nesting level, placed from its own first byte, ending past its
    ///     tail padding. Cached per composite and shared by every struct that holds it.
    /// </summary>
    Composite,

    /// <summary>
    ///     An anonymous struct member whose members are promoted into its parent: its values go to the value of the
    ///     nearest named struct around it and it is not a nesting level, but it places its members from its own first
    ///     byte and has its own conditional selection and scope, as the interpreter reads it.
    /// </summary>
    Promoted,

    /// <summary>
    ///     A root: one struct, one typedef or enum field read standalone (no composite places it), or a
    ///     <c>#define</c>. Its value is stored under the root's name in a one-member root value.
    /// </summary>
    Root,
}
