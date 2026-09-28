#pragma warning disable RCS1194 // Structured failures require a category and coordinates; context-free constructors are intentionally absent.
namespace CStructSharp.Memory;

using CStructSharp.Diagnostics;

/// <summary>A failure of an addressed memory operation, carrying a stable category and the coordinates of the fragment that failed.</summary>
/// <remarks>
/// <para>
/// Reading captured memory fails for reasons a caller needs to tell apart: a page was never captured, a file was
/// truncated, a budget ran out, a source changed under a prepared patch. <see cref="Failure"/> is the stable
/// category for programmatic handling; <see cref="Exception.Message"/> adds detail and
/// <see cref="Exception.InnerException"/> can retain a transport cause. None of these cases is ever reported as a
/// successful zero-filled read.
/// </para>
/// <para>
/// The class is a <see cref="CStructReadException"/>, so one handler can catch every CStructSharp read failure.
/// <see cref="CStructException.Code"/> is <see cref="CStructErrorCode.ReadLimitExceeded"/> for
/// <see cref="MemoryFailure.BudgetExceeded"/> and <see cref="CStructErrorCode.ReadFailed"/> for every other
/// category.
/// </para>
/// <para>
/// The exception carries two coordinate systems on purpose. <see cref="SourceId"/>, <see cref="Address"/>, and
/// <see cref="Length"/> identify the fragment that actually failed, which may be a file offset several mapping
/// layers below the caller's request. <see cref="SourceId"/> and <see cref="Address"/> are null when the failure is
/// not tied to a source address, such as a depth limit or the byte budget of serialized output.
/// <see cref="CStructException.Path"/> and <see cref="LogicalRegion"/> are filled in when the failure crosses a
/// <see cref="MemorySession"/> call and identify what the caller asked for. Show both in a diagnostic; never
/// present a backing offset as if it were the process address.
/// </para>
/// <para>
/// Invalid arguments, malformed paths (<see cref="CStructPathException"/>), unencodable values
/// (<see cref="CStructWriteException"/>), invalid schemas or metadata (<see cref="CStructLayoutException"/>), and
/// cancellation use other exception types; this class is for operations that were well-formed but could not be
/// completed against the addressed memory.
/// </para>
/// </remarks>
public sealed class MemoryAccessException : CStructReadException
{
    /// <summary>Creates a structured failure, prefixing the message with the source label, address, and length when both coordinates are known.</summary>
    /// <param name="failure">Stable failure category; <see cref="MemoryFailure.BudgetExceeded"/> selects <see cref="CStructErrorCode.ReadLimitExceeded"/>.</param>
    /// <param name="sourceId">Label of the source in which the failure occurred, or null when the failure is not tied to a source.</param>
    /// <param name="address">Unsigned address of the failing fragment in that source, or null when the failure is not tied to an address.</param>
    /// <param name="length">Byte length of the failing request.</param>
    /// <param name="message">Human-readable detail, appended to the coordinates when they are known.</param>
    /// <param name="inner">Underlying transport or commit exception, if any.</param>
    public MemoryAccessException(MemoryFailure failure, string? sourceId, ulong? address, int length, string message, Exception? inner = null)
        : base(CodeOf(failure), FormatMessage(sourceId, address, length, message), inner)
    {
        this.Failure = failure;
        this.SourceId = sourceId;
        this.Address = address;
        this.Length = length;
    }

    /// <summary>Gets the stable failure category for programmatic handling.</summary>
    public MemoryFailure Failure { get; }

    /// <summary>Gets the label of the source in which the failure occurred, or null when the failure is not tied to a source.</summary>
    public string? SourceId { get; }

    /// <summary>Gets the unsigned address of the failing fragment, in <see cref="SourceId"/>'s coordinate system, or null when the failure is not tied to an address.</summary>
    public ulong? Address { get; }

    /// <summary>Gets the byte length of the failing request.</summary>
    public int Length { get; }

    /// <summary>Gets the caller's logical root region, set when the failure crossed a session operation; otherwise null.</summary>
    public MemoryRegion? LogicalRegion { get; internal set; }

    /// <summary>Maps a memory failure category to the core error code: a budget failure is a read limit, everything else a failed read.</summary>
    /// <param name="failure">The memory failure category.</param>
    /// <returns><see cref="CStructErrorCode.ReadLimitExceeded"/> for <see cref="MemoryFailure.BudgetExceeded"/>; otherwise <see cref="CStructErrorCode.ReadFailed"/>.</returns>
    private static CStructErrorCode CodeOf(MemoryFailure failure)
        => failure == MemoryFailure.BudgetExceeded ? CStructErrorCode.ReadLimitExceeded : CStructErrorCode.ReadFailed;

    /// <summary>Prefixes the detail with <c>source:0xaddress (length bytes): </c> when both coordinates are known.</summary>
    /// <param name="sourceId">Label of the failing source, or null.</param>
    /// <param name="address">Address of the failing fragment, or null.</param>
    /// <param name="length">Byte length of the failing request.</param>
    /// <param name="message">The human-readable detail.</param>
    /// <returns>The message with or without the coordinate prefix.</returns>
    private static string FormatMessage(string? sourceId, ulong? address, int length, string message)
        => sourceId is not null && address is { } known ? $"{sourceId}:0x{known:x} ({length} bytes): {message}" : message;
}
