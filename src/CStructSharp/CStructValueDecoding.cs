namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     The decoding rules that depend on the layout's settings, which the compiled engine's reader and its path resolver
///     share: how a bitfield is decoded under the layout's bit order, and the size check before a pointer target is read
///     under its pointer size. The rules that depend only on the field are in <see cref="ValueDecoding"/>.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>The debug records of an ordinary read: one shared empty list, which callers only discard.</summary>
    private static readonly List<DebugData> NoDebugData = new(0);

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
        return field.Enum is { } enm ? ValueDecoding.CreateEnumValue(enm, content) : content;
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
