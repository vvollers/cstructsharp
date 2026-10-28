namespace CStructSharp.Memory;

/// <summary>Prepared raw storage reader for a fixed array of one-byte scalars, independent of text interpretation.</summary>
/// <remarks>Signed and unsigned one-byte storage is returned bit-for-bit. Raw reads deliberately do not invoke element codecs. Decode text separately with an explicit encoding, termination and invalid-byte policy. No bytes or contexts are retained.</remarks>
public sealed class MemoryByteReader
{
    /// <summary>Stores a validated raw byte-array selection.</summary>
    /// <param name="selection">Array path whose elements occupy one byte.</param>
    internal MemoryByteReader(MemoryPath selection)
    {
        if (selection.Type.Kind != MemoryTypeKind.Array || selection.Schema.GetType(selection.Type.ElementTypeId!).Kind != MemoryTypeKind.Scalar
            || selection.Schema.GetType(selection.Type.ElementTypeId!).Size != 1)
        {
            throw new ArgumentException("A raw byte reader requires an array of one-byte scalars.", nameof(selection));
        }

        this.Selection = selection;
    }

    /// <summary>Gets the prepared schema path and its required byte extent.</summary>
    public MemoryPath Selection { get; }

    /// <summary>Gets the exact storage byte count, without a terminator or truncation interpretation.</summary>
    public int Length => this.Selection.Type.Size;

    /// <summary>Copies raw storage into caller-owned bytes, preserving a confirmed prefix on unavailable input.</summary>
    /// <param name="session">Session owning the identical schema instance.</param>
    /// <param name="region">Finite root record storage.</param>
    /// <param name="destination">Capacity at least Length. Bytes beyond Length remain untouched.</param>
    /// <param name="context">Shared source budget and cancellation.</param>
    /// <returns>Copied prefix and source failure, with no padding.</returns>
    /// <exception cref="ArgumentException">The schema differs or destination is too short.</exception>
    /// <exception cref="MemoryAccessException">The shared budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public MemoryReadResult Read(MemorySession session, MemoryRegion region, Span<byte> destination, MemoryAccessContext context)
    {
        if (destination.Length < this.Length)
        {
            throw new ArgumentException("Destination cannot hold the complete byte field.", nameof(destination));
        }

        if (this.Selection.Offset is { } offset)
        {
            if (!this.Selection.TryValidateFixed(session, region, context, out MemoryReadFailure? failure))
            {
                return new MemoryReadResult(0, failure);
            }

            region.TryReadExactlyAt(offset, destination[..this.Length], context, out failure, out int copied);
            return new MemoryReadResult(copied, failure);
        }

        return this.Selection.Resolve(session, region, context).Region.ReadPartial(destination[..this.Length], context);
    }

    /// <summary>Borrows raw field storage from stable caller bytes, without copies, decoding or pointer follows.</summary>
    /// <param name="source">Captured bytes beginning at the root's logical start.</param>
    /// <param name="context">Shared local decoding budget and cancellation.</param>
    /// <param name="bytes">Borrowed field bytes on success; empty on false. They cannot outlive source.</param>
    /// <param name="validity">Known-byte intervals for this exact buffer, or null for fully known input.</param>
    /// <returns>Whether the entire selected byte extent is known.</returns>
    /// <exception cref="NotSupportedException">The path follows a pointer.</exception>
    /// <exception cref="ArgumentException">Validity describes another buffer length.</exception>
    public bool TryReadBuffer(ReadOnlySpan<byte> source, MemoryAccessContext context, out ReadOnlySpan<byte> bytes, MemoryValidity? validity = null)
    {
        if (this.Selection.Offset is not { } offset)
        {
            throw new NotSupportedException("A byte buffer reader cannot follow a pointer.");
        }

        if (validity is not null && validity.Length != source.Length)
        {
            throw new ArgumentException("Validity must describe the supplied buffer.", nameof(validity));
        }

        context.Charge(null, null, this.Length);
        bytes = default;
        if (offset > source.Length || this.Length > source.Length - offset || (validity is not null && !validity.IsKnown((int)offset, this.Length)))
        {
            return false;
        }

        bytes = source.Slice((int)offset, this.Length);
        return true;
    }
}
