namespace CStructSharp.Memory;

/// <summary>A decoded value together with the evidence of where it came from: its logical selection and the physical ranges that supplied its bytes.</summary>
/// <remarks>
/// An inspector that shows "PID 42 at kernel address 0xffff800000000ffe, from file offsets 0x2ffe and 0x9000"
/// needs three things: the value, the logical location with its type and member metadata, and the backing
/// fragments after every mapping layer has been resolved. One logical field can span several fragments when it
/// crosses a page boundary. A custom source adapter is reported as a terminal coordinate because the library
/// cannot see how it obtains bytes. The result describes one read; it is not a snapshot and does not prevent the
/// source from changing afterwards.
/// </remarks>
/// <param name="Value">Decoded value; pointers keep their stored bits as <see cref="StoredPointer"/>.</param>
/// <param name="Selection">Logical region and metadata of the selected field.</param>
/// <param name="BackingRegions">Physical ranges in logical order, resolved through every known mapping layer.</param>
public sealed record MemoryInspection(object? Value, MemorySelection Selection, IReadOnlyList<MemoryRegion> BackingRegions);
