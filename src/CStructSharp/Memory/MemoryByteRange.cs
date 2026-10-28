namespace CStructSharp.Memory;

/// <summary>A byte interval relative to the beginning of a captured buffer.</summary>
/// <param name="Offset">Nonnegative byte offset.</param>
/// <param name="Length">Nonnegative number of bytes.</param>
public readonly record struct MemoryByteRange(int Offset, int Length);
