namespace CStructSharp.Reading;

/// <summary>The block size bulk reads use to bound one pooled buffer or one read request.</summary>
internal static class ReadBlock
{
    /// <summary>The largest number of bytes one bulk read requests or buffers at a time: 64 KiB.</summary>
    public const int Size = 64 * 1024;
}
