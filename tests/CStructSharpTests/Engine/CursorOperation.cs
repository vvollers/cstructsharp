namespace CStructSharp.Tests;

/// <summary>
///     One step of a scripted cursor sequence (<see cref="CursorStep"/>): each maps to a call on the reference
///     <c>ReadBudgetStream</c> and to the matching <c>IReadCursor</c> member. <c>A</c> and <c>B</c> are the step's operands.
/// </summary>
internal enum CursorOperation
{
    /// <summary>Reads one fixed-width value of A bytes (1, 2, 3, 4, 6, 8 or 16).</summary>
    Fixed,

    /// <summary>Sets the position to A bytes from the data's first byte (possibly outside the input).</summary>
    Seek,

    /// <summary>Moves the position by A bytes.</summary>
    Skip,

    /// <summary>Aligns the position to B bytes, measured from A bytes after the data's first byte.</summary>
    Align,

    /// <summary>Reads a typed array of B elements of the A-th array codec.</summary>
    Array,

    /// <summary>Reads a terminated string with the A-th encoding and terminator.</summary>
    Terminated,

    /// <summary>Reads A bytes of bounded text of the B-th bounded type.</summary>
    Bounded,

    /// <summary>Asks whether the input is provably short by A bytes.</summary>
    IsShortBy,

    /// <summary>Takes A bytes from memory within the budget, or declines.</summary>
    SpanWithinBudget,

    /// <summary>Takes A bytes from a seekable stream into a block within the budget, or declines.</summary>
    BlockWithinBudget,

    /// <summary>Reads exactly A bytes, failing with the short-read text.</summary>
    Exact,

    /// <summary>Reads one required byte.</summary>
    ByteExactly,

    /// <summary>Peeks the remaining memory and consumes up to A bytes of it.</summary>
    PeekAdvance,

    /// <summary>Reports the input length.</summary>
    Length,

    /// <summary>Cancels the operation's token; later steps show which boundaries observe it.</summary>
    Cancel,

    /// <summary>Checks the token at an explicit boundary (struct entry, pointer follow, array element).</summary>
    Checkpoint,
}
