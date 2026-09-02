namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are not supported by modern .NET exception APIs.
/// <summary>Represents a binary-layout read that could not be completed safely.</summary>
public class CStructReadException : CStructException
{
    /// <summary>Creates an empty read error.</summary>
    public CStructReadException()
        : this(CStructErrorCode.ReadFailed, null)
    {
    }

    /// <summary>Creates a read error with a message that explains what could not be read.</summary>
    /// <param name="message">The caller-facing diagnostic for the failed read.</param>
    public CStructReadException(string message)
        : this(CStructErrorCode.ReadFailed, message)
    {
    }

    /// <summary>Creates a read error with its message and the lower-level error that caused it.</summary>
    /// <param name="message">The caller-facing diagnostic for the failed read.</param>
    /// <param name="innerException">The lower-level stream, conversion, or arithmetic failure.</param>
    public CStructReadException(string message, Exception innerException)
        : this(CStructErrorCode.ReadFailed, message, innerException)
    {
    }

    /// <summary>Creates a read or read-limit error for a more specific derived category.</summary>
    /// <param name="code">The stable read or read-limit failure category.</param>
    /// <param name="message">The optional caller-facing diagnostic.</param>
    /// <param name="innerException">The optional lower-level failure that caused this error.</param>
    protected CStructReadException(
        CStructErrorCode code,
        string? message,
        Exception? innerException = null)
        : base(code, message, innerException)
    {
    }
}
#pragma warning restore RCS1194
