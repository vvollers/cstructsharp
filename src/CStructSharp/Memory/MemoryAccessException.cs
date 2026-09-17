#pragma warning disable RCS1194 // Structured failures require address or commit context; context-free constructors are intentionally absent.
namespace CStructSharp.Memory;

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
/// The exception carries two coordinate systems on purpose. <see cref="SourceId"/>, <see cref="Address"/>, and
/// <see cref="Length"/> identify the fragment that actually failed, which may be a file offset several mapping
/// layers below the caller's request. <see cref="Path"/> and <see cref="LogicalRegion"/> are filled in when the
/// failure crosses a <see cref="MemorySession"/> call and identify what the caller asked for. Show both in a
/// diagnostic; never present a backing offset as if it were the process address.
/// </para>
/// <para>
/// Invalid schema arguments, malformed paths, and cancellation use other exception types; this class is for
/// operations that were well-formed but could not be completed against the addressed memory.
/// </para>
/// </remarks>
public sealed class MemoryAccessException : Exception
{
    /// <summary>Creates a structured failure, formatting the source label, address, and length into the message.</summary>
    /// <param name="failure">Stable failure category.</param>
    /// <param name="sourceId">Label of the source in which the failure occurred.</param>
    /// <param name="address">Unsigned address of the failing fragment in that source.</param>
    /// <param name="length">Byte length of the failing request.</param>
    /// <param name="message">Human-readable detail appended to the coordinates.</param>
    /// <param name="inner">Underlying transport or commit exception, if any.</param>
    public MemoryAccessException(MemoryFailure failure, string sourceId, ulong address, int length, string message, Exception? inner = null)
        : base($"{sourceId}:0x{address:x} ({length} bytes): {message}", inner)
    {
        this.Failure = failure;
        this.SourceId = sourceId;
        this.Address = address;
        this.Length = length;
    }

    /// <summary>Gets the stable failure category for programmatic handling.</summary>
    public MemoryFailure Failure { get; }

    /// <summary>Gets the label of the source in which the failure occurred.</summary>
    public string SourceId { get; }

    /// <summary>Gets the unsigned address of the failing fragment, in <see cref="SourceId"/>'s coordinate system.</summary>
    public ulong Address { get; }

    /// <summary>Gets the byte length of the failing request.</summary>
    public int Length { get; }

    /// <summary>Gets the requested type and member path, set when the failure crossed a session operation; otherwise null.</summary>
    public string? Path { get; internal set; }

    /// <summary>Gets the caller's logical root region, set when the failure crossed a session operation; otherwise null.</summary>
    public MemoryRegion? LogicalRegion { get; internal set; }
}
