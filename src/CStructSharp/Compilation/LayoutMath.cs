namespace CStructSharp.Compilation;

using System;
using CStructSharp.Diagnostics;

/// <summary>Contains the small layout arithmetic rules shared by compilation and operation execution.</summary>
internal static class LayoutMath
{
    /// <summary>Rounds a byte offset upward without adding an extra alignment unit when it is already aligned.</summary>
    public static int AlignUp(int value, int alignment)
    {
        if (alignment <= 0)
        {
            throw new CStructLayoutException("Alignment must be greater than zero.");
        }

        int remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>Rounds a bit position up to a positive multiple (bit granularity, for bitfield cells).</summary>
    public static long AlignUp(long value, long alignment)
    {
        if (alignment <= 0)
        {
            throw new CStructLayoutException("Alignment must be greater than zero.");
        }

        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>Rounds a stream position up to a positive field boundary without losing 64-bit address range.</summary>
    public static long AlignUp(long value, int alignment)
    {
        if (alignment <= 0)
        {
            throw new CStructLayoutException("Alignment must be greater than zero.");
        }

        long remainder = value % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    /// <summary>
    ///     Validates an explicit alignment override (LANG-15's <c>@align(N)</c>) - it must be a positive power of two,
    ///     matching every native ABI's own alignment rule.
    /// </summary>
    public static int ValidateExplicitAlignment(int alignment, string fieldName)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new CStructLayoutException(
                "Explicit alignment override must be a positive power of two: " + fieldName + " = " + alignment);
        }

        return alignment;
    }
}
