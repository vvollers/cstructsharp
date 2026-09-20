namespace CStructSharp.Generators;

using System;
using System.Globalization;
using CStructSharp.Codecs;

/// <summary>
///     Parses one entry of <c>[CStructLayout(Codecs = ...)]</c>: <c>name</c>, <c>name:size</c>,
///     <c>name:size:alignment</c>, or <c>name:*:alignment</c> - the facts the compiler places a custom codec's fields
///     with; the instances come from the class at run time.
/// </summary>
internal static class CustomCodecDeclaration
{
    public static bool TryParse(string declaration, out CustomCodecDescriptor descriptor)
    {
        descriptor = default;
        string[] parts = declaration.Split(':');
        if (parts.Length is 0 or > 3 || parts[0].Length == 0)
        {
            return false;
        }

        int? size = null;
        if (parts.Length >= 2 && parts[1] != "*")
        {
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int fixedSize))
            {
                return false;
            }

            size = fixedSize;
        }

        int alignment = 1;
        if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out alignment))
        {
            return false;
        }

        descriptor = new CustomCodecDescriptor(parts[0].Trim(), size, alignment);
        return true;
    }

    /// <summary>The declaration text for a descriptor, as the generated validation quotes it.</summary>
    public static string Describe(CustomCodecDescriptor descriptor)
        => descriptor.Name + ":" + (descriptor.FixedSize is { } size ? size.ToString(CultureInfo.InvariantCulture) : "*") + ":" + descriptor.Alignment.ToString(CultureInfo.InvariantCulture);
}
