namespace CStructSharp.Memory;

/// <summary>The checks every caller of <see cref="IMemorySource.Read"/> applies to the count a source returns.</summary>
/// <remarks>
/// <para>
/// A source may return fewer bytes than requested (a short read), but never a negative count or more than the
/// destination holds; either breaks the <see cref="IMemorySource"/> contract and is a
/// <see cref="MemoryFailure.SourceFailure"/>. A count of zero is the source saying the bytes do not exist. Whether
/// that is an error depends on the caller: inside a finite region or mapping it means the capture is missing data
/// (<see cref="MemoryFailure.MissingBytes"/>), while an adapter that only forwards a read passes the zero on.
/// </para>
/// <para>
/// The exceptions name the coordinates the caller reports: its own source ID and the address and byte count of the
/// read that failed, which is not always the backing source's view of the same request.
/// </para>
/// </remarks>
internal static class MemorySourceChecks
{
    /// <summary>Throws <see cref="MemoryFailure.SourceFailure"/> when a source returned a negative count or more bytes than requested.</summary>
    /// <param name="read">The count the source returned.</param>
    /// <param name="requested">The number of bytes the destination could hold.</param>
    /// <param name="sourceId">The source ID the failure names.</param>
    /// <param name="address">The address the failed read started at.</param>
    /// <exception cref="MemoryAccessException">The count is outside zero to <paramref name="requested"/>.</exception>
    internal static void ThrowIfInvalidReadCount(int read, int requested, string sourceId, ulong address)
    {
        if (read < 0 || read > requested)
        {
            throw new MemoryAccessException(MemoryFailure.SourceFailure, sourceId, address, requested, "Backing source returned an invalid read count.");
        }
    }

    /// <summary>Throws <see cref="MemoryFailure.MissingBytes"/> when a source returned zero bytes where bytes were required.</summary>
    /// <param name="read">The count the source returned, already checked by <see cref="ThrowIfInvalidReadCount"/>.</param>
    /// <param name="requested">The number of bytes the read asked for.</param>
    /// <param name="sourceId">The source ID the failure names.</param>
    /// <param name="address">The address the failed read started at.</param>
    /// <param name="message">The failure message; mapped reads name the mapping layer in it.</param>
    /// <exception cref="MemoryAccessException"><paramref name="read"/> is zero.</exception>
    internal static void ThrowIfUnavailable(int read, int requested, string sourceId, ulong address, string message = "Backing bytes are unavailable.")
    {
        if (read == 0)
        {
            throw new MemoryAccessException(MemoryFailure.MissingBytes, sourceId, address, requested, message);
        }
    }
}
