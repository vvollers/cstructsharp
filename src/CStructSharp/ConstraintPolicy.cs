namespace CStructSharp;

/// <summary>
///     Chooses whether a read evaluates the <c>@expect</c> and <c>@supports</c> constraints of its layout. It is the
///     <see cref="ReadOptions.Constraints"/> and <see cref="ReadLimits.Constraints"/> setting of one read.
/// </summary>
/// <remarks>
///     <see cref="Skip"/> is for a read of a layout that is parsed at a second site which does not want its header rules (a
///     lenient scan, a forensic read of damaged data). It evaluates no condition of the read, so it also skips a condition that
///     later code may rely on (a bound checked before it is used as a shift or an index); the reader then meets the bad value
///     itself and fails, or not, as the layout without constraints would. <c>Matches</c> takes no read settings and always
///     evaluates its conditions.
/// </remarks>
public enum ConstraintPolicy
{
    /// <summary>Every constraint of the read is evaluated: a broken one throws, or is collected when <see cref="ReadOptions.ConstraintViolations"/> is set. The default.</summary>
    Enforce = 0,

    /// <summary>
    ///     No constraint of the read is evaluated and none is collected, so a value that breaks a condition is read as any other value.
    ///     It cannot be combined with <see cref="ReadOptions.ConstraintViolations"/> (that option asks for the conditions to be
    ///     evaluated): a read that sets both fails with <see cref="System.ArgumentException"/> when it starts.
    /// </summary>
    Skip = 1,
}
