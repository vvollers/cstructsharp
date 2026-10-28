namespace CStructSharp.Memory;

/// <summary>Coverage of a bounded sequential operation, without retained payload storage.</summary>
/// <param name="Stop">Explicit completion reason.</param>
/// <param name="ReadableBytes">Newly copied known bytes, excluding reused overlap.</param>
/// <param name="UnavailableBytes">Bytes skipped at the caller's explicit unavailable-stride granularity.</param>
/// <param name="Windows">Callbacks issued.</param>
public readonly record struct MemoryScanResult(MemoryScanStop Stop, long ReadableBytes, long UnavailableBytes, int Windows);
