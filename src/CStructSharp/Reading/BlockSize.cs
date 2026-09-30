namespace CStructSharp.Reading;

/// <summary>The block size bulk reads and writes use to bound one pooled buffer, one request, or one static plan.</summary>
internal static class BlockSize
{
    /// <summary>The largest number of bytes one bulk read or write requests, buffers or plans at a time: 64 KiB.</summary>
    public const int Bytes = 64 * 1024;
}
