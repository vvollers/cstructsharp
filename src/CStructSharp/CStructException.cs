namespace CStructSharp;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are intentionally unsupported.
/// <summary>
/// Base class for expected layout, path, binary-read, and binary-write failures reported by CStructSharp.
/// </summary>
public abstract class CStructException : Exception
{
    /// <summary>Creates one categorized failure and optionally retains its lower-level cause.</summary>
    /// <param name="code">The stable machine-readable failure category.</param>
    /// <param name="message">The optional caller-facing diagnostic.</param>
    /// <param name="innerException">The optional lower-level failure that caused this error.</param>
    protected CStructException(CStructErrorCode code, string? message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure category.</summary>
    public CStructErrorCode Code { get; }

    /// <summary>Gets the zero-based stream offset associated with the failure, when it is safe and known.</summary>
    public long? Offset { get; private set; }

    /// <summary>Gets the normalized semantic path associated with the failure, when it is safe and known.</summary>
    public string? Path { get; private set; }

    /// <summary>Adds operation context without replacing more precise context already supplied by a lower layer.</summary>
    internal void AttachContext(string? path = null, long? offset = null)
    {
        this.Path ??= path;
        this.Offset ??= offset;
    }
}
#pragma warning restore RCS1194
