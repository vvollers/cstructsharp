namespace CStructSharp.Memory;

/// <summary>A source that can also replace bytes in place. A successful write changes every requested byte; a failed one may have changed some.</summary>
/// <remarks>
/// <para>
/// Writing is a separate interface from reading so that an analyzer can hand a read-only capture to code that
/// only needs <see cref="IMemorySource"/> without accidentally offering a way to modify it. <see cref="MemoryPatch"/>
/// checks for this interface at commit time, which is why a patch can be previewed against any source but only
/// committed to a writable one.
/// </para>
/// <para>
/// <see cref="Write"/> has no partial-success return value: either the whole span was written or an exception was
/// thrown. An exception, however, may arrive after some bytes were already changed (a transport can fail halfway).
/// This is the reason <see cref="MemoryPatch.Commit"/> reports its failing fragment as uncertain and never promises
/// rollback. Implementations keep ownership of their storage and must advance <see cref="IMemorySource.Generation"/>
/// after every observable mutation so caches and staged patches can detect the change.
/// </para>
/// </remarks>
public interface IWritableMemorySource : IMemorySource
{
    /// <summary>Replaces bytes starting at <paramref name="address"/>. The range must already exist; writing never extends a source.</summary>
    /// <param name="address">Unsigned start address in this source's own coordinate system.</param>
    /// <param name="bytes">Replacement bytes; the source copies them and does not retain the span.</param>
    /// <param name="context">Shared budget and cancellation for the operation; the write charges its byte count.</param>
    /// <exception cref="MemoryAccessException">The range is outside the source, the budget is exhausted, or the source state prevents the write.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before the write.</exception>
    void Write(ulong address, ReadOnlySpan<byte> bytes, MemoryAccessContext context);
}
