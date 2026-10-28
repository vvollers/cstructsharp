namespace CStructSharp.Memory;

using CStructSharp.Diagnostics;

/// <summary>A typed scalar reader whose schema decisions are prepared once and whose operation state is supplied on each read.</summary>
/// <typeparam name="T">Exact managed scalar result type, or StoredPointer for stored pointer bits.</typeparam>
public sealed class MemoryFieldReader<T>
{
    private readonly MemoryScalarCodec codec;

    /// <summary>Stores a validated schema plan and its scalar codec.</summary>
    /// <param name="selection">Immutable path belonging to one schema instance.</param>
    /// <param name="codec">Validated scalar or bit-slice codec.</param>
    internal MemoryFieldReader(MemoryPath selection, MemoryScalarCodec codec)
    {
        this.Selection = selection;
        this.codec = codec;
    }

    /// <summary>Gets the schema-only selection, including offset and field metadata.</summary>
    public MemoryPath Selection { get; }

    /// <summary>Reads one value using fresh pointer resolution and the supplied operation budget.</summary>
    /// <param name="session">Session owning the identical schema instance.</param>
    /// <param name="region">Finite root record storage.</param>
    /// <param name="context">Shared operation context, or null to create one.</param>
    /// <returns>The selected value without numeric scalar boxing.</returns>
    /// <exception cref="Diagnostics.CStructException">Selection, source, or budget failure.</exception>
    /// <exception cref="ArgumentException">The session owns a different schema.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public T Read(MemorySession session, MemoryRegion region, MemoryAccessContext? context = null)
        => this.TryRead(session, region, out T value, out MemoryReadFailure? failure, context) ? value : throw failure!.ToException();

    /// <summary>Reads one value, returning expected missing memory and pointer failures without throwing internally.</summary>
    /// <remarks>Source implementations and custom codecs may themselves throw. Cancellation and exhausted budgets always propagate. No context or captured bytes are retained.</remarks>
    /// <param name="session">Session owning the identical schema.</param>
    /// <param name="region">Caller-owned root storage.</param>
    /// <param name="value">Decoded value on success; default on false.</param>
    /// <param name="failure">Structured failure on false, null on success.</param>
    /// <param name="context">Shared operation budget; null creates one.</param>
    /// <returns>Whether selection and decoding succeeded.</returns>
    /// <exception cref="MemoryAccessException">An operation budget was exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public bool TryRead(MemorySession session, MemoryRegion region, out T value, out MemoryReadFailure? failure, MemoryAccessContext? context = null)
    {
        try
        {
            return this.TryReadCore(session, region, out value, out failure, context);
        }
        catch (CStructException exception)
        {
            exception.AttachContext(this.Selection.Path.Length == 0 ? this.Selection.RootType.Id : this.Selection.RootType.Id + "." + this.Selection.Path);
            if (exception is MemoryAccessException access)
            {
                access.LogicalRegion ??= region;
            }

            throw;
        }
    }

    /// <summary>Performs selection and scalar decoding under the throwing boundary's contextual diagnostics.</summary>
    /// <param name="session">Schema-compatible session.</param>
    /// <param name="region">Logical root.</param>
    /// <param name="value">Decoded value or default.</param>
    /// <param name="failure">Expected read failure.</param>
    /// <param name="context">Operation context.</param>
    /// <returns>Whether all storage was available and valid.</returns>
    private bool TryReadCore(MemorySession session, MemoryRegion region, out T value, out MemoryReadFailure? failure, MemoryAccessContext? context)
    {
        context ??= new MemoryAccessContext();
        value = default!;
        MemoryRegion storage = region;
        long offset = this.Selection.Offset ?? 0;
        if (this.Selection.Offset is not null)
        {
            if (!this.Selection.TryValidateFixed(session, region, context, out failure))
            {
                return false;
            }
        }
        else if (this.Selection.TryResolve(session, region, out MemorySelection? selected, out failure, context))
        {
            storage = selected!.Region;
        }
        else
        {
            return false;
        }

        context.CheckNestingDepth(0);
        ulong address = checked(storage.Address + (ulong)offset);
        context.Charge(storage.Source.Id, address, 0);
        if (this.Selection.Type.Size > context.MaxTotalBytes - context.BytesRequested)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, storage.Source.Id, address, this.Selection.Type.Size, "Scalar exceeds the remaining byte budget.");
        }

        Span<byte> bytes = this.Selection.Type.Size <= 256 ? stackalloc byte[this.Selection.Type.Size] : new byte[this.Selection.Type.Size];
        if (!storage.TryReadExactlyAt(offset, bytes, context, out failure, out _))
        {
            failure = this.Selection.WithContext(failure!, region);
            return false;
        }

        if (this.Selection.Type.Kind == MemoryTypeKind.Pointer)
        {
            value = (T)(object)new StoredPointer(this.codec.DecodePointer(bytes), this.Selection.Type.Size);
        }
        else
        {
            try
            {
                value = this.codec.DecodeTyped<T>(bytes, this.Selection.Field?.Signed == true);
            }
            catch (CStructReadException exception) when (exception.Code != CStructErrorCode.ReadLimitExceeded)
            {
                failure = this.Selection.WithContext(new(MemoryReadFailureKind.InvalidValue, exception.Message, SourceId: storage.Source.Id, Address: address, Length: this.Selection.Type.Size, MemoryFailure: MemoryFailure.InvalidValue), region);
                return false;
            }
        }

        return true;
    }
}
