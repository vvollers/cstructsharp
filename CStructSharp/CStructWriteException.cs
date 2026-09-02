namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are obsolete and intentionally unsupported.
/// <summary>Represents data that cannot be written according to the compiled binary layout.</summary>
public class CStructWriteException : CStructException
{
    /// <summary>Creates an empty write error.</summary>
    public CStructWriteException()
        : this(CStructErrorCode.WriteFailed, null)
    {
    }

    /// <summary>Creates a write error with an actionable diagnostic.</summary>
    /// <param name="message">The caller-facing diagnostic for the failed write.</param>
    public CStructWriteException(string message)
        : this(CStructErrorCode.WriteFailed, message)
    {
    }

    /// <summary>Creates a write error while preserving its lower-level cause.</summary>
    /// <param name="message">The caller-facing diagnostic for the failed write.</param>
    /// <param name="innerException">The lower-level stream, conversion, or arithmetic failure.</param>
    public CStructWriteException(string message, Exception innerException)
        : this(CStructErrorCode.WriteFailed, message, innerException)
    {
    }

    /// <summary>Creates a write or write-limit error for a more specific derived category.</summary>
    /// <param name="code">The stable write or write-limit failure category.</param>
    /// <param name="message">The optional caller-facing diagnostic.</param>
    /// <param name="innerException">The optional lower-level failure that caused this error.</param>
    protected CStructWriteException(
        CStructErrorCode code,
        string? message,
        Exception? innerException = null)
        : base(code, message, innerException)
    {
    }
}
#pragma warning restore RCS1194
