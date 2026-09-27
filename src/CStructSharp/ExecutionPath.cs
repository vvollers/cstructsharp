namespace CStructSharp;

/// <summary>
///     Which implementation paths one read or write may take. Every path produces the same values, bytes and failures;
///     tests restrict the paths through the internal <c>ExecutionPath</c> option of <see cref="ReadOptions"/> and
///     <see cref="WriteOptions"/> to compare a fast path with the general one.
/// </summary>
internal enum ExecutionPath
{
    /// <summary>Every path: direct span access, static read and write plans, and block reads.</summary>
    Fastest = 0,

    /// <summary>No direct span access for whole fixed roots; the general reader and writer still use their static plans.</summary>
    NoDirectAccess = 1,

    /// <summary>The general per-member reader and writer only: no direct access, static plans, or block paths.</summary>
    GeneralOnly = 2,
}
