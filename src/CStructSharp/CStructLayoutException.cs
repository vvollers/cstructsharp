namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are intentionally unsupported.
/// <summary>Represents a layout declaration that cannot be resolved into a safe, finite binary representation.</summary>
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
}
#pragma warning restore RCS1194
