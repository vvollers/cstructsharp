namespace CStructSharp.Writing;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>
///     The value rules every write shares, whichever path writes it (the compiled engine, <see cref="WriteEngine"/>, or a
///     static write plan): which supplied members a struct accepts, what padding holds, how a nested array's values are
///     flattened, how a typed numeric array is encoded in bulk, and the message for a value a field cannot hold.
/// </summary>
internal static class WriteValueRules
{
    // A declared shape may be much larger than the supplied values. Speculative capacity stays at most 512 KiB
    // of references on a 64-bit runtime, and is reserved only after the first child has validated successfully.
    private const int MaximumInitialFlattenedCapacity = 65_536;

    /// <summary>Whether the data supplies at least one leaf of an anonymous promoted member (transitively).</summary>
    /// <param name="promoted">The anonymous member.</param>
    /// <param name="data">The data that would carry its members.</param>
    /// <returns>Whether any named member under it is supplied.</returns>
    internal static bool SuppliesAnyPromotedMember(CompiledField promoted, object data)
    {
        if (promoted.Type.Symbol.Definition is not CompiledCompositeType composite)
        {
            return false;
        }

        foreach (CompiledField member in composite.Fields)
        {
            if (composite.PromotedFields.Contains(member))
            {
                if (SuppliesAnyPromotedMember(member, data))
                {
                    return true;
                }

                continue;
            }

            string name = member.Name;
            if (name.Length > 0 && WriteDataBinding.TryGetMemberValue(data, name, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     <see cref="UnknownMemberPolicy.Reject"/>: every member the supplied value carries must be one the composite
    ///     declares, matched by exact key (the lookup the writer performs). A parsed <see cref="UnionValue"/> is
    ///     trusted; a mapped class was materialized into the composite's own shape and so cannot carry an unknown key.
    ///     The compiled engine calls it at every non-promoted struct it enters, and the direct fixed-root write for its root.
    /// </summary>
    /// <param name="composite">The struct whose declared members are allowed.</param>
    /// <param name="data">The struct's bound value.</param>
    /// <exception cref="CStructWriteException">The value carries a member the struct does not declare.</exception>
    internal static void RejectUnknownMembers(CompiledCompositeType composite, object data)
    {
        StructShape shape = composite.Shape;
        if (data is UnionValue)
        {
            return;
        }

        foreach (string key in WriteDataBinding.EnumerateMemberNames(data))
        {
            if (!shape.TryGetIndex(key, out _))
            {
                throw UnknownMember(composite, key);
            }
        }

        RejectUnknownNestedMembers(composite, data);
    }

    /// <summary>Applies the same check to every by-value nested struct the composite declares, arrays included.</summary>
    private static void RejectUnknownNestedMembers(CompiledCompositeType composite, object data)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.Composite is not { } nested)
            {
                continue;
            }

            if (composite.PromotedFields.Contains(field))
            {
                // A promoted member's children live on the same data object; only its own nested composites need checking.
                RejectUnknownNestedMembers(nested, data);
                continue;
            }

            if (field.IsUnnamed || !WriteDataBinding.TryGetMemberValue(data, field.Name, out object? value) || value is null)
            {
                continue;
            }

            try
            {
                if (field.Array.Kind == CompiledArrayKind.Scalar)
                {
                    RejectUnknownMembers(nested, WriteDataBinding.Materialize(value, nested));
                }
                else if (value is System.Collections.IEnumerable elements and not string)
                {
                    foreach (object? element in elements)
                    {
                        if (element is not null)
                        {
                            RejectUnknownMembers(nested, WriteDataBinding.Materialize(element, nested));
                        }
                    }
                }
            }
            catch (CStructException exception) when (exception.NoteMember(field.Name, field.DisplayTypeSpelling))
            {
                throw;
            }
        }
    }

    /// <summary>The failure of <see cref="UnknownMemberPolicy.Reject"/> for one member the composite does not declare.</summary>
    /// <param name="composite">The struct whose declared members are allowed.</param>
    /// <param name="member">The supplied member name.</param>
    /// <returns>The failure, naming the member and the declared ones.</returns>
    private static CStructWriteException UnknownMember(CompiledCompositeType composite, string member)
    {
        string declared = composite.Shape.Names.Length == 0 ? "no members" : string.Join(", ", composite.Shape.Names);
        return new CStructWriteException(
            $"'{member}' is not a member of '{composite.Name}' (WriteOptions.UnknownMembers is Reject). The layout declares: {declared}.");
    }

    /// <summary>The all-zero value an unnamed padding field is written with: a zero scalar, or one zero per fixed element.</summary>
    /// <param name="field">The unnamed field.</param>
    /// <returns>A new value: <c>0</c>, an empty string for characters, or an array of zeroes.</returns>
    internal static object CreatePaddingValue(CompiledField field)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 0;
        }

        if (field.IsCharacterArray)
        {
            return string.Empty;
        }

