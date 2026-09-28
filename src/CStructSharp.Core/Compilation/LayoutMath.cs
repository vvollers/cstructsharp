namespace CStructSharp.Compilation;

using System;
using CStructSharp.Diagnostics;

/// <summary>Contains the small layout arithmetic rules shared by compilation and operation execution.</summary>
internal static class LayoutMath
{
    /// <summary>Rounds a byte offset upward without adding an extra alignment unit when it is already aligned.</summary>
    /// <param name="value">The byte offset to round.</param>
    /// <param name="alignment">The boundary, in bytes; must be positive.</param>
    /// <returns>The smallest multiple of the alignment that is at least <paramref name="value"/>.</returns>
    /// <exception cref="CStructLayoutException"><paramref name="alignment"/> is zero or negative.</exception>
    /// <exception cref="OverflowException">The rounded offset exceeds <see cref="int.MaxValue"/>.</exception>
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
    /// <param name="value">The bit position to round.</param>
    /// <param name="alignment">The boundary, in bits; must be positive.</param>
    /// <returns>The smallest multiple of the alignment that is at least <paramref name="value"/>.</returns>
    /// <exception cref="CStructLayoutException"><paramref name="alignment"/> is zero or negative.</exception>
    /// <exception cref="OverflowException">The rounded position exceeds <see cref="long.MaxValue"/>.</exception>
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
    /// <param name="value">The stream position, in bytes, to round.</param>
    /// <param name="alignment">The boundary, in bytes; must be positive.</param>
    /// <returns>The smallest multiple of the alignment that is at least <paramref name="value"/>.</returns>
    /// <exception cref="CStructLayoutException"><paramref name="alignment"/> is zero or negative.</exception>
    /// <exception cref="OverflowException">The rounded position exceeds <see cref="long.MaxValue"/>.</exception>
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
    ///     Validates an explicit alignment override (<c>@align(N)</c>) - it must be a positive power of two,
    ///     matching every native ABI's own alignment rule.
    /// </summary>
    /// <param name="alignment">The requested alignment, in bytes.</param>
    /// <param name="fieldName">The field that declares the override, named in the error message.</param>
    /// <returns>The validated <paramref name="alignment"/>, unchanged.</returns>
    /// <exception cref="CStructLayoutException">The alignment is not a positive power of two.</exception>
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
