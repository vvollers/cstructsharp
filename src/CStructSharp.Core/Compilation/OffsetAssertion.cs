namespace CStructSharp.Compilation;

using System;
using CStructSharp.Diagnostics;

/// <summary>
///     The rules of a declarator's <c>@N</c> offset assertion. <c>N</c> is a constant expression, evaluated once when the
///     layout is built. It states the field's byte offset from the start of its own struct or union, as C's
///     <c>offsetof</c> does, and never changes placement. The layout checks it when the field's offset is known at that
///     point; otherwise the placement cursor checks it where an operation places the field.
/// </summary>
internal static class OffsetAssertion
{
    /// <summary>Validates an evaluated assertion value.</summary>
    /// <param name="asserted">The evaluated <c>N</c>.</param>
    /// <param name="field">The field name, for the diagnostic.</param>
    /// <returns><paramref name="asserted"/>.</returns>
    /// <exception cref="CStructLayoutException"><paramref name="asserted"/> is negative.</exception>
    public static int Validate(int asserted, string field)
        => asserted >= 0
               ? asserted
               : throw new CStructLayoutException(FormattableString.Invariant($"Explicit offset assertion must be non-negative: {field} = {asserted}"));

    /// <summary>Compares an assertion with the field's actual offset.</summary>
    /// <param name="field">The field name, for the diagnostic.</param>
    /// <param name="asserted">The asserted offset in bytes.</param>
    /// <param name="offset">The field's offset in bytes from the start of its struct or union.</param>
    /// <returns><see langword="null"/> when the assertion holds; otherwise the failure message.</returns>
    public static string? Check(string field, int asserted, long offset)
        => offset == asserted
               ? null
               : FormattableString.Invariant($"Field '{field}' asserts offset {asserted} but computed offset is {offset}.");
}
