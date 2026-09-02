namespace CStructSharp;

using System;
using CStructSharp.Structure;

/// <summary>Contains the small layout arithmetic rules shared by compilation and operation execution.</summary>
public partial class CStruct
{
    /// <summary>Rounds a byte offset upward without adding an extra alignment unit when it is already aligned.</summary>
    private int AlignUp(int value, int alignment)
    {
        if (alignment <= 0)
        {
            throw new CStructLayoutException("Alignment must be greater than zero.");
        }

        int remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>Rounds a stream position up to a positive field boundary without losing 64-bit address range.</summary>
    private long AlignUp(long value, int alignment)
    {
        if (alignment <= 0)
        {
            throw new CStructLayoutException("Alignment must be greater than zero.");
        }

        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>
    ///     Decides whether the next bitfield requires a fresh primitive storage unit. Compilation, read, write, and path
    ///     operations share this rule so type changes, capacity, and alignment cannot drift independently.
    /// </summary>
    private bool StartsNewBitfieldUnit(
        string? activeType,
        int activeUnitSize,
        int activeAlignment,
        int bitsUsed,
        Field nextField,
        int nextUnitSize,
        int nextAlignment)
    {
        return activeUnitSize == 0 ||
               activeUnitSize != nextUnitSize ||
               activeAlignment != nextAlignment ||
               !string.Equals(activeType, nextField.Type.Name, StringComparison.Ordinal) ||
               bitsUsed + nextField.BitSize > nextUnitSize * 8;
    }
}
