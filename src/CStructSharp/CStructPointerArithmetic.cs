namespace CStructSharp;

using System;

/// <summary>Defines the signed stream-position and unsigned pointer-storage domain shared by every pointer operation.</summary>
internal static class CStructPointerArithmetic
{
    /// <summary>Converts one decoded unsigned pointer payload into the signed address domain used by streams.</summary>
    public static long DecodeStoredAddress(ulong storedAddress)
    {
        if (storedAddress > long.MaxValue)
        {
            throw new OverflowException("Pointer address exceeds the signed stream-position range.");
        }

        return (long)storedAddress;
    }

    /// <summary>Applies a relative origin to one non-null stored address with checked signed arithmetic.</summary>
    public static long ResolveTargetAddress(
        long storedAddress,
        PointerAddressingMode addressingMode,
        long origin)
    {
        return addressingMode == PointerAddressingMode.Relative
                   ? checked(storedAddress + origin)
                   : storedAddress;
    }

    /// <summary>Converts a caller pointer value into a signed target address and normalizes data failures.</summary>
    public static long ConvertTargetAddress(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is Pointer pointer)
        {
            return pointer.Address;
        }

        try
        {
            return Convert.ToInt64(value);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            throw new CStructWriteException(
                "Pointer address must be an integer in the signed stream-position range.",
                exception);
        }
    }

    /// <summary>
    ///     Converts a non-negative target address into its unsigned stored representation, preserving encoded zero for
    ///     null and enforcing the configured pointer width before any output occurs.
    /// </summary>
    public static ulong EncodeTargetAddress(
        long targetAddress,
        PointerAddressingMode addressingMode,
        long origin,
        byte pointerSize)
    {
        if (targetAddress == 0)
        {
            return 0;
        }

        if (targetAddress < 0)
        {
            throw new CStructWriteException("Pointer target addresses cannot be negative.");
        }

        long storedAddress;
        try
        {
            storedAddress = addressingMode == PointerAddressingMode.Relative
                                ? checked(targetAddress - origin)
                                : targetAddress;
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException(
                "Relative pointer address overflowed the signed stream-position range.",
                exception);
        }

        if (storedAddress < 0)
        {
            throw new CStructWriteException("The relative pointer offset cannot be negative.");
        }

        if (storedAddress == 0)
        {
            throw new CStructWriteException(
                "A non-null relative pointer target cannot equal the origin because stored zero represents null.");
        }

        ulong maximum = pointerSize switch
        {
            1 => byte.MaxValue,
            2 => ushort.MaxValue,
            4 => uint.MaxValue,
            8 => long.MaxValue,
            _ => throw new ArgumentOutOfRangeException(nameof(pointerSize), pointerSize, "Unsupported pointer size."),
        };
        ulong value = (ulong)storedAddress;
        if (value > maximum)
        {
            throw new CStructWriteException("Pointer address does not fit the configured pointer size.");
        }

        return value;
    }
}
