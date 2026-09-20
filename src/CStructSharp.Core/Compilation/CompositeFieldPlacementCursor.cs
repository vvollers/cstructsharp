namespace CStructSharp.Compilation;

using System;
using CStructSharp.Syntax;

/// <summary>
///     Walks a composite's fields in declaration order, computing each field's start address and bit offset while
///     tracking bitfield storage-unit continuation and struct alignment. Used identically by target resolution
///     (which stops as soon as the requested field is found) and by extent measurement (which walks every field to
///     Compute the composite's total size).
/// </summary>
internal sealed class CompositeFieldPlacementCursor
{
    private readonly bool aligned;
    private BitfieldPlacement bitfields;
    private long current;

    public CompositeFieldPlacementCursor(long start, bool aligned, BitfieldPacking packing, bool highBitFirst = false)
    {
        this.current = start;
        this.aligned = aligned;
        this.bitfields = new BitfieldPlacement(packing, aligned, highBitFirst);
    }

    /// <summary>Gets the cursor's current position, including a struct's final tail after every field has been placed.</summary>
    public long Current => this.current;

    /// <summary>
    ///     Advances to one field's start, placing a bitfield in its storage unit (opening a new one when the packing
    ///     rule requires) or aligning an ordinary field normally.
    /// </summary>
    /// <returns>The field's start (a bitfield's unit start), the bit offset inside the unit, and the unit size in bytes (0 for an ordinary field).</returns>
    public (long FieldStart, int BitOffset, int UnitSize) AdvanceToField(CompiledField compiledField)
    {
        if (compiledField.IsZeroWidthBitfield)
        {
            this.bitfields.PlaceSeparator(this.current, compiledField.BitStorageSize ?? 1, compiledField.Alignment, compiledField.BitRunBits);
            this.current = this.bitfields.RunEnd;
            return (this.current, 0, 0);
        }

        if (compiledField.BitSize > 0)
        {
            int declaredSize = compiledField.BitStorageSize ??
                               throw new InvalidOperationException(
                                   "Compiled bitfield has no storage size: " + compiledField.Name);
            (long unitStart, int unitSize, int bitOffset) = this.bitfields.Place(this.current, declaredSize, compiledField.Alignment, compiledField.BitSize, compiledField.BitRunBits, compiledField.BitStorageIsLittleEndian ?? true, compiledField.Name);
            this.current = this.bitfields.RunEnd;
            return (unitStart, bitOffset, unitSize);
        }

        this.bitfields.Close();
        this.current = this.aligned ? LayoutMath.AlignUp(this.current, compiledField.Alignment) : this.current;
        return (this.current, 0, 0);
    }

    /// <summary>Records where a just-placed non-bitfield field actually ends, so the next field starts after it.</summary>
    public void CompleteField(long fieldEnd)
    {
        this.current = fieldEnd;
    }

    /// <summary>Rounds the composite's own tail up to its own alignment, reproducing C-compiler trailing padding.</summary>
    public long FinishComposite(int structAlignment)
    {
        return this.aligned ? LayoutMath.AlignUp(this.current, structAlignment) : this.current;
    }
}
