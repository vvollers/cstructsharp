namespace CStructSharp.Memory;

/// <summary>A caller-owned unsigned address space: the object that can answer "which bytes are at this address?".</summary>
/// <remarks>
/// <para>
/// This is the lowest layer of the memory APIs. Everything above it (regions, sessions, walkers, patches) works in
/// terms of a source and an unsigned address, and never cares whether the bytes come from an array, a file, a
/// mapping table, or a debugger connection. Implementing this interface is how an application plugs in a transport
/// the library does not know.
/// </para>
/// <para>
/// An address has meaning only together with its source. Address 0x1000 in two processes can select different
/// bytes, so the library distinguishes address spaces by object identity, not by the <see cref="Id"/> label.
/// Two sources with the same label are still two spaces; keep and reuse one source object while following addresses
/// within one space.
/// </para>
/// <para>
/// The contract an implementation must honor: return the number of bytes actually copied, which may be fewer than
/// requested (a positive short read); return zero only when no bytes are available at that address; never turn a
/// hole or a transport error into zero-filled success, because a fabricated zero is indistinguishable from a real
/// one; call <see cref="MemoryAccessContext.Charge"/> before performing a leaf read; and forward the same context
/// through any adapter so one operation keeps one budget. <see cref="Generation"/> is a change counter that lets
/// caches and patches notice that bytes changed; it is not a lock or a snapshot, so a source that cannot observe
/// its own changes must report a constant value and document that the caller keeps it stable.
/// </para>
/// </remarks>
public interface IMemorySource
{
    /// <summary>Gets a stable label for diagnostics and error messages. Object identity, not this label, distinguishes address spaces.</summary>
    string Id { get; }

    /// <summary>Gets a change counter that advances on every observable mutation, or a constant when consistency is guaranteed externally.</summary>
    long Generation { get; }

    /// <summary>Copies up to <paramref name="destination"/>.Length bytes starting at <paramref name="address"/> and returns the count copied.</summary>
    /// <remarks>Zero means the bytes are unavailable; a finite view turns that into a <see cref="MemoryFailure.MissingBytes"/>
    /// failure rather than treating it as end of file. An implementation may also throw a structured
    /// <see cref="MemoryAccessException"/>, for example <see cref="MemoryFailure.Unmapped"/> for a hole it can name.</remarks>
    /// <param name="address">Unsigned address in this source's own coordinate system (file offset, virtual address, or whatever the source defines).</param>
    /// <param name="destination">Caller-owned buffer, valid only for the duration of the call.</param>
    /// <param name="context">Shared operation budget; leaf sources charge bytes, translation adapters charge zero-byte requests.</param>
    /// <returns>The number of bytes copied, never greater than the buffer length.</returns>
    int Read(ulong address, Span<byte> destination, MemoryAccessContext context);
}
