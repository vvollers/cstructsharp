namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are intentionally unsupported.
/// <summary>Represents a read stopped by an explicit caller-configurable safety or work budget.</summary>
public sealed class CStructReadLimitException : CStructReadException
{
    /// <summary>Creates a read-limit error with an actionable diagnostic.</summary>
    /// <param name="message">The diagnostic identifying the exceeded read budget.</param>
    public CStructReadLimitException(string message)
        : base(CStructErrorCode.ReadLimitExceeded, message)
    {
    }

    /// <summary>Creates a read-limit error while preserving the lower-level cause.</summary>
    /// <param name="message">The diagnostic identifying the exceeded read budget.</param>
    /// <param name="innerException">The lower-level failure encountered while enforcing the budget.</param>
    public CStructReadLimitException(string message, Exception innerException)
        : base(CStructErrorCode.ReadLimitExceeded, message, innerException)
    {
    }
}
#pragma warning restore RCS1194
