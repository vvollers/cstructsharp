namespace CStructSharp.Memory;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>Controls the observable source request shape of caller-owned array decoding.</summary>
public enum MemoryArrayReadMode
{
    /// <summary>Reads one encoded element at a time, as ordinary memory-array reads do.</summary>
    Elementwise,

    /// <summary>Reads bounded chunks, then decodes complete elements locally.</summary>
    Bulk,
}

/// <summary>Caller-owned arrays and explicitly bounded transport operations.</summary>
public sealed partial class MemorySession
{
    /// <summary>Copies a declared byte or signed-byte array directly to caller-owned raw storage.</summary>
    /// <remarks>The destination must hold exactly the declared count. Each byte is read individually. Use ReadArray with Bulk for explicit chunked transport. A failure can leave a written prefix; no unread byte is manufactured.</remarks>
    /// <param name="region">Finite root record.</param>
    /// <param name="typeId">Root type identity.</param>
    /// <param name="path">Path to a fixed one-byte integer array.</param>
    /// <param name="destination">Destination with exactly the declared element count.</param>
    /// <param name="context">Shared context, or null to create one.</param>
    /// <exception cref="ArgumentException">The path does not select a byte array or its count differs from the destination.</exception>
    public void ReadBytes(MemoryRegion region, string typeId, string path, Span<byte> destination, MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selection = this.Resolve(region, typeId, path, context);
            this.ReadBytesCore(selection, destination, context);
        }
        catch (CStructException exception)
        {
            AttachContext(exception, typeId, path, region);
            throw;
        }
    }

    /// <summary>Reads a declared one-byte integer array into its final owned raw-byte result without an intermediate numeric array.</summary>
    /// <param name="region">Finite root record.</param>
    /// <param name="typeId">Root schema identity.</param>
    /// <param name="path">Relative byte-array path.</param>
    /// <param name="context">Shared operation context; the full byte count is checked before allocation.</param>
    /// <returns>A complete owned array; missing bytes never become zero padding.</returns>
    /// <exception cref="ArgumentException">The path does not select a one-byte integer array.</exception>
    /// <exception cref="MemoryAccessException">The source is unavailable or the operation budget is exhausted.</exception>
    public byte[] ReadBytes(MemoryRegion region, string typeId, string path = "", MemoryAccessContext? context = null)
    {
        try
        {
            context ??= new MemoryAccessContext();
            MemorySelection selection = this.Resolve(region, typeId, path, context);
            this.ValidateByteArray(selection);

            if (selection.Type.Count > context.MaxTotalBytes - context.BytesRequested || selection.Type.Count >= context.MaxRequests - context.Requests)
            {
                throw new MemoryAccessException(MemoryFailure.BudgetExceeded, selection.Region.Source.Id, selection.Region.Address, selection.Type.Count, "Byte array exceeds the remaining operation budget.");
            }

            byte[] bytes = new byte[selection.Type.Count];
            this.ReadBytesCore(selection, bytes, context);
            return bytes;
        }
        catch (CStructException exception)
        {
            AttachContext(exception, typeId, path, region);
            throw;
        }
    }

    /// <summary>Copies validated raw byte storage using one source request per element and no intermediate region allocation.</summary>
    /// <param name="selection">Resolved byte-array storage.</param>
    /// <param name="destination">Exact-count caller or result storage.</param>
    /// <param name="context">Shared operation context.</param>
    private void ReadBytesCore(MemorySelection selection, Span<byte> destination, MemoryAccessContext context)
    {
        this.ValidateByteArray(selection);
        MemoryTypeDefinition array = selection.Type;
        if (array.Kind != MemoryTypeKind.Array || array.Count != destination.Length)
        {
            throw new ArgumentException("Destination must match the declared byte-array count.", nameof(destination));
        }

        context.Charge(selection.Region.Source.Id, selection.Region.Address, 0);
        if (destination.Length > context.MaxTotalBytes - context.BytesRequested || destination.Length > context.MaxRequests - context.Requests)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, selection.Region.Source.Id, selection.Region.Address, destination.Length, "Byte array exceeds the remaining operation budget.");
        }

        for (int index = 0; index < destination.Length; index++)
        {
            context.CheckNestingDepth(1);
            context.Charge(selection.Region.Source.Id, checked(selection.Region.Address + (ulong)index), 0);
            if (!selection.Region.TryReadExactlyAt(index, destination.Slice(index, 1), context, out MemoryReadFailure? failure, out _))
            {
                throw failure!.ToException();
            }
        }
    }

    /// <summary>Rejects non-byte array selections before result allocation, transport or budget mutation.</summary>
    /// <param name="selection">Resolved selection to validate.</param>
    /// <exception cref="ArgumentException">The selection is not a fixed one-byte integer array.</exception>
    private void ValidateByteArray(MemorySelection selection)
    {
        if (selection.Type.Kind == MemoryTypeKind.Array)
        {
            MemoryTypeDefinition element = this.Schema.GetType(selection.Type.ElementTypeId!);
            if (element.Kind == MemoryTypeKind.Scalar && element.Size == 1 && this.Schema.GetCodec(element).GetPrimitiveKind() is PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Int8)
            {
                return;
            }
        }

        throw new ArgumentException("Selection must be an array of one-byte integers.", "path");
    }

    /// <summary>Decodes a caller-counted numeric or stored-pointer array within an explicit permitted storage extent.</summary>
    /// <typeparam name="T">Exact numeric codec type; use ulong for pointers of any supported width, preserving all stored bits.</typeparam>
    /// <param name="storage">Explicitly permitted array extent, independent of a fixed-size root header. No read may exceed it.</param>
    /// <param name="elementTypeId">Schema identity of one scalar or pointer element.</param>
    /// <param name="destination">Caller-owned destination; its element count determines the read count.</param>
    /// <param name="context">Shared budget and cancellation.</param>
    /// <param name="mode">Elementwise by default; Bulk explicitly changes requests and failure boundaries.</param>
    /// <param name="chunkBytes">Maximum bulk request bytes, 1 to 65536 and at least one complete element.</param>
    /// <remarks>On an unavailable-memory outcome every complete element of the confirmed copied prefix is committed, even within the final partial chunk. Unread elements remain untouched; an incomplete element's staged bytes are discarded. Custom codecs and enums use the ordinary API. Counts and total extent are validated before transport or staging allocation.</remarks>
    public void ReadArray<T>(MemoryRegion storage, string elementTypeId, Span<T> destination, MemoryAccessContext? context = null, MemoryArrayReadMode mode = MemoryArrayReadMode.Elementwise, int chunkBytes = 4096)
        where T : unmanaged
    {
        if (!this.TryReadArray(storage, elementTypeId, destination, out _, out MemoryReadFailure? failure, context, mode, chunkBytes))
        {
            throw failure!.ToException();
        }
    }

    /// <summary>Decodes a bounded caller-counted array, reporting completed elements and expected memory failure as data.</summary>
    /// <typeparam name="T">Exact primitive result type, or ulong for stored pointers.</typeparam>
    /// <param name="storage">Finite permitted byte extent; a flexible-array count never extends this region.</param>
    /// <param name="elementTypeId">Scalar or pointer element identity.</param>
    /// <param name="destination">Caller-owned values; unread elements are untouched.</param>
    /// <param name="elementsWritten">Complete elements committed, including the confirmed prefix of a failed chunk.</param>
    /// <param name="failure">Memory failure on false, null on success.</param>
    /// <param name="context">Operation budget; exhaustion and cancellation propagate.</param>
    /// <param name="mode">Explicit transport shape.</param>
    /// <param name="chunkBytes">Bulk staging/request bound in bytes, at most 65536.</param>
    /// <returns>Whether every element was decoded.</returns>
    /// <exception cref="ArgumentException">Element type, destination extent or mode is invalid.</exception>
    /// <exception cref="MemoryAccessException">The remaining operation budget is insufficient.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public bool TryReadArray<T>(MemoryRegion storage, string elementTypeId, Span<T> destination, out int elementsWritten, out MemoryReadFailure? failure, MemoryAccessContext? context = null, MemoryArrayReadMode mode = MemoryArrayReadMode.Elementwise, int chunkBytes = 4096)
        where T : unmanaged
    {
        try
        {
            return this.TryReadArrayCore(storage, elementTypeId, destination, out elementsWritten, out failure, context, mode, chunkBytes);
        }
        catch (CStructException exception)
        {
            AttachContext(exception, elementTypeId, string.Empty, storage);
            throw;
        }
    }

    /// <summary>Validates and decodes complete staged array elements under the public operation's diagnostic boundary.</summary>
    /// <typeparam name="T">Exact numeric destination type.</typeparam>
    /// <param name="storage">Explicit permitted byte extent.</param>
    /// <param name="elementTypeId">Scalar or pointer identity.</param>
    /// <param name="destination">Caller-owned element storage.</param>
    /// <param name="elementsWritten">Confirmed complete element prefix.</param>
    /// <param name="failure">Expected source failure.</param>
    /// <param name="context">Shared operation context.</param>
    /// <param name="mode">Explicit transport mode.</param>
    /// <param name="chunkBytes">Maximum bulk staging extent.</param>
    /// <returns>Whether every destination element was filled.</returns>
    private bool TryReadArrayCore<T>(MemoryRegion storage, string elementTypeId, Span<T> destination, out int elementsWritten, out MemoryReadFailure? failure, MemoryAccessContext? context, MemoryArrayReadMode mode, int chunkBytes)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkBytes, 65536);
        if (mode is not (MemoryArrayReadMode.Elementwise or MemoryArrayReadMode.Bulk))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        MemoryTypeDefinition element = this.Schema.GetType(elementTypeId);
        if (element.Kind is not (MemoryTypeKind.Scalar or MemoryTypeKind.Pointer))
        {
            throw new ArgumentException("Array elements must be numeric scalars or stored pointers.", nameof(elementTypeId));
        }

        MemoryScalarCodec codec = this.Schema.GetCodec(element);
        bool pointer = element.Kind == MemoryTypeKind.Pointer;
        if (pointer ? typeof(T) != typeof(ulong) : codec.GetPrimitiveKind() == PrimitiveCodecKind.None || codec.ResultType(false) != typeof(T))
        {
            throw new ArgumentException("Destination type must match a built-in numeric codec, or ulong for stored pointers.", nameof(destination));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(chunkBytes, element.Size);
        long totalBytes = checked((long)destination.Length * element.Size);
        if (totalBytes > storage.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Count times element stride exceeds the permitted storage extent.");
        }

        context ??= new MemoryAccessContext();
        context.CheckNestingDepth(0);
        if (totalBytes > context.MaxTotalBytes - context.BytesRequested)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, storage.Source.Id, storage.Address, element.Size, "Array exceeds remaining byte budget.");
        }

        elementsWritten = 0;
        failure = null;
        context.Charge(storage.Source.Id, storage.Address, 0);
        int chunkElements = mode == MemoryArrayReadMode.Elementwise ? 1 : chunkBytes / element.Size;
        int capacity = Math.Min(destination.Length, chunkElements) * element.Size;
        byte[]? rented = null;
        Span<byte> bytes = capacity <= 1024 ? stackalloc byte[capacity] : (rented = ArrayPool<byte>.Shared.Rent(capacity)).AsSpan(0, capacity);
        try
        {
            while (elementsWritten < destination.Length)
            {
                context.CheckNestingDepth(1);
                int count = Math.Min(chunkElements, destination.Length - elementsWritten);
                long offset = (long)elementsWritten * element.Size;
                context.Charge(storage.Source.Id, checked(storage.Address + (ulong)offset), 0);
                bytes.Clear();
                bool complete = storage.TryReadExactlyAt(offset, bytes[..(count * element.Size)], context, out failure, out int bytesRead);
                int available = bytesRead / element.Size;

                for (int index = 0; index < available; index++)
                {
                    ReadOnlySpan<byte> encoded = bytes.Slice(index * element.Size, element.Size);
                    destination[elementsWritten + index] = pointer
                        ? (T)(object)codec.DecodePointer(encoded)
                        : codec.DecodeTyped<T>(encoded, false);
                }

                elementsWritten += available;
                if (!complete)
                {
                    failure = failure! with { Path = failure.Path ?? elementTypeId, LogicalRegion = failure.LogicalRegion ?? storage, };
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
