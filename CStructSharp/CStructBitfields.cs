namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Structure;

/// <summary>Centralizes the portable unsigned-value rules shared by bitfield readers and writers.</summary>
public partial class CStruct
{
    private readonly ConstructionDictionary<string, BitfieldStorageCodec> integralBitfieldStorageCodecs =
        new(StringComparer.Ordinal);

    /// <summary>Extracts one unsigned bit slice from a signed or unsigned primitive storage value.</summary>
    private static ulong ExtractBitfieldValue(object storageValue, int bitOffset, int bitSize)
    {
        ulong rawValue = ConvertBitfieldStorageToUnsigned(storageValue);

        // Stryker disable once Bitwise: signed-fill and zero-fill right shifts are identical for ulong.
        ulong shiftedValue = rawValue >> bitOffset;
        return shiftedValue & GetBitfieldMask(bitSize);
    }

    /// <summary>Combines one validated bitfield value with the neighboring bits in its storage unit.</summary>
    private static ulong MergeBitfieldValue(ulong storageValue, ulong fieldValue, int bitOffset, int bitSize)
    {
        ulong mask = GetBitfieldMask(bitSize);
        ulong shiftedMask = mask << bitOffset;
        return (storageValue & ~shiftedMask) | (fieldValue << bitOffset);
    }

    /// <summary>Converts and validates a caller value against one bitfield's unsigned numeric domain.</summary>
    private static ulong ValidateBitfieldWriteValue(Field field, object? value)
    {
        if (value is null)
        {
            throw new CStructWriteException("Bitfield value cannot be null: " + field.Name.Name);
        }

        bool isOutsideIntegerDomain = value is bool ||
                                      (value is decimal decimalValue &&
                                       decimalValue != decimal.Truncate(decimalValue)) ||
                                      (value is double doubleValue &&
                                       (!double.IsFinite(doubleValue) || doubleValue != Math.Truncate(doubleValue))) ||
                                      (value is float floatValue &&
                                       (!float.IsFinite(floatValue) || floatValue != MathF.Truncate(floatValue)));
        if (isOutsideIntegerDomain)
        {
            throw new CStructWriteException(
                $"Bitfield value for '{field.Name.Name}' must be an unsigned integer that fits {field.BitSize} bits.");
        }

        ulong converted = 0;
        try
        {
            converted = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            throw new CStructWriteException(
                $"Bitfield value for '{field.Name.Name}' must be an unsigned integer that fits {field.BitSize} bits.",
                exception);
        }

        ulong maximum = GetBitfieldMask(field.BitSize);
        if (converted > maximum)
        {
            throw new CStructWriteException(
                $"Bitfield value for '{field.Name.Name}' exceeds the unsigned {field.BitSize}-bit range.");
        }

        return converted;
    }

    /// <summary>Builds a low-bit mask without overflowing the full 64-bit case.</summary>
    private static ulong GetBitfieldMask(int bitSize)
    {
        return bitSize == 64 ? ulong.MaxValue : (1UL << bitSize) - 1UL;
    }

    /// <summary>Reinterprets signed primitive values as raw same-width storage bits.</summary>
    private static ulong ConvertBitfieldStorageToUnsigned(object value)
    {
        return value switch
        {
            sbyte signed8 => unchecked((byte)signed8),
            short signed16 => unchecked((ushort)signed16),
            int signed32 => unchecked((uint)signed32),
            long signed64 => unchecked((ulong)signed64),
            byte unsigned8 => unsigned8,
            ushort unsigned16 => unsigned16,
            uint unsigned32 => unsigned32,
            ulong unsigned64 => unsigned64,
            char character => character,
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    ///     Records the scalar integral primitive codecs that are safe bitfield storage, including their encoded byte
    ///     order. A read/write delegate alone is deliberately not sufficient capability.
    /// </summary>
    private void BuildIntegralBitfieldStorageCodecs()
    {
        this.RegisterBitfieldStorageCodec("byte", 1, this.IsLittleEndian);
        this.RegisterBitfieldStorageCodec("int8", 1, this.IsLittleEndian);
        this.RegisterBitfieldStorageCodec("uint8", 1, this.IsLittleEndian);
        this.RegisterBitfieldStorageCodec("char", 1, this.IsLittleEndian);

        foreach ((string name, int byteSize) in new[]
                 {
                     ("wchar", 2),
                     ("int16", 2),
                     ("uint16", 2),
                     ("int32", 4),
                     ("uint32", 4),
                     ("int64", 8),
                     ("uint64", 8),
                 })
        {
            this.RegisterBitfieldStorageCodec(name + ">", byteSize, false);
            this.RegisterBitfieldStorageCodec(name + "<", byteSize, true);
            this.RegisterBitfieldStorageCodec(name, byteSize, this.IsLittleEndian);
        }

        foreach (KeyValuePair<string, string> alias in FieldTypeAliasses)
        {
            if (this.integralBitfieldStorageCodecs.TryGetValue(
                    alias.Value,
                    out BitfieldStorageCodec storageCodec))
            {
                this.RegisterBitfieldStorageCodec(alias.Key, storageCodec);
            }
        }
    }

    /// <summary>Registers one eligible codec and verifies that its reader, writer, and fixed width agree.</summary>
    private void RegisterBitfieldStorageCodec(string name, int byteSize, bool isLittleEndian)
    {
        this.RegisterBitfieldStorageCodec(name, new BitfieldStorageCodec(byteSize, isLittleEndian));
    }

    /// <summary>Registers an alias of an already validated integral storage codec.</summary>
    private void RegisterBitfieldStorageCodec(string name, BitfieldStorageCodec storageCodec)
    {
        bool hasMatchingSize = this.fieldAlignments.TryGetValue(name, out byte byteSize) &&
                               byteSize == storageCodec.ByteSize;
        if (!hasMatchingSize ||
            !this.fieldHandlers.ContainsKey(name) ||
            !this.writeHandlers.ContainsKey(name))
        {
            throw new InvalidOperationException("Integral bitfield codec registration is inconsistent: " + name);
        }

        this.integralBitfieldStorageCodecs.Add(name, storageCodec);
    }

    /// <summary>
    ///     Returns the explicitly capable integral storage codec after validating scalar shape and bit width.
    /// </summary>
    private BitfieldStorageCodec ValidateBitField(Field field)
    {
        if (!ReferenceEquals(field.ArrayCount, Field.NoArray))
        {
            throw new InvalidOperationException("Arrays cannot be bitfields.");
        }

        if (field.IsPointer)
        {
            throw new InvalidOperationException("Pointers cannot be bitfields.");
        }

        if (!this.integralBitfieldStorageCodecs.TryGetValue(
                field.Type.Name,
                out BitfieldStorageCodec storageCodec))
        {
            throw new InvalidOperationException(
                $"Bitfield storage type '{field.Type.Name}' is not a direct scalar integral codec.");
        }

        if (field.BitSize <= 0 || field.BitSize > storageCodec.BitCapacity)
        {
            throw new InvalidOperationException(
                $"Bitfield width for {field.Name.Name} must be between 1 and {storageCodec.BitCapacity}.");
        }

        return storageCodec;
    }

    /// <summary>Describes the fixed-width integer storage facts needed by every bitfield executor.</summary>
    private readonly record struct BitfieldStorageCodec(int ByteSize, bool IsLittleEndian)
    {
        public int BitCapacity => checked(this.ByteSize * 8);
    }
}
