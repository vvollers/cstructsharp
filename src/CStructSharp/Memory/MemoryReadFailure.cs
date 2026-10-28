namespace CStructSharp.Memory;

using CStructSharp.Diagnostics;

/// <summary>Why a non-throwing schema selection or addressed read could not complete.</summary>
public enum MemoryReadFailureKind
{
    /// <summary>The schema has no type with the requested identity.</summary>
    UnknownType,

    /// <summary>No direct or promoted member matches the requested name.</summary>
    AbsentMember,

    /// <summary>Several promoted members match the same name.</summary>
    AmbiguousMember,

    /// <summary>The path cannot traverse this type or its declared array count.</summary>
    InvalidPath,

    /// <summary>The requested managed result type does not match the codec.</summary>
    WrongType,

    /// <summary>The selected codec rejected the captured scalar representation.</summary>
    InvalidValue,

    /// <summary>The pointer's target has no value layout.</summary>
    IncompleteTarget,

    /// <summary>The stored pointer has address zero.</summary>
    NullPointer,

    /// <summary>The finite target or root cannot hold the selected type.</summary>
    InsufficientExtent,

    /// <summary>The source could not supply the requested bytes; MemoryFailure preserves its category.</summary>
    UnavailableMemory,

    /// <summary>A projected composite does not include this member; consult its unselected names or import a wider schema.</summary>
    UnselectedMember,

    /// <summary>A pointer target's identity is retained but its value layout was deliberately excluded from this projection.</summary>
    UnselectedType,
}

/// <summary>An unsuccessful selection or read, with diagnostics and optional source coordinates; creating it raises no exception.</summary>
/// <param name="Kind">Schema or memory failure category.</param>
/// <param name="Message">Explanation without exception formatting.</param>
/// <param name="Path">Requested root and member path.</param>
/// <param name="SourceId">Failing source label, when known.</param>
/// <param name="Address">Failing unsigned source coordinate, when known.</param>
/// <param name="Length">Requested byte extent.</param>
/// <param name="MemoryFailure">Original transport category, when applicable.</param>
/// <param name="LogicalRegion">Caller's logical root, independent of transport coordinates.</param>
public sealed record MemoryReadFailure(MemoryReadFailureKind Kind, string Message, string? Path = null, string? SourceId = null, ulong? Address = null, int Length = 0, MemoryFailure? MemoryFailure = null, MemoryRegion? LogicalRegion = null)
{
    /// <summary>Retains a transport's existing exception so throwing callers preserve its original details and inner cause.</summary>
    internal MemoryAccessException? OriginalException { get; init; }

    /// <summary>Creates a detailed exception only when a throwing caller requests it.</summary>
    /// <returns>A path exception for schema failures, or an addressed memory exception for storage failures.</returns>
    public CStructException ToException()
    {
        CStructException exception = this.OriginalException ?? (this.Kind is MemoryReadFailureKind.NullPointer or MemoryReadFailureKind.UnavailableMemory or MemoryReadFailureKind.InsufficientExtent or MemoryReadFailureKind.InvalidValue
            ? new MemoryAccessException(this.MemoryFailure ?? Memory.MemoryFailure.InvalidValue, this.SourceId, this.Address, this.Length, this.Message) { LogicalRegion = this.LogicalRegion, }
            : (CStructException)new CStructPathException(this.Message));
        if (exception is MemoryAccessException access)
        {
            access.LogicalRegion ??= this.LogicalRegion;
        }

        exception.AttachContext(this.Path);
        return exception;
    }

    /// <summary>Preserves a source exception's coordinates when its transport uses exceptions for absence.</summary>
    /// <param name="exception">Non-budget source failure.</param>
    /// <returns>The structured failure without allocating another exception.</returns>
    internal static MemoryReadFailure FromAccess(MemoryAccessException exception) => new(MemoryReadFailureKind.UnavailableMemory, exception.Message, exception.Path, exception.SourceId, exception.Address, exception.Length, exception.Failure, exception.LogicalRegion) { OriginalException = exception, };
}
