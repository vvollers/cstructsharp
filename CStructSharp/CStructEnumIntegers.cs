namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>Compiles and converts the exact signed/unsigned integer domains accepted for enum storage.</summary>
public partial class CStruct
{
    private readonly ConstructionDictionary<string, EnumIntegerCodec> enumIntegerCodecs =
        new(StringComparer.Ordinal);

    /// <summary>Resolves and validates every enum backing declaration before layout alignment is consulted.</summary>
    private void CompileEnumStorageDescriptors(IEnumerable<CStructElement> declarations)
    {
        foreach (CstructEnum enm in declarations.OfType<CstructEnum>())
        {
            string storageName = this.ResolveEnumStorageName(
                enm.Type,
                new HashSet<string>(StringComparer.Ordinal));
            if (!EnumIntegerCodec.TryCreate(storageName, out EnumIntegerCodec? codec))
            {
                throw new CStructLayoutException(
                    $"Enum '{enm.Name.Name}' storage type '{enm.Type.Name}' must resolve to " +
                    "a scalar signed or unsigned 8/16/32/64-bit integer codec.");
            }

            this.enumIntegerCodecs.Add(enm.Name.Name, codec!);
        }
    }

    /// <summary>Follows scalar typedefs until an enum reaches a direct built-in storage spelling.</summary>
    private string ResolveEnumStorageName(Identifier type, HashSet<string> visiting)
    {
        if (type.PointerDepth != 0)
        {
            throw new CStructLayoutException(
                "Enum storage type cannot be a pointer: " + type.Name);
        }

        if (!this.cStructElements.TryGetValue(type.Name, out CStructElement? declaration) ||
            declaration is not Typedef { Struct: null, } alias)
        {
            return type.Name;
        }

        if (!visiting.Add(alias.Name.Name))
        {
            throw new CStructLayoutException(
                "Circular typedef dependency detected at: " + alias.Name.Name);
        }

        try
        {
            return this.ResolveEnumStorageName(alias.Type, visiting);
        }
        finally
        {
            visiting.Remove(alias.Name.Name);
        }
    }

    /// <summary>Returns the validated exact integer descriptor owned by one enum declaration.</summary>
    private EnumIntegerCodec GetEnumIntegerCodec(string enumName)
    {
        return this.enumIntegerCodecs.TryGetValue(enumName, out EnumIntegerCodec? codec)
                   ? codec
                   : throw new CStructLayoutException(
                       "Enum has no validated integer storage descriptor: " + enumName);
    }

    /// <summary>Describes one exact integer storage domain without introducing a public numeric union.</summary>
    internal sealed class EnumIntegerCodec
    {
        /// <summary>Creates a canonical descriptor for an accepted direct spelling.</summary>
        public static bool TryCreate(string spelling, out EnumIntegerCodec? codec)
        {
            codec = spelling switch
            {
                "byte" or "uint8" => new EnumIntegerCodec("uint8", 8, false),
                "int8" => new EnumIntegerCodec("int8", 8, true),
                "uint16" or "ushort" => new EnumIntegerCodec("uint16", 16, false),
                "int16" or "short" => new EnumIntegerCodec("int16", 16, true),
                "uint32" or "uint" => new EnumIntegerCodec("uint32", 32, false),
                "int32" or "int" => new EnumIntegerCodec("int32", 32, true),
                "uint64" or "ulong" => new EnumIntegerCodec("uint64", 64, false),
                "int64" or "long" => new EnumIntegerCodec("int64", 64, true),
                _ => null,
            };
            return codec is not null;
        }

        /// <summary>Accepts only mathematical integral CLR inputs; floating/fractional conversion is never implicit.</summary>
        public static bool TryConvertIntegral(object? value, out BigInteger result)
        {
            switch (value)
            {
            case BigInteger number:
                result = number;
                return true;
            case sbyte number:
                result = number;
                return true;
            case byte number:
                result = number;
                return true;
            case short number:
                result = number;
                return true;
            case ushort number:
                result = number;
                return true;
            case int number:
                result = number;
                return true;
            case uint number:
                result = number;
                return true;
            case long number:
                result = number;
                return true;
            case ulong number:
                result = number;
                return true;
            default:
                result = BigInteger.Zero;
                return false;
            }
        }

        private EnumIntegerCodec(string storageType, int bitWidth, bool isSigned)
        {
            this.StorageType = storageType;
            this.BitWidth = bitWidth;
            this.IsSigned = isSigned;
            this.Minimum = isSigned ? -(BigInteger.One << (bitWidth - 1)) : BigInteger.Zero;
            this.Maximum = isSigned
                               ? (BigInteger.One << (bitWidth - 1)) - BigInteger.One
                               : (BigInteger.One << bitWidth) - BigInteger.One;
        }

        public int BitWidth { get; }

        public bool IsSigned { get; }

        public BigInteger Maximum { get; }

        public BigInteger Minimum { get; }

        public int SizeInBytes => this.BitWidth / 8;

        public string StorageType { get; }

        /// <summary>Converts a primitive reader result into its exact mathematical value.</summary>
        public BigInteger FromStorageValue(object value)
        {
            if (!TryConvertIntegral(value, out BigInteger result) || !this.Contains(result))
            {
                throw new InvalidOperationException(
                    $"Enum storage reader for {this.StorageType} returned an incompatible value.");
            }

            return result;
        }

        /// <summary>Converts an in-domain mathematical value to its declared-width storage bits.</summary>
        public ulong ToRawBits(BigInteger value)
        {
            this.EnsureInRange(value);
            BigInteger raw = value < BigInteger.Zero
                                 ? (BigInteger.One << this.BitWidth) + value
                                 : value;
            return (ulong)raw;
        }

        /// <summary>Interprets declared-width storage bits through this descriptor's signedness.</summary>
        public BigInteger FromRawBits(ulong rawBits)
        {
            BigInteger raw = rawBits;
            if (!this.IsSigned)
            {
                return raw;
            }

            ulong signBit = 1UL << (this.BitWidth - 1);
            return (rawBits & signBit) == 0
                       ? raw
                       : raw - (BigInteger.One << this.BitWidth);
        }

        /// <summary>Converts an exact validated value to the primitive writer's natural CLR type.</summary>
        public object ToStorageValue(BigInteger value)
        {
            this.EnsureInRange(value);
            return this.StorageType switch
            {
                "int8" => (sbyte)value,
                "uint8" => (byte)value,
                "int16" => (short)value,
                "uint16" => (ushort)value,
                "int32" => (int)value,
                "uint32" => (uint)value,
                "int64" => (long)value,
                "uint64" => (ulong)value,
                _ => throw new InvalidOperationException(
                    "Unknown enum integer storage type: " + this.StorageType),
            };
        }

        public bool Contains(BigInteger value)
        {
            return value >= this.Minimum && value <= this.Maximum;
        }

        public void EnsureInRange(BigInteger value)
        {
            if (!this.Contains(value))
            {
                throw new OverflowException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Value {value} is outside the {this.StorageType} enum domain."));
            }
        }
    }
}
