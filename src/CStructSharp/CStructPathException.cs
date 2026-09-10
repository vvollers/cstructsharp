namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are obsolete and intentionally unsupported.
/// <summary>Represents an invalid or unresolvable public CStruct path.</summary>
public sealed class CStructPathException : CStructException
{
    /// <summary>Creates an empty path error for serializers and general exception tooling.</summary>
    public CStructPathException()
        : base(CStructErrorCode.InvalidPath, null)
    {
    }

    /// <summary>Creates a path error with a caller-facing diagnostic.</summary>
    /// <param name="message">The diagnostic describing the invalid or unresolved path.</param>
    public CStructPathException(string message)
        : base(CStructErrorCode.InvalidPath, message)
    {
    }

    /// <summary>Creates a path error while preserving the lower-level failure.</summary>
    /// <param name="message">The diagnostic describing the invalid or unresolved path.</param>
    /// <param name="innerException">The lower-level failure encountered while parsing or resolving the path.</param>
    public CStructPathException(string message, Exception innerException)
        : base(CStructErrorCode.InvalidPath, message, innerException)
    {
    }
}
#pragma warning restore RCS1194
