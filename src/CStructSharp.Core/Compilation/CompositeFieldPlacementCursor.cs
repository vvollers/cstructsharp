namespace CStructSharp.Compilation;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>
///     Walks a composite's fields in declaration order, computing each field's start address and bit offset while
///     tracking bitfield storage-unit continuation and struct alignment. Used identically by target resolution
///     (which stops as soon as the requested field is found) and by extent measurement (which walks every field to
///     Compute the composite's total size).
/// </summary>
internal sealed class CompositeFieldPlacementCursor
{
    private PlacementCursor cursor;

    /// <summary>Starts placing a composite's fields at a known position.</summary>
    /// <param name="start">The composite's first byte.</param>
    /// <param name="aligned">Whether the layout applies the portable alignment rules.</param>
    /// <param name="packing">The bitfield storage-sharing rule.</param>
    /// <param name="highBitFirst">Whether the first bitfield occupies the high end of its storage unit.</param>
    public CompositeFieldPlacementCursor(long start, bool aligned, BitfieldPacking packing, bool highBitFirst = false)
    {
        this.cursor = new PlacementCursor(start, aligned, packing, highBitFirst);
    }

    /// <summary>Gets the cursor's current position, including a struct's final tail after every field has been placed.</summary>
    public long Current => this.cursor.Current!.Value;

    /// <summary>
    ///     Advances a placement cursor held by value to one field's start: the one dispatch every runtime placer uses (this
    ///     class, and the compiled read engine, which keeps its cursor on the stack), so they place a separator, a bitfield
    ///     and an ordinary field identically.
    /// </summary>
    /// <param name="cursor">The composite's cursor, started at the composite's first byte; it is advanced.</param>
    /// <param name="compiledField">The next field in declaration order.</param>
    /// <returns>The field's start (a bitfield's unit start), the bit offset inside the unit, and the unit size in bytes (0 for an ordinary field).</returns>
    /// <exception cref="CStructLayoutException">The field does not sit at its asserted offset.</exception>
    public static (long FieldStart, int BitOffset, int UnitSize) AdvanceToField(ref PlacementCursor cursor, CompiledField compiledField)
    {
        if (compiledField.IsZeroWidthBitfield)
        {
            return (cursor.AdvanceToSeparator(compiledField.BitStorageSize ?? 1, compiledField.Alignment, compiledField.BitRunBits)!.Value, 0, 0);
        }

        if (compiledField.BitSize > 0)
        {
            int declaredSize = compiledField.BitStorageSize ??
                               throw new InvalidOperationException(
                                   "Compiled bitfield has no storage size: " + compiledField.Name);
            (long unitStart, int unitSize, int bitOffset) = cursor.AdvanceToBitfield(declaredSize, compiledField.Alignment, compiledField.BitSize, compiledField.BitRunBits, compiledField.BitStorageIsLittleEndian ?? true, compiledField.Name)!.Value;
            return (unitStart, bitOffset, unitSize);
        }

        long fieldStart = cursor.AdvanceToField(compiledField.Alignment)!.Value;
        if (compiledField.AssertedOffset is int asserted && compiledField.FixedOffset is null &&
            cursor.CheckAssertedOffset(fieldStart, asserted, compiledField.Name) is { } failure)
        {
            throw new CStructLayoutException(failure);
        }

        return (fieldStart, 0, 0);
    }

    /// <summary>
    ///     Advances to one field's start, placing a bitfield in its storage unit (opening a new one when the packing
    ///     rule requires) or aligning an ordinary field normally.
    /// </summary>
    /// <remarks>
    ///     An ordinary field with an <c>@N</c> offset assertion that compilation could not check (its offset depends on
    ///     the data) is checked here, so every operation that places the field checks it.
    /// </remarks>
    /// <param name="compiledField">The next field in declaration order.</param>
    /// <returns>The field's start (a bitfield's unit start), the bit offset inside the unit, and the unit size in bytes (0 for an ordinary field).</returns>
    /// <exception cref="CStructLayoutException">The field does not sit at its asserted offset.</exception>
    public (long FieldStart, int BitOffset, int UnitSize) AdvanceToField(CompiledField compiledField)
        => AdvanceToField(ref this.cursor, compiledField);

    /// <summary>Records where a just-placed non-bitfield field actually ends, so the next field starts after it.</summary>
    /// <param name="fieldEnd">The position one byte past the field's last byte, in the start's coordinates.</param>
    public void CompleteField(long fieldEnd)
    {
        this.cursor.CompleteField(fieldEnd);
    }

    /// <summary>Rounds the composite's own tail up to its own alignment, reproducing C-compiler trailing padding.</summary>
    /// <param name="structAlignment">The composite's own alignment in bytes.</param>
    /// <returns>The position one byte past the composite's trailing padding.</returns>
    public long FinishComposite(int structAlignment)
    {
        return this.cursor.Finish(structAlignment)!.Value;
    }
}
