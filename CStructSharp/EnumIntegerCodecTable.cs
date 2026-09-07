namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>Compiles and looks up the exact signed/unsigned integer domain declared for every enum in a layout.</summary>
internal sealed class EnumIntegerCodecTable
{
    private readonly Dictionary<string, EnumIntegerCodec> codecs = new(StringComparer.Ordinal);

    /// <summary>Resolves and validates every enum backing declaration before layout alignment is consulted.</summary>
    /// <param name="declarations">The parsed top-level declarations, including every enum to compile a codec for.</param>
    /// <param name="cStructElements">
    ///     The layout's global declarations, populated with at least every top-level typedef, used to follow a
    ///     scalar typedef chain down to its direct built-in storage spelling.
    /// </param>
    public EnumIntegerCodecTable(
        IEnumerable<CStructElement> declarations,
        IReadOnlyDictionary<string, CStructElement> cStructElements)
    {
        foreach (CstructEnum enm in declarations.OfType<CstructEnum>())
        {
            string storageName = ResolveEnumStorageName(
                enm.Type,
                cStructElements,
                new HashSet<string>(StringComparer.Ordinal));
            if (!EnumIntegerCodec.TryCreate(storageName, out EnumIntegerCodec? codec))
            {
                throw new CStructLayoutException(
                    $"Enum '{enm.Name.Name}' storage type '{enm.Type.Name}' must resolve to " +
                    "a scalar signed or unsigned 8/16/32/64-bit integer codec.");
            }

            this.codecs.Add(enm.Name.Name, codec!);
        }
    }

    /// <summary>Returns the validated exact integer descriptor owned by one enum declaration.</summary>
    public EnumIntegerCodec Get(string enumName)
    {
        return this.codecs.TryGetValue(enumName, out EnumIntegerCodec? codec)
                   ? codec
                   : throw new CStructLayoutException(
                       "Enum has no validated integer storage descriptor: " + enumName);
    }

    /// <summary>Follows scalar typedefs until an enum reaches a direct built-in storage spelling.</summary>
    private static string ResolveEnumStorageName(
        Identifier type,
        IReadOnlyDictionary<string, CStructElement> cStructElements,
        HashSet<string> visiting)
    {
        if (type.PointerDepth != 0)
        {
            throw new CStructLayoutException(
                "Enum storage type cannot be a pointer: " + type.Name);
        }

        if (!cStructElements.TryGetValue(type.Name, out CStructElement? declaration) ||
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
            return ResolveEnumStorageName(alias.Type, cStructElements, visiting);
        }
        finally
        {
            visiting.Remove(alias.Name.Name);
        }
    }
}
