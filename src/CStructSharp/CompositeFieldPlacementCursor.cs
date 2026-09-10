namespace CStructSharp;

using System;
using CStructSharp.Structure;

/// <summary>
///     Walks a composite's fields in declaration order, computing each field's start address and bit offset while
///     tracking bitfield storage-unit continuation and struct alignment. Used identically by target resolution
///     (which stops as soon as the requested field is found) and by extent measurement (which walks every field to
///     compute the composite's total size) - the two previously carried byte-identical copies of this state
///     machine.
/// </summary>
internal sealed class CompositeFieldPlacementCursor
{
    private readonly bool aligned;
    private long activeBitUnitStart = -1;
    private int activeBitUnitSize;
    private int activeBitUnitBitsUsed;
    private int activeBitUnitAlignment;
    private string? activeBitUnitType;
    private long current;

    public CompositeFieldPlacementCursor(long start, bool aligned)
    {
        this.current = start;
        this.aligned = aligned;
    }

    /// <summary>Gets the cursor's current position, including a struct's final tail after every field has been placed.</summary>
    public long Current => this.current;

    /// <summary>
    ///     Advances to one field's start, opening a new bitfield storage unit when required or aligning normally
    ///     otherwise.
    /// </summary>
    public (long FieldStart, int BitOffset) AdvanceToField(CompiledField compiledField)
    {
        Field field = compiledField.EffectiveField;
        long fieldStart;
        int bitOffset = 0;

        if (field.BitSize > 0)
        {
            int unitSize = compiledField.BitStorageSize ??
                           throw new InvalidOperationException(
                               "Compiled bitfield has no storage size: " + field.Name.Name);
            int alignment = compiledField.Alignment;
            bool startsNewUnit = LayoutMath.StartsNewBitfieldUnit(
                this.activeBitUnitType,
                this.activeBitUnitSize,
                this.activeBitUnitAlignment,
                this.activeBitUnitBitsUsed,
                field,
                unitSize,
                alignment);
            if (startsNewUnit)
            {
                this.current = this.aligned ? LayoutMath.AlignUp(this.current, alignment) : this.current;
                this.activeBitUnitStart = this.current;
                this.current = checked(this.current + unitSize);
                this.activeBitUnitSize = unitSize;
                this.activeBitUnitAlignment = alignment;
                this.activeBitUnitType = field.Type.Name;
                this.activeBitUnitBitsUsed = 0;
            }

            fieldStart = this.activeBitUnitStart;
            bitOffset = this.activeBitUnitBitsUsed;
            this.activeBitUnitBitsUsed += field.BitSize;
        }
        else
        {
            this.activeBitUnitStart = -1;
            this.activeBitUnitSize = 0;
            this.activeBitUnitBitsUsed = 0;
            this.activeBitUnitAlignment = 0;
            this.activeBitUnitType = null;

            int alignment = compiledField.Alignment;
            this.current = this.aligned ? LayoutMath.AlignUp(this.current, alignment) : this.current;
            fieldStart = this.current;
        }

        return (fieldStart, bitOffset);
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
