namespace CStructSharp.Compilation;

/// <summary>
///     The placement arithmetic of one composite's fields, in declaration order: where each field starts (aligned to its
///     type when the layout is aligned), how bitfields share storage units under the layout's packing rule, and where
///     the composite ends. Every placer uses it - layout compilation, the runtime reader, writer and path resolver, and
///     generated code - so they agree byte for byte.
/// </summary>
/// <remarks>
///     The position can be unknown (<see langword="null"/>): compilation places fields before any data exists, and a
///     field after a runtime-sized one has no build-time offset. Placing anything from an unknown position keeps it
///     unknown; the runtime always starts from a known position.
/// </remarks>
internal struct PlacementCursor
{
    // The position and whether it is known are two fields rather than a long?, the booleans side by side: the runtime
    // cursor is allocated per composite instance, and a struct keeps its declared field order.
    private readonly long start;
    private bool aligned;
    private bool known;
    private long position;
    private BitfieldPlacement bitfields;

    /// <summary>Starts placing a composite at <paramref name="start"/>.</summary>
    /// <param name="start">The composite's first byte; compilation places from 0.</param>
    /// <param name="aligned">Whether the layout applies the portable alignment rules.</param>
    /// <param name="packing">The bitfield storage-sharing rule.</param>
    /// <param name="highBitFirst">Whether the first bitfield occupies the high end of its storage unit.</param>
    public PlacementCursor(long start, bool aligned, BitfieldPacking packing, bool highBitFirst)
    {
        this.start = start;
        this.Current = start;
        this.aligned = aligned;
        this.bitfields = new BitfieldPlacement(packing, aligned, highBitFirst);
    }

    /// <summary>Gets the position the next field starts from (before its own alignment), or <see langword="null"/> when unknown.</summary>
    public long? Current
    {
        readonly get => this.known ? this.position : null;
        private set
        {
            this.known = value.HasValue;
            this.position = value.GetValueOrDefault();
        }
    }

    /// <summary>A union's extent: its largest member, padded to the union's alignment when the layout is aligned (every member starts at 0).</summary>
    /// <param name="largestMember">The largest member's storage size in bytes.</param>
    /// <param name="unionAlignment">The union's alignment.</param>
    /// <param name="aligned">Whether the layout applies the portable alignment rules.</param>
    /// <returns>The union's size in bytes.</returns>
    public static long UnionEnd(long largestMember, int unionAlignment, bool aligned)
        => aligned ? LayoutMath.AlignUp(largestMember, unionAlignment) : largestMember;

    /// <summary>Checks a field's <c>@N</c> offset assertion, which counts from the composite's first byte.</summary>
    /// <param name="fieldStart">Where the field was placed, in the cursor's coordinates.</param>
    /// <param name="asserted">The asserted offset in bytes.</param>
    /// <param name="field">The field name, for the diagnostic.</param>
    /// <returns><see langword="null"/> when the assertion holds; otherwise the failure message.</returns>
    public readonly string? CheckAssertedOffset(long fieldStart, int asserted, string field)
        => OffsetAssertion.Check(field, asserted, fieldStart - this.start);

    /// <summary>Places an ordinary field: closes any bitfield run and aligns when the layout is aligned.</summary>
    /// <param name="alignment">The field type's alignment in bytes.</param>
    /// <returns>The field's start, or <see langword="null"/> when the position is unknown.</returns>
    public long? AdvanceToField(int alignment)
    {
        // The two position fields are used directly: the runtime reader and writer place every field through here, and
        // a long? round trip per call is measurable there.
        this.bitfields.Close();
        if (!this.known)
        {
            return null;
        }

        if (this.aligned)
        {
            this.position = LayoutMath.AlignUp(this.position, alignment);
        }

        return this.position;
    }

    /// <summary>Places a bitfield in the open run, or opens a new storage unit when the packing rule requires one.</summary>
    /// <param name="declaredSize">The declared storage type's size in bytes.</param>
    /// <param name="alignment">The declared storage type's alignment.</param>
    /// <param name="width">The bitfield's width in bits.</param>
    /// <param name="runBits">The compiled bit length of the run the field belongs to.</param>
    /// <param name="littleEndian">Whether the storage unit is little-endian.</param>
    /// <param name="member">The field name, for the diagnostics.</param>
    /// <returns>The unit's start and size and the field's bit offset, or <see langword="null"/> when the position is unknown.</returns>
    public (long UnitStart, int UnitSize, int BitOffset)? AdvanceToBitfield(int declaredSize, int alignment, int width, int runBits, bool littleEndian, string member)
    {
        if (!this.known)
        {
            return null;
        }

        (long unitStart, int unitSize, int bitOffset) = this.bitfields.Place(this.position, declaredSize, alignment, width, runBits, littleEndian, member);
        this.position = this.bitfields.RunEnd;
        return (unitStart, unitSize, bitOffset);
    }

    /// <summary>Applies a <c>: 0</c> separator: the next bitfield opens a new storage unit.</summary>
    /// <param name="declaredSize">The separator's declared storage size.</param>
    /// <param name="alignment">The separator's declared alignment.</param>
    /// <param name="runBits">The compiled bit length of the run.</param>
    /// <returns>The position after the separator, or <see langword="null"/> when unknown.</returns>
    public long? AdvanceToSeparator(int declaredSize, int alignment, int runBits)
    {
        if (!this.known)
        {
            return null;
        }

        this.bitfields.PlaceSeparator(this.position, declaredSize, alignment, runBits);
        this.position = this.bitfields.RunEnd;
        return this.position;
    }

    /// <summary>Records where an ordinary field ended, so the next field is placed after it.</summary>
    /// <param name="fieldEnd">The position after the field, or <see langword="null"/> when its size is not known.</param>
    public void CompleteField(long? fieldEnd)
    {
        this.Current = fieldEnd;
    }

    /// <summary>Records where an ordinary field of known size ended, so the next field is placed after it.</summary>
    /// <param name="fieldEnd">The position after the field.</param>
    public void CompleteField(long fieldEnd)
    {
        this.known = true;
        this.position = fieldEnd;
    }

    /// <summary>The composite's end: its current extent, padded to its alignment when the layout is aligned.</summary>
    /// <param name="compositeAlignment">The composite's alignment.</param>
    /// <returns>The position after the composite, or <see langword="null"/> when unknown.</returns>
    public readonly long? Finish(int compositeAlignment)
        => !this.known ? null : this.aligned ? LayoutMath.AlignUp(this.position, compositeAlignment) : this.position;
}
