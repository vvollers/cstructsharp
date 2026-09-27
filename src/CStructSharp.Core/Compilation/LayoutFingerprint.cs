namespace CStructSharp.Compilation;

using System.Globalization;
using System.Text;

/// <summary>
///     A 64-bit summary of everything that decides where the members of a compiled struct sit and how their bytes
///     decode: sizes, alignment, offsets, codecs, byte order, arrays, pointers, bitfields, conditions, enums and nested
///     structs. The source generator stamps it on a mapped class's direct reader and writer; the runtime compares it
///     with the fingerprint of the layout a call actually uses, and takes the direct route only when the two agree.
/// </summary>
/// <remarks>
///     Core compiles into the runtime and the generator alike, so both compute the fingerprint with this code. It is a
///     comparison key, not a security boundary: the fields that feed it are written out as text and hashed with FNV-1a.
///     Anything added to the compiled model that changes placement or decoding must be added here as well.
/// </remarks>
internal static class LayoutFingerprint
{
    /// <summary>Computes the fingerprint of <paramref name="composite"/> and every struct nested in it.</summary>
    /// <param name="composite">The compiled struct or union.</param>
    /// <returns>The fingerprint.</returns>
    public static ulong Compute(CompiledCompositeType composite)
    {
        var text = new StringBuilder();
        Append(text, composite, 0);
        ulong hash = 14695981039346656037UL;
        foreach (char character in text.ToString())
        {
            hash = (hash ^ character) * 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Writes one composite and its fields, recursing into nested composites up to a fixed depth.</summary>
    private static void Append(StringBuilder text, CompiledCompositeType composite, int depth)
    {
        text.Append(composite.IsUnion ? 'U' : 'S')
            .Append(Number(composite.Symbol.FixedSize)).Append('/')
            .Append(composite.Symbol.Alignment.ToString(CultureInfo.InvariantCulture))
            .Append(composite.HasDirectConditionalFields ? "?" : string.Empty)
            .Append('{');
        foreach (CompiledField field in composite.Fields)
        {
            text.Append(field.Name).Append('@').Append(Number(field.FixedOffset))
                .Append(':').Append(((int)field.Codec.Kind).ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(field.Codec.Size.ToString(CultureInfo.InvariantCulture))
                .Append(field.Codec.LittleEndian ? 'l' : 'b')
                .Append('[').Append(((int)field.Array.Kind).ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(field.Array.Dimensions.Length.ToString(CultureInfo.InvariantCulture))
                .Append(',').Append(Number(field.Array.FixedCount))
                .Append(',').Append(Dimensions(field.Array))
                .Append(',').Append(Number(field.FixedElementSize)).Append(']')
                .Append('*').Append(field.PointerDepth.ToString(CultureInfo.InvariantCulture))
                .Append('~').Append(field.BitSize.ToString(CultureInfo.InvariantCulture))
                .Append('.').Append(field.BitOffset.ToString(CultureInfo.InvariantCulture))
                .Append('/').Append(Number(field.BitUnitSize)).Append('/').Append(Number(field.BitStorageSize))
                .Append(field.BitStorageIsLittleEndian switch { true => "l", false => "b", null => "-" })
                .Append(field.IsZeroWidthBitfield ? "z" : string.Empty)
                .Append(field.IsCharacterArray ? "c" : string.Empty)
                .Append(field.IsWideCharElement ? "w" : string.Empty)
                .Append(field.IsConditional ? "?" : string.Empty)
                .Append(field.AssertedOffset is int asserted ? "!" + asserted.ToString(CultureInfo.InvariantCulture) : string.Empty)
                .Append(field.HasCountedTarget ? "#" : string.Empty)
                .Append(composite.PromotedFields.Contains(field) ? "^" : string.Empty);
            if (field.Enum is { } enumeration)
            {
                text.Append("e").Append(enumeration.IsFlag ? "f" : string.Empty)
                    .Append(enumeration.Integer.StorageType);
            }

            if (field.Composite is { } nested)
            {
                if (depth < 64)
                {
                    Append(text, nested, depth + 1);
                }
                else
                {
                    text.Append("{...}");
                }
            }

            text.Append(';');
        }

        text.Append('}');
    }

    /// <summary>Every dimension's fixed count, outermost first, with <c>-</c> for a runtime-sized one.</summary>
    /// <param name="array">The array shape.</param>
    /// <returns>The counts, separated by <c>x</c>.</returns>
    private static string Dimensions(CompiledArrayShape array)
    {
        var text = new StringBuilder();
        foreach (CompiledArrayDimension dimension in array.Dimensions)
        {
            text.Append(Number(dimension.FixedCount)).Append('x');
        }

        return text.ToString();
    }

    /// <summary>An optional number as text, with <c>-</c> for none.</summary>
    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";
}
