namespace CStructSharp.Codecs;

using System;
using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>
///     Centralizes the portable unsigned-value rules shared by bitfield readers and writers, and the per-instance
///     table of primitive codecs that are valid bitfield storage.
/// </summary>
internal sealed class BitfieldCodecTable
{
    private readonly Dictionary<string, Entry> codecs = new(StringComparer.Ordinal);

    /// <summary>Builds the table of scalar integral primitive codecs that are safe bitfield storage.</summary>
    /// <param name="isLittleEndian">The layout's default byte order for unsuffixed multi-byte type names.</param>
    /// <param name="fieldAlignments">The catalog's alignment per readable name; for a direct integral type it equals the byte width.</param>
    /// <param name="fieldTypeAliases">The C-style and shorthand type name aliases to mirror into this table.</param>
    public BitfieldCodecTable(
        bool isLittleEndian,
        IReadOnlyDictionary<string, byte> fieldAlignments,
        IReadOnlyDictionary<string, string> fieldTypeAliases)
    {
        this.Register("byte", 1, isLittleEndian, fieldAlignments);
        this.Register("int8", 1, isLittleEndian, fieldAlignments);
        this.Register("uint8", 1, isLittleEndian, fieldAlignments);
        this.Register("char", 1, isLittleEndian, fieldAlignments);

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
            this.Register(name + ">", byteSize, false, fieldAlignments);
            this.Register(name + "<", byteSize, true, fieldAlignments);
            this.Register(name, byteSize, isLittleEndian, fieldAlignments);
        }

