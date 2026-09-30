namespace CStructSharp;

/// <summary>
///     Which fast paths one read or write may take in front of the compiled engine. Every path produces the same values,
///     bytes and failures; tests restrict the paths through the internal <c>ExecutionPath</c> option of
///     <see cref="ReadOptions"/> and <see cref="WriteOptions"/> to compare a fast path with the engine.
/// </summary>
internal enum ExecutionPath
{
    /// <summary>Every path: direct span access for whole fixed roots, then the engine with its static plans and block reads and writes.</summary>
    Fastest = 0,

    /// <summary>No direct span access for whole fixed roots; the engine still uses the static plans and block paths.</summary>
    NoDirectAccess = 1,

    /// <summary>The engine member by member only: no direct access, static plans, or block paths.</summary>
    GeneralOnly = 2,
}
