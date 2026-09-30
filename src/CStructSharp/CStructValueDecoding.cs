namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The value-decoding rules the compiled engine, the static read plans and the direct fixed-root reads share: how a
///     multidimensional array is nested, how <c>char[N]</c> bytes become text, how a bitfield and an enum are decoded from
///     their storage, and the size check before a pointer target is read.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>The debug records of an ordinary read: one shared empty list, which callers only discard.</summary>
    private static readonly List<DebugData> NoDebugData = new(0);

    /// <summary>
    ///     Groups a flat, row-major list of leaf values into nested lists matching every dimension but the
    ///     outermost one, which the caller's own loop already accounted for by producing this flat list in the
    ///     first place. Each pass groups the previous level by one dimension's size, from the innermost dimension
    ///     outward - the same grouping a single-dimension array already performs once, repeated once per
    ///     additional dimension.
    /// </summary>
    /// <param name="flatValues">The leaf values (or character rows) in row-major order.</param>
    /// <param name="dimensionSizes">The sizes of the dimensions the values span, outermost first.</param>
    /// <returns>The outermost level's list; every level is a <see cref="List{T}"/>.</returns>
    internal static List<object?> ReshapeFlatArrayValues(List<object?> flatValues, IReadOnlyList<int> dimensionSizes)
    {
        List<object?> currentLevel = flatValues;
        for (int dimensionIndex = dimensionSizes.Count - 1; dimensionIndex >= 1; dimensionIndex--)
        {
            int groupSize = dimensionSizes[dimensionIndex];
            var nextLevel = new List<object?>(currentLevel.Count / groupSize);
            for (int start = 0; start < currentLevel.Count; start += groupSize)
            {
                nextLevel.Add(currentLevel.GetRange(start, groupSize));
            }

            currentLevel = nextLevel;
        }

        return currentLevel;
    }

    /// <summary>The fixed size of every dimension of a multidimensional array, which the reader and writer shape it by.</summary>
    /// <param name="field">A multidimensional array field.</param>
    /// <returns>The dimension sizes, outermost first.</returns>
    internal static int[] FixedDimensionSizes(CompiledField field)
    {
        var sizes = new int[field.Array.Dimensions.Length];
        for (int dimension = 0; dimension < sizes.Length; dimension++)
        {
            sizes[dimension] = field.Array.Dimensions[dimension].FixedCount ??
                               throw new InvalidOperationException("Multidimensional array dimension has no fixed count: " + field.Name);
        }

        return sizes;
    }

    /// <summary>A <c>char[N]</c> buffer is one Latin-1 character per byte, exactly as the per-element <c>char</c> reader produces.</summary>
    /// <param name="bytes">The buffer's bytes.</param>
    /// <returns>The characters, untrimmed.</returns>
    internal static string ReadLatin1Characters(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = bytes.Length <= 256 ? stackalloc char[bytes.Length] : new char[bytes.Length];
        for (int index = 0; index < chars.Length; index++)
        {
            chars[index] = (char)bytes[index];
        }

        return new string(chars);
    }

    /// <summary>
    ///     Decodes one bitfield from its placed storage unit, the one rule the compiled engine's reader and its path
    ///     resolver share: the field's own bits, as an <see cref="int"/> below 32 bits and a <see cref="ulong"/>
    ///     otherwise, and for an enum or flag bitfield the enum result of those bits.
    /// </summary>
    /// <param name="field">The bitfield.</param>
    /// <param name="unit">The storage unit's value as read.</param>
    /// <param name="bitOffset">The field's bit offset in the unit, in declaration order.</param>
    /// <param name="unitBits">The unit's width in bits.</param>
    /// <returns>The field's value.</returns>
    internal object DecodeBitfield(CompiledField field, object unit, int bitOffset, int unitBits)
    {
        ulong extracted = BitfieldCodecTable.ExtractBitfieldValue(
            unit,
            BitfieldCodecTable.EffectiveShift(bitOffset, field.BitSize, unitBits, this.highBitFirst),
            field.BitSize);
        object content = field.BitSize < 32 ? (object)(int)extracted : extracted;
        return field.Enum is { } enm ? CreateEnumValue(enm, content) : content;
    }

    /// <summary>Maps a decoded storage value to the enum result (shared by the compiled engine and the static read plans).</summary>
    /// <param name="compiled">The enum or flag type.</param>
    /// <param name="storageValue">The value its storage codec decoded.</param>
    /// <returns>The enum result: the value, its member name if any, and (for a flag) its decomposition on first use.</returns>
    internal static EnumValueResult CreateEnumValue(CompiledEnumType compiled, object storageValue)
    {
        BigInteger value = compiled.Integer.FromStorageValue(storageValue);
        ulong rawBits = compiled.Integer.ToRawBits(value);
        if (compiled.IsFlag)
        {
            // The member decomposition is deferred to first use so a flag read costs what an enum read costs.
            return new FlagValueResult(
                compiled.Name,
                compiled.FindName(rawBits),
                value,
                rawBits,
                compiled.Integer.StorageType,
                compiled.Integer.BitWidth,
                compiled.Integer.IsSigned,
                compiled.Decompose);
        }

        return new EnumValueResult(
            compiled.Name,
            compiled.FindName(rawBits),
            value,
            rawBits,
            compiled.Integer.StorageType,
            compiled.Integer.BitWidth,
            compiled.Integer.IsSigned);
    }

    /// <summary>
    ///     Checks an optional caller limit before reading a fixed-size pointer target; the compiled engine's reader and its
    ///     path resolver both check it here.
    /// </summary>
    /// <param name="pointerDepth">The pointer levels still to follow; above 1 the target is another pointer.</param>
    /// <param name="field">The pointer field.</param>
    /// <param name="maxPointerTargetBytes">The operation's <see cref="ReadOptions.MaxPointerTargetBytes"/>, or <see langword="null"/> for none.</param>
    /// <param name="elementCount">The number of target elements: the evaluated <c>@count</c>, otherwise 1.</param>
    /// <exception cref="CStructReadLimitException">The target's size is unknown, or larger than the limit.</exception>
    internal void EnsurePointerTargetSize(
        int pointerDepth,
        CompiledField field,
        long? maxPointerTargetBytes,
        int elementCount)
    {
        if (!maxPointerTargetBytes.HasValue)
        {
            // No configured budget means the existing pointer behavior remains unrestricted.
            return;
        }

        long? targetSize = pointerDepth > 1
                               ? this.PointerSize
                               : field.HasCountedTarget
                                   ? field.Type.Symbol.FixedSize * (long)elementCount
                                   : GetFixedTargetSize(field);
        if (!targetSize.HasValue)
        {
            // A fixed budget cannot safely approve a string or an unsized structure whose eventual length is unknown.
            throw new CStructReadLimitException(ReadFailures.PointerTargetVariableLength);
        }

        if (targetSize.Value > maxPointerTargetBytes.Value)
        {
            // Refuse the target before decoding so malformed data cannot bypass the caller's memory-safety policy.
            throw new CStructReadLimitException(ReadFailures.PointerTargetLimit);
        }
    }

    /// <summary>Returns a target's known size, or <see langword="null"/> when it is variable length.</summary>
    /// <param name="field">The pointer field.</param>
    /// <returns>The target's fixed size in bytes, or <see langword="null"/>.</returns>
    private static long? GetFixedTargetSize(CompiledField field)
    {
        if (field.HasTerminatedCodec)
        {
            // A terminator determines string length at runtime, so no finite static bound can be reported here.
            return null;
        }

        // The compiled type carries the static extent only when no runtime expression or terminator controls it.
        return field.Type.Symbol.FixedSize;
    }
}
