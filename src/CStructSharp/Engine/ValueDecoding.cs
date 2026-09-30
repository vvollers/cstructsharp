namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The value-decoding rules the compiled engine, the static read plans and the direct fixed-root reads share: how a
///     multidimensional array is nested, how <c>char[N]</c> bytes become text, and how an enum is decoded from its storage.
/// </summary>
internal static class ValueDecoding
{
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
}
