namespace CStructSharp.Memory;

/// <summary>A read-only source over a caller-owned seekable stream, typically a capture file, whose addresses are file offsets.</summary>
/// <remarks>
/// <para>
/// This adapter lets a large capture be analyzed without loading it into memory. Its addresses are positions in
/// the stream, so they must fit a signed <see cref="Stream.Position"/>; a <see cref="MappedMemorySource"/> above
/// it can still expose those bytes at addresses beyond <see cref="long.MaxValue"/>, because translation happens
/// before the stream is touched.
/// </para>
/// <para>
/// Each read seeks to the requested offset and restores the previous position in a <c>finally</c> block, so code
/// that shares the stream for other purposes sees it unchanged. The adapter's lock serializes calls made through
/// this object only; direct use of the stream by other code still needs the caller's own synchronization.
/// </para>
/// <para>
/// The adapter cannot observe changes made to the file by other programs, so <see cref="Generation"/> is a constant
/// supplied at construction rather than a real change counter. Keep the file stable while analyzing or caching it,
/// and dispose the stream yourself after every non-owning view has finished with it.
/// </para>
/// </remarks>
public sealed class StreamMemorySource : IMemorySource
{
    private readonly Stream stream;
    private readonly object gate = new();

    /// <summary>Wraps a readable, seekable stream without taking ownership of it.</summary>
    /// <param name="id">Diagnostic label; source objects, not labels, distinguish address spaces.</param>
    /// <param name="stream">Caller-owned stream that supports reading and seeking.</param>
    /// <param name="generation">Constant snapshot label reported as the generation; the adapter cannot detect external changes.</param>
    public StreamMemorySource(string id, Stream stream, long generation = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Memory streams must be readable and seekable.", nameof(stream));
        }

        this.Id = id;
        this.stream = stream;
        this.Generation = generation;
    }

    /// <summary>Gets the diagnostic label for this source.</summary>
    public string Id { get; }

    /// <summary>Gets the constant snapshot label supplied at construction; external file changes are not detected.</summary>
    public long Generation { get; }

    /// <inheritdoc/>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        context.Charge(this.Id, address, destination.Length);

        // A file offset above long.MaxValue cannot be a stream position; report it rather than let the cast wrap.
        if (address > long.MaxValue)
        {
            throw new MemoryAccessException(MemoryFailure.Unmapped, this.Id, address, destination.Length, "Backing file offsets must fit signed stream positions.");
        }

        lock (this.gate)
        {
            long original = this.stream.Position;
            try
            {
                this.stream.Position = (long)address;
                return this.stream.Read(destination);
            }
            catch (IOException exception)
            {
                throw new MemoryAccessException(MemoryFailure.SourceFailure, this.Id, address, destination.Length, "Backing stream read failed.", exception);
            }
            finally
            {
                this.stream.Position = original;
            }
        }
    }
}
