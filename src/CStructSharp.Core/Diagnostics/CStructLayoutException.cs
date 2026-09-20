namespace CStructSharp.Diagnostics;

using System;
using System.Globalization;

#pragma warning disable RCS1194 // Binary serialization constructors are intentionally unsupported.
/// <summary>Represents a layout declaration that cannot be resolved into a safe, finite binary representation.</summary>
/// <remarks>
///     When the failing declaration is known, <see cref="Line"/> and <see cref="Column"/> locate it in the layout
///     source (one-based, counted over the text handed to <see cref="CStruct"/>, prelude included) and the
///     <see cref="Message"/> ends with <c>(line L, column C)</c>. A syntax error from the parser carries its position
///     in the message text itself and leaves these properties <see langword="null"/>.
/// </remarks>
public sealed class CStructLayoutException : CStructException
{
    /// <summary>Creates an empty layout error.</summary>
    public CStructLayoutException()
        : base(CStructErrorCode.InvalidLayout, null)
    {
    }

    /// <summary>Creates a layout error with an actionable description of the invalid declaration.</summary>
    /// <param name="message">The caller-facing diagnostic for the invalid declaration.</param>
    public CStructLayoutException(string message)
        : base(CStructErrorCode.InvalidLayout, message)
    {
    }

    /// <summary>Creates a layout error with its message and the lower-level error that caused it.</summary>
    /// <param name="message">The caller-facing diagnostic for the invalid declaration.</param>
    /// <param name="innerException">The lower-level parse, validation, or arithmetic failure.</param>
    public CStructLayoutException(string message, Exception innerException)
        : base(CStructErrorCode.InvalidLayout, message, innerException)
    {
    }

    /// <summary>Gets the one-based source line of the failing declaration, when known.</summary>
    public int? Line { get; private set; }

    /// <summary>Gets the one-based source column of the failing declaration, when known.</summary>
    public int? Column { get; private set; }

    /// <inheritdoc/>
    public override string Message =>
        this.Line is { } line && this.Column is { } column
            ? FormattableString.Invariant($"{base.Message} (line {line}, column {column})")
            : base.Message;

    /// <summary>
    ///     Gets the zero-based source offset of the declaration this error is about, or -1 when unknown. The
    ///     <see cref="CStruct"/> constructor turns it into <see cref="Line"/> and <see cref="Column"/> once, where the
    ///     source text is available.
    /// </summary>
    internal int SourceOffset { get; init; } = -1;

    /// <summary>Records the source position once; a later, less precise attempt does not overwrite it.</summary>
    internal void AttachSourcePosition(int line, int column)
    {
        this.Line ??= line;
        this.Column ??= column;
    }
}
#pragma warning restore RCS1194