        var zeroes = new object[field.Array.TotalFixedElementCount ?? 0];
        Array.Fill(zeroes, 0);
        return zeroes;
    }

    /// <summary>
    ///     Materializes a caller-supplied N-deep nested collection into a flat, row-major list of leaf values,
    ///     validating that every level matches its declared dimension size exactly. Each level is materialized
    ///     with the same <see cref="WriteValueMaterialization.ConvertToObjectList"/> a single-dimension array
    ///     already uses once - this just repeats that call once per remaining dimension.
    /// </summary>
    /// <param name="value">The caller's nested collection.</param>
    /// <param name="dimensionSizes">The declared size of each remaining dimension, outermost first.</param>
    /// <param name="fieldName">The array field, named in a failure.</param>
    /// <returns>The leaves in row-major order.</returns>
    /// <remarks>After the first child validates, a bounded capacity hint avoids repeated growth without trusting an arbitrarily large declared shape.</remarks>
    /// <exception cref="CStructWriteException">A level is not a collection or has a different number of elements.</exception>
    internal static List<object> FlattenNestedArrayValues(object value, IReadOnlyList<int> dimensionSizes, string fieldName)
    {
        IList<object> level = WriteValueMaterialization.ConvertToObjectList(value, dimensionSizes[0], fieldName);
        if (level.Count != dimensionSizes[0])
        {
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(fieldName, dimensionSizes[0], level.Count));
        }

        if (dimensionSizes.Count == 1)
        {
            return level as List<object> ?? level.ToList();
        }

        int[] remainingDimensions = [.. dimensionSizes.Skip(1),];
        var flattened = new List<object>();
        bool firstChild = true;
        foreach (object item in level)
        {
            List<object> child = FlattenNestedArrayValues(item, remainingDimensions, fieldName);
            if (firstChild)
            {
                int capacity = 1;
                foreach (int size in dimensionSizes)
                {
                    capacity = (int)Math.Min((long)capacity * size, MaximumInitialFlattenedCapacity);
                }

                flattened.EnsureCapacity(capacity);
                firstChild = false;
            }

            flattened.AddRange(child);
        }

        return flattened;
    }

    /// <summary>The shared unwritable-value text (<see cref="WriteFailures.UnwritableValue"/>) for one compiled field.</summary>
    /// <param name="value">The value that could not be encoded.</param>
    /// <param name="field">The field it was written as, whose type and accepted range the text names.</param>
    /// <returns>The failure message.</returns>
    internal static string DescribeUnwritableValue(object? value, CompiledField field)
        => WriteFailures.UnwritableValue(value, field.TypeSpelling, WriteFailures.AcceptedRange(field.Codec.Kind));

    /// <summary>
    ///     Bulk path for a typed numeric array whose element type is the codec's own CLR type - the
    ///     <see cref="PrimitiveArray{T}"/> a parse produced, or a plain <c>T[]</c> such as a mapped class's
    ///     <c>int[]</c> - and whose length matches: the elements are encoded straight from the typed storage, with
    ///     one vectorized byte swap when the layout's byte order differs from the machine's. Anything else -
    ///     including a length mismatch, so its message stays the engine's - takes the element loop.
    /// </summary>
    /// <param name="field">The numeric array field, whose codec gives the element type and byte order.</param>
    /// <param name="target">The destination bytes, exactly <paramref name="count"/> elements long.</param>
    /// <param name="value">The supplied array value.</param>
    /// <param name="count">The declared element count.</param>
    /// <returns>Whether the value was typed storage of that length and was encoded; otherwise nothing is written.</returns>
    internal static bool TryWriteTypedArray(CompiledField field, Span<byte> target, object value, int count)
    {
        PrimitiveCodec codec = field.Codec;
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => TryEncode<byte>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.Int8 => TryEncode<sbyte>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.Int16 => TryEncode<short>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.UInt16 => TryEncode<ushort>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.Int32 => TryEncode<int>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.UInt32 => TryEncode<uint>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.Int64 => TryEncode<long>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.UInt64 => TryEncode<ulong>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.Float32 => TryEncode<float>(value, target, count, codec.LittleEndian),
            PrimitiveCodecKind.Float64 => TryEncode<double>(value, target, count, codec.LittleEndian),
            _ => false,
        };
    }

    /// <summary>Encodes <paramref name="value"/> when it is a <typeparamref name="T"/> array of exactly <paramref name="count"/> elements.</summary>
    private static bool TryEncode<T>(object value, Span<byte> target, int count, bool littleEndian)
        where T : unmanaged
    {
        ReadOnlySpan<T> elements;
        switch (value)
        {
        case PrimitiveArray<T> parsed:
            elements = parsed.Span;
            break;
        case T[] array when array.GetType() == typeof(T[]):
            // The runtime lets a uint[] pass as an int[]; only the exact type encodes without the per-element range checks.
            elements = array;
            break;
        default:
            return false;
        }

        if (elements.Length != count)
        {
            return false;
        }

        Codec.EncodeIntegers(elements, target, littleEndian);
        return true;
    }
}
