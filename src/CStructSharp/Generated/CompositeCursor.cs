namespace CStructSharp.Generated;

using CStructSharp.Compilation;

/// <summary>
///     The placement state of one composite being read or written by generated code: where the next field starts
///     (aligned to its type when the layout is aligned) and the open run of bitfields, with the runtime's packing
///     rules (<see cref="BitfieldPacking"/>, <see cref="BitfieldAllocation"/>). Generated code creates one per
///     composite instance, exactly as the runtime creates a placement cursor per composite.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can use it.
/// </remarks>
public struct CompositeCursor
{
    private readonly bool aligned;
    private BitfieldPlacement bitfields;
    private long current;

    /// <summary>Creates placement state without owning or reading any binary storage.</summary>
    /// <param name="start">The composite's byte offset in the containing read/write cursor's coordinate system.</param>
    /// <param name="aligned">Whether member placement applies alignment padding.</param>
    /// <param name="packing">The bitfield storage-sharing rule.</param>
    /// <param name="highBitFirst">Whether the first bitfield occupies the high end of its storage unit.</param>
    private CompositeCursor(long start, bool aligned, BitfieldPacking packing, bool highBitFirst)
    {
        this.current = start;
        this.aligned = aligned;
        this.bitfields = new BitfieldPlacement(packing, aligned, highBitFirst);
    }

    /// <summary>Gets the position the next field starts from (before its own alignment).</summary>
    public readonly long Current => this.current;

    /// <summary>Starts placing a composite at <paramref name="start"/>.</summary>
    /// <param name="start">The composite's first byte.</param>
    /// <param name="aligned">Whether the layout applies the portable alignment rules.</param>
    /// <param name="packing">The bitfield packing rule.</param>
    /// <param name="allocation">Which end of a storage unit bitfields are allocated from.</param>
    /// <returns>The cursor.</returns>
    public static CompositeCursor Start(long start, bool aligned, BitfieldPacking packing, BitfieldAllocation allocation)
        => new(start, aligned, packing, allocation == BitfieldAllocation.HighBitFirst);

    /// <summary>Places an ordinary field: closes any bitfield run and aligns when the layout is aligned.</summary>
    /// <param name="alignment">The field type's alignment in bytes.</param>
    /// <returns>The field's start.</returns>
    public long AdvanceToField(int alignment)
    {
        this.bitfields.Close();
        this.current = this.aligned ? LayoutMath.AlignUp(this.current, alignment) : this.current;
        return this.current;
    }

    /// <summary>Places a bitfield in the open run (or opens one) and returns its storage unit and bit offset.</summary>
    /// <param name="declaredSize">The declared storage type's size in bytes.</param>
    /// <param name="alignment">The declared storage type's alignment.</param>
    /// <param name="width">The bitfield's width in bits.</param>
    /// <param name="runBits">The compiled bit length of the run the field belongs to (SysV packing).</param>
    /// <param name="littleEndian">Whether the storage unit is little-endian.</param>
    /// <param name="member">The field name, for the diagnostics.</param>
    /// <returns>The unit's start and size, and the field's bit offset within it.</returns>
    public BitfieldSlot AdvanceToBitfield(int declaredSize, int alignment, int width, int runBits, bool littleEndian, string member)
    {
        (long unitStart, int unitSize, int bitOffset) = this.bitfields.Place(this.current, declaredSize, alignment, width, runBits, littleEndian, member);
        this.current = this.bitfields.RunEnd;
        return new BitfieldSlot(unitStart, unitSize, bitOffset);
    }

    /// <summary>Applies a <c>: 0</c> separator: the next bitfield opens a new storage unit.</summary>
    /// <param name="declaredSize">The separator's declared storage size.</param>
    /// <param name="alignment">The separator's declared alignment.</param>
    /// <param name="runBits">The compiled bit length of the run.</param>
    /// <returns>The position after the separator.</returns>
    public long AdvanceToSeparator(int declaredSize, int alignment, int runBits)
    {
        this.bitfields.PlaceSeparator(this.current, declaredSize, alignment, runBits);
        this.current = this.bitfields.RunEnd;
        return this.current;
    }

    /// <summary>Records where an ordinary field ended, so the next field is placed after it.</summary>
    /// <param name="fieldEnd">The position after the field.</param>
    public void CompleteField(long fieldEnd)
    {
        this.current = fieldEnd;
    }

    /// <summary>The composite's end: its current extent, padded to its alignment when the layout is aligned.</summary>
    /// <param name="structAlignment">The composite's alignment.</param>
    /// <returns>The position after the composite.</returns>
    public readonly long Finish(int structAlignment)
        => this.aligned ? LayoutMath.AlignUp(this.current, structAlignment) : this.current;
}

/// <summary>Where a bitfield lives: its storage unit's start and size, and its bit offset within the unit.</summary>
/// <param name="UnitStart">The unit's first byte.</param>
/// <param name="UnitSize">The unit's size in bytes (a packed window can differ from the declared type's size).</param>
/// <param name="BitOffset">The declaration-order bit offset inside the unit.</param>
public readonly record struct BitfieldSlot(long UnitStart, int UnitSize, int BitOffset);
