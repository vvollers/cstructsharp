namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are intentionally unsupported.
/// <summary>Represents a write stopped by an explicit caller-configurable output or encoded-string budget.</summary>
public sealed class CStructWriteLimitException : CStructWriteException
{
    /// <summary>Creates a write-limit error with an actionable diagnostic.</summary>
    /// <param name="message">The diagnostic identifying the exceeded write budget.</param>
    public CStructWriteLimitException(string message)
        : base(CStructErrorCode.WriteLimitExceeded, message)
    {
    }

    /// <summary>Creates a write-limit error while preserving the lower-level cause.</summary>
    /// <param name="message">The diagnostic identifying the exceeded write budget.</param>
    /// <param name="innerException">The lower-level failure encountered while enforcing the budget.</param>
    public CStructWriteLimitException(string message, Exception innerException)
        : base(CStructErrorCode.WriteLimitExceeded, message, innerException)
    {
    }
}
#pragma warning restore RCS1194
