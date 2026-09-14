namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CStructSharp.Structure;

/// <summary>
///     Centralizes the portable unsigned-value rules shared by bitfield readers and writers, and the per-instance
///     table of primitive codecs that are valid bitfield storage.
/// </summary>
internal sealed class BitfieldCodecTable
{
    private readonly Dictionary<string, Entry> codecs = new(StringComparer.Ordinal);

    /// <summary>Builds the table of scalar integral primitive codecs that are safe bitfield storage.</summary>
    /// <param name="isLittleEndian">The layout's default byte order for unsuffixed multi-byte type names.</param>
    /// <param name="fieldAlignments">The already-populated primitive byte width for every registered field type name.</param>
    /// <param name="fieldHandlers">The already-populated primitive readers, used only to confirm a candidate type has one.</param>
    /// <param name="writeHandlers">The already-populated primitive writers, used only to confirm a candidate type has one.</param>
    /// <param name="fieldTypeAliases">The C-style and shorthand type name aliases to mirror into this table.</param>
    public BitfieldCodecTable(
        bool isLittleEndian,
        IReadOnlyDictionary<string, byte> fieldAlignments,
        IReadOnlyDictionary<string, Func<Stream, object>> fieldHandlers,
        IReadOnlyDictionary<string, Action<Stream, object>> writeHandlers,
        IReadOnlyDictionary<string, string> fieldTypeAliases)
    {
        this.Register("byte", 1, isLittleEndian, fieldAlignments, fieldHandlers, writeHandlers);
        this.Register("int8", 1, isLittleEndian, fieldAlignments, fieldHandlers, writeHandlers);
        this.Register("uint8", 1, isLittleEndian, fieldAlignments, fieldHandlers, writeHandlers);
        this.Register("char", 1, isLittleEndian, fieldAlignments, fieldHandlers, writeHandlers);

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
            this.Register(name + ">", byteSize, false, fieldAlignments, fieldHandlers, writeHandlers);
            this.Register(name + "<", byteSize, true, fieldAlignments, fieldHandlers, writeHandlers);
            this.Register(name, byteSize, isLittleEndian, fieldAlignments, fieldHandlers, writeHandlers);
        }

        foreach (KeyValuePair<string, string> alias in fieldTypeAliases)
        {
            if (this.codecs.TryGetValue(alias.Value, out Entry storageCodec))
            {
                this.codecs.Add(alias.Key, storageCodec);
            }
        }
    }

    /// <summary>Extracts one unsigned bit slice from a signed or unsigned primitive storage value.</summary>
    public static ulong ExtractBitfieldValue(object storageValue, int bitOffset, int bitSize)
    {
        ulong rawValue = ConvertBitfieldStorageToUnsigned(storageValue);

        // Stryker disable once Bitwise: signed-fill and zero-fill right shifts are identical for ulong.
        ulong shiftedValue = rawValue >> bitOffset;
        return shiftedValue & GetBitfieldMask(bitSize);
    }

    /// <summary>Combines one validated bitfield value with the neighboring bits in its storage unit.</summary>
    public static ulong MergeBitfieldValue(ulong storageValue, ulong fieldValue, int bitOffset, int bitSize)
    {
        ulong mask = GetBitfieldMask(bitSize);
        ulong shiftedMask = mask << bitOffset;
        return (storageValue & ~shiftedMask) | (fieldValue << bitOffset);
    }

    /// <summary>Converts and validates a caller value against one bitfield's unsigned numeric domain.</summary>
    public static ulong ValidateBitfieldWriteValue(Field field, object? value)
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
    public static ulong GetBitfieldMask(int bitSize)
    {
        return bitSize == 64 ? ulong.MaxValue : (1UL << bitSize) - 1UL;
    }

    /// <summary>Reinterprets signed primitive values as raw same-width storage bits.</summary>
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
        IReadOnlyDictionary<string, byte> fieldAlignments,
        IReadOnlyDictionary<string, Func<Stream, object>> fieldHandlers,
        IReadOnlyDictionary<string, Action<Stream, object>> writeHandlers)
    {
        bool hasMatchingSize = fieldAlignments.TryGetValue(name, out byte alignedByteSize) &&
                               alignedByteSize == byteSize;
        if (!hasMatchingSize || !fieldHandlers.ContainsKey(name) || !writeHandlers.ContainsKey(name))
        {
            throw new InvalidOperationException("Integral bitfield codec registration is inconsistent: " + name);
        }

        this.codecs.Add(name, new Entry(byteSize, isLittleEndian));
    }

    /// <summary>Describes the fixed-width integer storage facts needed by every bitfield executor.</summary>
    public readonly record struct Entry(int ByteSize, bool IsLittleEndian)
    {
        public int BitCapacity => checked(this.ByteSize * 8);
    }
}
