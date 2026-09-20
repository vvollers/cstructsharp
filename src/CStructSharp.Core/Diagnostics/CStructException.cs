namespace CStructSharp.Diagnostics;

using System;
using System.Collections.Generic;
using System.Globalization;

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

    /// <summary>
    ///     Gets the position at which the operation stopped, when known: the absolute stream position for a stream
    ///     operation, or the zero-based offset within the supplied region for a span, memory, or array operation.
    ///     It is where the reader or writer was when the failure surfaced, which is at or after the item that
    ///     failed, not necessarily its start.
    /// </summary>
    public long? Offset { get; private set; }

    /// <summary>Gets the normalized semantic path associated with the failure, when it is safe and known.</summary>
    public string? Path { get; private set; }

    /// <summary>Gets the name of the field being read or written when the failure surfaced, when known.</summary>
    public string? Member { get; private set; }

    /// <summary>Gets the layout type of <see cref="Member"/>, when known.</summary>
    public string? MemberType { get; private set; }

    /// <summary>
    ///     Gets the diagnostic with every known fact appended: the field and its type, the requested path, and the
    ///     position at which the operation stopped, so a caller that only logs the message still sees where the
    ///     failure happened.
    /// </summary>
    public override string Message => this.Compose(base.Message);

    /// <summary>Adds operation context without replacing more precise context already supplied by a lower layer.</summary>
    internal void AttachContext(string? path = null, long? offset = null)
    {
        this.Path ??= path;
        this.Offset ??= offset;
    }

    /// <summary>
    ///     Qualifies a path recorded relative to a nested value with the path of that value, so a failure a mapper
    ///     raises through <c>StructValue.Get&lt;T&gt;("v")</c> while mapping <c>root.leaves[0]</c> reads
    ///     <c>root.leaves[0].v</c>. A missing path becomes <paramref name="prefix"/> itself.
    /// </summary>
    internal void PrefixPath(string prefix)
    {
        if (this.Path is null)
        {
            this.Path = prefix;
        }
        else if (!this.Path.StartsWith(prefix, StringComparison.Ordinal))
        {
            this.Path = prefix + "." + this.Path;
        }
    }

    /// <summary>Records the innermost field the failure belongs to; outer levels do not replace it.</summary>
    internal void AttachMember(string name, string? type)
    {
        if (this.Member is null && name.Length > 0)
        {
            this.Member = name;
            this.MemberType = type;
        }
    }

    /// <summary>
    ///     <see cref="AttachMember"/> for an exception filter: it records the field and returns
    ///     <see langword="false"/>, so the exception keeps propagating without a catch-and-rethrow at every
    ///     composite level (a filter runs before the stack unwinds and costs no rethrow).
    /// </summary>
    internal bool NoteMember(string name, string? type)
    {
        this.AttachMember(name, type);
        return false;
    }

    /// <summary>Appends the known context to <paramref name="core"/> as one parenthesized clause.</summary>
    private protected string Compose(string core)
    {
        if (this.Member is null && this.Path is null && this.Offset is null)
        {
            return core;
        }

        var parts = new List<string>(3);
        if (this.Member is not null)
        {
            parts.Add(this.MemberType is null ? $"field '{this.Member}'" : $"field '{this.Member}' ({this.MemberType})");
        }

        if (this.Path is not null)
        {
            parts.Add((this.Member is null ? "path '" : "in '") + this.Path + "'");
        }

        if (this.Offset is { } offset)
        {
            parts.Add("offset " + offset.ToString(CultureInfo.InvariantCulture));
        }

        return core.TrimEnd('.') + " (" + string.Join(", ", parts) + ").";
    }
}
#pragma warning restore RCS1194
