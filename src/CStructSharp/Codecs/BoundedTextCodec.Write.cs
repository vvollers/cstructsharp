namespace CStructSharp.Codecs;

using System;

/// <summary>Encodes validated bounded text into storage owned by the calling writer.</summary>
internal static partial class BoundedTextCodec
{
    /// <summary>Encodes an immutable string after its complete encoding and capacity validation.</summary>
    /// <param name="type">The bounded-text encoding spelling used for validation.</param>
    /// <param name="value">The string already accepted by <see cref="GetByteCount"/>.</param>
    /// <param name="destination">Storage for at least the validated byte count; padding is left untouched.</param>
    /// <remarks>The caller validates before reserving or writing output, preserving failure order and diagnostics.</remarks>
    internal static void EncodeValidated(string type, string value, Span<byte> destination)
    {
        if (type != "cp437")
        {
            GetEncoding(type).GetBytes(value, destination);
            return;
        }

        for (int index = 0; index < value.Length; index++)
        {
            destination[index] = EncodeCp437(value[index]);
        }
    }
}
