namespace CStructSharp.Memory;

/// <summary>An in-memory writable image: a private copy of a byte array whose addresses are zero-based indexes.</summary>
/// <remarks>
/// <para>
/// This is the simplest source and the one most tests and examples use. It is also a reasonable representation of
/// a small capture that has been loaded entirely into memory. Its addresses are indexes into the owned array; to
/// present those bytes at process addresses, put a <see cref="MappedMemorySource"/> in front of it.
/// </para>
/// <para>
/// The constructor copies its input and <see cref="ToArray"/> returns a copy, so no caller ever holds a reference
/// to the internal buffer; that is what makes the generation counter trustworthy, because every mutation goes
/// through <see cref="Write"/>. Each read or write holds a lock, but two consecutive operations are not one atomic
/// snapshot. The array has a fixed length: a write cannot extend it, and a read past the end returns fewer bytes.
/// </para>
/// </remarks>
public sealed class ByteArrayMemorySource : IWritableMemorySource
{
    private readonly byte[] bytes;
    private readonly object gate = new();
    private long generation;

    /// <summary>Copies the supplied bytes into a new independent image.</summary>
    /// <param name="id">Diagnostic label; source objects, not labels, distinguish address spaces.</param>
    /// <param name="bytes">Initial contents, copied so later changes to the caller's buffer do not affect the image.</param>
    public ByteArrayMemorySource(string id, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        this.Id = id;
        this.bytes = bytes.ToArray();
    }

    /// <summary>Gets the diagnostic label for this source.</summary>
    public string Id { get; }

    /// <summary>Gets the mutation counter: zero at construction, incremented after each successful write.</summary>
    public long Generation => Interlocked.Read(ref this.generation);

    /// <summary>Gets the size of the image in bytes.</summary>
    public int Length => this.bytes.Length;

    /// <inheritdoc/>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        context.Charge(this.Id, address, destination.Length);
        lock (this.gate)
        {
            // Past the end there is nothing to copy; zero lets a finite view report MissingBytes with the address.
            if (address >= (ulong)this.bytes.Length)
            {
                return 0;
            }

            int count = Math.Min(destination.Length, this.bytes.Length - (int)address);
            this.bytes.AsSpan((int)address, count).CopyTo(destination);
            return count;
        }
    }

    /// <inheritdoc/>
    public void Write(ulong address, ReadOnlySpan<byte> bytes, MemoryAccessContext context)
    {
        context.Charge(this.Id, address, bytes.Length);
        lock (this.gate)
        {
            // Unlike a read, a write must fit completely; a partial write would leave the image in a mixed state.
            if (address > (ulong)this.bytes.Length || (ulong)bytes.Length > (ulong)this.bytes.Length - address)
            {
                throw new MemoryAccessException(MemoryFailure.Unmapped, this.Id, address, bytes.Length, "Write exceeds the image.");
            }

            bytes.CopyTo(this.bytes.AsSpan((int)address));
            this.generation++;
        }
    }

    /// <summary>Returns a snapshot of the current bytes; later writes do not affect the returned array.</summary>
    /// <returns>An independent copy of the image.</returns>
    public byte[] ToArray()
    {
        lock (this.gate)
        {
            return (byte[])this.bytes.Clone();
        }
    }
}
