namespace CStructSharp.Memory;

/// <summary>Everything a pointer resolver may need to turn stored bits into a target region: the bits, where they were found, and what is expected at the target.</summary>
/// <remarks>
/// <para>
/// A resolver is the application's rule for interpreting pointers, supplied to <see cref="MemorySession"/> at
/// construction. The session calls it only when a path takes an explicit <c>.value</c> step, never merely because
/// a record contains a pointer. Common rules are: absolute address in the same space (the default), displacement
/// from the containing record (use <see cref="Container"/>), an address in another process (return a region in
/// that other source), or an address with tag bits (mask the documented bits before using them).
/// </para>
/// <para>
/// The resolver must return a region of at least <see cref="TargetSize"/> bytes; the session slices that extent and
/// continues the path. The returned source remains owned by the application, and the original
/// <see cref="StoredPointer"/> in the record is never modified.
/// </para>
/// </remarks>
/// <param name="Pointer">Stored bits and byte width, exactly as read from memory.</param>
/// <param name="Storage">Region holding the pointer bytes; its source is the address space the pointer was read from.</param>
/// <param name="Container">Region of the record that contains the pointer, for relative encodings.</param>
/// <param name="TargetTypeId">ID of the pointed-to type, or null for an untyped address.</param>
/// <param name="TargetSize">Minimum number of bytes the returned region must cover.</param>
/// <param name="Path">The full path being resolved, for diagnostics.</param>
/// <param name="Depth">Path depth at this step, counting member, index, and pointer steps.</param>
public sealed record PointerRequest(StoredPointer Pointer, MemoryRegion Storage, MemoryRegion Container, string? TargetTypeId, int TargetSize, string Path, int Depth);