        foreach (KeyValuePair<string, string> alias in fieldTypeAliases)
        {
            if (this.codecs.TryGetValue(alias.Value, out Entry storageCodec))
            {
                this.codecs.Add(alias.Key, storageCodec);
            }
        }
    }

    /// <summary>
    ///     The shift of a slice inside its unit: the declaration-order offset itself for low-bit-first allocation, or
    ///     counted down from the unit's top bit for high-bit-first allocation.
    /// </summary>
    /// <param name="bitOffset">The field's offset in bits in declaration order from the start of its unit.</param>
    /// <param name="bitSize">The field width in bits.</param>
    /// <param name="unitBits">The storage unit's width in bits.</param>
    /// <param name="highBitFirst">Whether bitfields are allocated from the unit's most significant bit down.</param>
    /// <returns>The number of bits to shift the field's value left by inside the unit.</returns>
    public static int EffectiveShift(int bitOffset, int bitSize, int unitBits, bool highBitFirst)
    {
        return highBitFirst ? unitBits - bitOffset - bitSize : bitOffset;
    }

    /// <summary>Reads one bitfield's unsigned value out of its decoded storage unit.</summary>
    /// <param name="storageValue">The decoded storage value: a boxed integer or <see cref="char"/>.</param>
    /// <param name="bitOffset">The shift in bits of the field's lowest bit inside the unit.</param>
    /// <param name="bitSize">The field width in bits.</param>
    /// <returns>The field's bits, shifted down and zero-extended.</returns>
    public static ulong ExtractBitfieldValue(object storageValue, int bitOffset, int bitSize)
    {
        ulong rawValue = ConvertBitfieldStorageToUnsigned(storageValue);

        // Stryker disable once Bitwise: signed-fill and zero-fill right shifts are identical for ulong.
        ulong shiftedValue = rawValue >> bitOffset;
        return shiftedValue & GetBitfieldMask(bitSize);
    }

    /// <summary>Combines one validated bitfield value with the neighboring bits in its storage unit.</summary>
    /// <param name="storageValue">The storage unit's current bits.</param>
    /// <param name="fieldValue">The validated field value, already within <paramref name="bitSize"/> bits.</param>
    /// <param name="bitOffset">The shift in bits of the field's lowest bit inside the unit.</param>
    /// <param name="bitSize">The field width in bits.</param>
    /// <returns>The storage unit with the field's bits replaced and every other bit unchanged.</returns>
    public static ulong MergeBitfieldValue(ulong storageValue, ulong fieldValue, int bitOffset, int bitSize)
    {
        ulong mask = GetBitfieldMask(bitSize);
        ulong shiftedMask = mask << bitOffset;
        return (storageValue & ~shiftedMask) | (fieldValue << bitOffset);
    }

    /// <summary>Converts and validates a caller value against one bitfield's unsigned numeric domain.</summary>
    /// <param name="name">The field name, used in error messages.</param>
    /// <param name="bitSize">The field width in bits.</param>
    /// <param name="value">The caller's value: any integral number, or a whole-valued floating-point number.</param>
    /// <returns>The value as an unsigned integer no larger than the field's bit mask.</returns>
    /// <exception cref="CStructWriteException">
    ///     The value is null, a <see cref="bool"/>, not a whole number, negative, or wider than
    ///     <paramref name="bitSize"/> bits.
    /// </exception>
    public static ulong ValidateBitfieldWriteValue(string name, int bitSize, object? value)
    {
        if (value is null)
        {
            throw new CStructWriteException("Bitfield value cannot be null: " + name);
        }

        bool isOutsideIntegerDomain = value is bool ||
                                      (value is decimal decimalValue &&
                                       decimalValue != decimal.Truncate(decimalValue)) ||
                                      (value is double doubleValue &&
                                       (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue) || doubleValue != Math.Truncate(doubleValue))) ||
                                      (value is float floatValue &&
                                       (float.IsNaN(floatValue) || float.IsInfinity(floatValue) || floatValue != (float)Math.Truncate(floatValue)));
        if (isOutsideIntegerDomain)
        {
            throw new CStructWriteException(
                $"Bitfield value for '{name}' must be an unsigned integer that fits {bitSize} bits.");
        }

        ulong converted = 0;
        try
        {
            converted = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            throw new CStructWriteException(
                $"Bitfield value for '{name}' must be an unsigned integer that fits {bitSize} bits.",
                exception);
        }

        ulong maximum = GetBitfieldMask(bitSize);
        if (converted > maximum)
        {
            throw new CStructWriteException(
                $"Bitfield value for '{name}' exceeds the unsigned {bitSize}-bit range.");
        }

        return converted;
    }

    /// <summary>Builds a low-bit mask without overflowing the full 64-bit case.</summary>
    /// <param name="bitSize">The field width in bits, 1 through 64.</param>
    /// <returns>A mask with the lowest <paramref name="bitSize"/> bits set.</returns>
    public static ulong GetBitfieldMask(int bitSize)
    {
        return bitSize == 64 ? ulong.MaxValue : (1UL << bitSize) - 1UL;
    }

    /// <summary>Reinterprets signed primitive values as raw same-width storage bits.</summary>
    /// <param name="value">The decoded storage value: a boxed integer or <see cref="char"/>.</param>
    /// <returns>The storage bits zero-extended to 64 bits, so a negative value does not fill the high bits.</returns>
    public static ulong ConvertBitfieldStorageToUnsigned(object value)
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
    ///     Returns the explicitly capable integral storage codec after validating scalar shape and bit width.
    /// </summary>
    /// <param name="field">The bitfield declaration whose storage type and width are checked.</param>
    /// <returns>The storage facts of the field's declared type.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The field is an array or pointer, its type is not integral bitfield storage, or its width is not between 1
    ///     and the storage's bit capacity.
    /// </exception>
    public Entry ValidateBitField(Field field)
    {
        if (!ReferenceEquals(field.ArrayCount, Field.NoArray))
        {
            throw new InvalidOperationException("Arrays cannot be bitfields.");
        }

        if (field.IsPointer)
        {
            throw new InvalidOperationException("Pointers cannot be bitfields.");
        }

        if (!this.codecs.TryGetValue(field.Type.Name, out Entry storageCodec))
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

    /// <summary>Registers one eligible codec and verifies that its reader, writer, and fixed width agree.</summary>
    private void Register(
        string name,
        int byteSize,
        bool isLittleEndian,
        IReadOnlyDictionary<string, byte> fieldAlignments)
    {
        // The catalog's alignment of a direct integral type is its size; a disagreement means the storage-unit
        // width the placement would assume differs from what the codec reads - refuse rather than corrupt bits.
        bool hasMatchingSize = fieldAlignments.TryGetValue(name, out byte alignedByteSize) &&
                               alignedByteSize == byteSize;
        if (!hasMatchingSize)
        {
            throw new InvalidOperationException("Integral bitfield codec registration is inconsistent: " + name);
        }

        this.codecs.Add(name, new Entry(byteSize, isLittleEndian));
    }

    /// <summary>Describes the fixed-width integer storage facts needed by every bitfield executor.</summary>
    public readonly record struct Entry(int ByteSize, bool IsLittleEndian)
    {
        /// <summary>The storage unit's width in bits, the widest bitfield it can hold.</summary>
        public int BitCapacity => checked(this.ByteSize * 8);
    }
}
