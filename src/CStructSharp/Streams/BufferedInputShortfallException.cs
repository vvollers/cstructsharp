namespace CStructSharp.Streams;

using System;
using CStructSharp.Diagnostics;

/// <summary>
///     The internal signal a reader over buffered input raises when it needs a byte past the buffer while the input
///     continues (<see cref="BufferedInput"/>). It is deliberately not a <see cref="CStructException"/>: the engine's
///     handlers that attach member context and the non-throwing <c>Try*</c> forms catch only those, so the signal
///     always reaches the buffered form that reads more input and runs the operation again. It never leaves the library.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1194:Implement exception constructors", Justification = "An internal control-flow signal that always carries the needed length and is always caught by a buffered form.")]
internal sealed class BufferedInputShortfallException : Exception
{
    /// <summary>Creates the signal.</summary>
    /// <param name="neededLength">The buffered length in bytes the reader needs; <see cref="long.MaxValue"/> for the whole input.</param>
    public BufferedInputShortfallException(long neededLength)
        : base("The read needs more of the buffered input.")
    {
        this.NeededLength = neededLength;
    }

    /// <summary>Gets the buffered length in bytes, from the buffer's first byte, the reader needs; <see cref="long.MaxValue"/> for the whole input.</summary>
    public long NeededLength { get; }
}
