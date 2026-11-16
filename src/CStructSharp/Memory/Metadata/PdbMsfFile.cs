namespace CStructSharp.Memory.Metadata;

using System.Buffers.Binary;
using CStructSharp.Diagnostics;

/// <summary>Reads selected streams from a borrowed MSF 7 file without materializing the containing file.</summary>
internal sealed class PdbMsfFile
{
    private readonly Stream input;
    private readonly long length;
    private readonly int blockSize;
    private readonly byte[] directory;
    private readonly StreamLocation[] streams;
    private readonly CancellationToken cancellationToken;
    private int remainingBytes;

    /// <summary>Validates the directory and every recorded block reference before reading metadata streams.</summary>
    /// <param name="input">Readable, seekable input; ownership stays with the caller.</param>
    /// <param name="maximumBytes">Shared limit for retained directory and stream bytes.</param>
    /// <param name="cancellationToken">Checked during directory traversal and every block read.</param>
    /// <exception cref="ArgumentException">The input cannot be read and sought.</exception>
    /// <exception cref="CStructLayoutException">The file or directory is malformed or exceeds the metadata limit.</exception>
    internal PdbMsfFile(Stream input, int maximumBytes, CancellationToken cancellationToken)
    {
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("Native PDB metadata requires a readable, seekable stream.", nameof(input));
        }

        this.input = input;
        this.length = input.Length;
        this.remainingBytes = maximumBytes;
        this.cancellationToken = cancellationToken;
        Span<byte> header = stackalloc byte[56];
        this.ReadAt(0, header);
        if (!header[..32].SequenceEqual("Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8))
        {
            throw Invalid("the MSF 7 signature is absent");
        }

        uint width = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        uint freeMap = BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
        uint blockCount = BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        uint directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(header[44..]);
        uint mapBlock = BinaryPrimitives.ReadUInt32LittleEndian(header[52..]);
        if (width is not (512 or 1024 or 2048 or 4096) || freeMap is not (1 or 2)
            || blockCount < 3 || ((long)blockCount * width) != this.length)
        {
            throw Invalid("the block size, free-map selector or declared file extent is inconsistent");
        }

        this.blockSize = (int)width;
        int directoryBlocks = this.BlocksFor(directoryLength);
        if (directoryLength < 4 || directoryBlocks > this.blockSize / 4)
        {
            throw Invalid("the stream directory does not fit its recorded block map");
        }

        this.Charge(directoryLength);
        this.directory = new byte[(int)directoryLength];
        var occupied = new HashSet<uint> { 0 };
        this.ValidateBlock(mapBlock, blockCount, occupied);
        byte[] map = new byte[checked(directoryBlocks * 4)];
        this.ReadAt((long)mapBlock * this.blockSize, map);
        int copied = 0;
        for (int index = 0; index < directoryBlocks; index++)
        {
            uint block = BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(index * 4));
            this.ValidateBlock(block, blockCount, occupied);
            int count = Math.Min(this.blockSize, this.directory.Length - copied);
            this.ReadAt((long)block * this.blockSize, this.directory.AsSpan(copied, count));
            copied += count;
        }

        uint streamCount = BinaryPrimitives.ReadUInt32LittleEndian(this.directory);
        if (streamCount > 65_536 || 4UL + (streamCount * 4UL) > (ulong)this.directory.Length)
        {
            throw Invalid("the stream-size table is truncated or exceeds its record limit");
        }

        this.Charge(streamCount * 8);
        this.streams = new StreamLocation[streamCount];
        int cursor = checked(4 + ((int)streamCount * 4));
        for (int index = 0; index < this.streams.Length; index++)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(this.directory.AsSpan(4 + (index * 4)));
            int blocks = size == uint.MaxValue ? 0 : this.BlocksFor(size);
            if ((ulong)blocks * 4 > (ulong)(this.directory.Length - cursor))
            {
                throw Invalid("a stream's block list extends beyond the directory");
            }

            this.streams[index] = new(size, cursor);
            for (int blockIndex = 0; blockIndex < blocks; blockIndex++)
            {
                uint block = BinaryPrimitives.ReadUInt32LittleEndian(this.directory.AsSpan(cursor));
                this.ValidateBlock(block, blockCount, occupied);
                cursor += 4;
            }
        }

        if (cursor != this.directory.Length)
        {
            throw Invalid("the stream directory has unclaimed bytes");
        }
    }

    /// <summary>Gets the original logical extent of one present metadata stream.</summary>
    /// <param name="index">Zero-based MSF stream index.</param>
    /// <returns>The original logical byte length.</returns>
    /// <exception cref="CStructLayoutException">The required stream is absent.</exception>
    internal uint StreamLength(int index)
    {
        if ((uint)index >= (uint)this.streams.Length || this.streams[index].Length == uint.MaxValue)
        {
            throw Invalid($"required stream {index} is absent");
        }

        return this.streams[index].Length;
    }

    /// <summary>Copies only the requested metadata stream, joining its original discontiguous blocks.</summary>
    /// <param name="index">Zero-based MSF stream index.</param>
    /// <returns>The complete original stream bytes.</returns>
    /// <exception cref="CStructLayoutException">The stream is absent, unreadable or exceeds the shared limit.</exception>
    internal byte[] ReadStream(int index)
    {
        uint length = this.StreamLength(index);
        if (length > int.MaxValue)
        {
            throw Invalid("a required metadata stream exceeds the byte budget");
        }

        return this.ReadStreamRange(index, 0, (int)length);
    }

    /// <summary>Copies a selected original extent without retaining unrelated compiland or source information.</summary>
    /// <param name="index">Zero-based MSF stream index.</param>
    /// <param name="offset">Logical byte offset within that stream.</param>
    /// <param name="count">Number of original bytes to retain.</param>
    /// <returns>The exact original extent, including fields split across physical blocks.</returns>
    /// <exception cref="CStructLayoutException">The extent is absent, truncated or exceeds the shared limit.</exception>
    internal byte[] ReadStreamRange(int index, uint offset, int count)
    {
        uint length = this.StreamLength(index);
        if (count < 0 || offset > length || (uint)count > length - offset)
        {
            throw Invalid("a required metadata extent exceeds its stream");
        }

        this.Charge((uint)count);
        byte[] bytes = count == 0 ? [] : new byte[count];
        int cursor = checked(this.streams[index].BlockOffset + ((int)(offset / (uint)this.blockSize) * 4));
        int within = (int)(offset % (uint)this.blockSize);
        for (int copied = 0; copied < bytes.Length; cursor += 4)
        {
            uint block = BinaryPrimitives.ReadUInt32LittleEndian(this.directory.AsSpan(cursor));
            int take = Math.Min(this.blockSize - within, bytes.Length - copied);
            this.ReadAt(((long)block * this.blockSize) + within, bytes.AsSpan(copied, take));
            copied += take;
            within = 0;
        }

        return bytes;
    }

    /// <summary>Names the source format on a malformed-metadata failure.</summary>
    /// <param name="reason">The violated format or budget invariant.</param>
    /// <returns>A domain-specific layout exception.</returns>
    private static CStructLayoutException Invalid(string reason) => new($"Native PDB metadata is invalid: {reason}.");

    /// <summary>Bounds retained metadata before any length-controlled allocation.</summary>
    /// <param name="bytes">Additional byte count.</param>
    /// <exception cref="CStructLayoutException">The shared metadata limit would be exceeded.</exception>
    private void Charge(uint bytes)
    {
        if (bytes > this.remainingBytes)
        {
            throw Invalid("metadata exceeds its shared byte budget");
        }

        this.remainingBytes -= (int)bytes;
    }

    /// <summary>Converts a logical stream extent to its number of physical blocks without unsigned wrap.</summary>
    /// <param name="bytes">Logical byte count.</param>
    /// <returns>The required block count.</returns>
    private int BlocksFor(uint bytes) => checked((int)(((ulong)bytes + (uint)this.blockSize - 1) / (uint)this.blockSize));

    /// <summary>Rejects directory/data aliases, reserved pages and repeated ownership.</summary>
    /// <param name="block">Original physical block index.</param>
    /// <param name="count">Declared number of blocks in the original file.</param>
    /// <param name="occupied">Previously assigned metadata and stream blocks.</param>
    /// <exception cref="CStructLayoutException">The block is reserved, outside the file or already assigned.</exception>
    private void ValidateBlock(uint block, uint count, HashSet<uint> occupied)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        uint interval = block % (uint)this.blockSize;
        if (block >= count || interval is 1 or 2 || !occupied.Add(block))
        {
            throw Invalid("a stream or directory references a reserved, repeated or out-of-file block");
        }
    }

    /// <summary>Reads a confirmed original extent and rejects short reads or a changing file length.</summary>
    /// <param name="offset">Absolute byte coordinate in the input.</param>
    /// <param name="bytes">Destination for the complete original extent.</param>
    /// <exception cref="CStructLayoutException">The extent is outside the file, changes, or cannot be read completely.</exception>
    private void ReadAt(long offset, Span<byte> bytes)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        if (offset < 0 || offset > this.length || bytes.Length > this.length - offset || this.input.Length != this.length)
        {
            throw Invalid("an original stream extent is outside the file or its length changed");
        }

        try
        {
            this.input.Position = offset;
            this.input.ReadExactly(bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructLayoutException("Native PDB metadata has a truncated original extent.", exception);
        }

        if (this.input.Length != this.length)
        {
            throw Invalid("the input length changed during a metadata read");
        }
    }

    /// <summary>A stream's declared size and its first block-index coordinate in the retained directory.</summary>
    /// <param name="Length">Original logical byte length, or uint.MaxValue for an absent stream.</param>
    /// <param name="BlockOffset">Byte offset of its block list inside the directory.</param>
    private readonly record struct StreamLocation(uint Length, int BlockOffset);
}
