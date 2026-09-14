namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>Validates the case-sensitive declaration namespaces used by a compiled layout.</summary>
internal static class SymbolValidation
{
    /// <summary>Returns the user-facing declaration kind used in focused duplicate-name errors.</summary>
    public static string GetDeclarationKind(CStructElement declaration)
    {
        return declaration switch
        {
            Struct { IsUnion: true, } => "union",
            Struct => "struct",
            CstructEnum => "enum",
            Typedef => "typedef",
            Defines => "#define",
            _ => declaration.GetType().Name,
        };
    }

    /// <summary>Validates every lexical member scope represented by the parsed top-level declarations.</summary>
    public static void ValidateScopedMemberNames(IEnumerable<CStructElement> declarations)
    {
        foreach (CStructElement declaration in declarations)
        {
            switch (declaration)
            {
            case Struct strct:
                ValidateCompositeMemberNames(strct, strct.Name.Name);
                break;
            case CstructEnum enm:
                ValidateEnumMemberNames(enm);
                break;
            case Typedef { Struct: not null, } typedef:
                ValidateCompositeMemberNames(typedef.Struct, typedef.Struct.Name.Name);
                break;
            }
        }
    }

    /// <summary>Rejects a declaration name that would shadow a built-in primitive, character, or string codec.</summary>
    public static void ValidateBuiltInNameCollision(
        CStructElement declaration,
        IReadOnlyDictionary<string, Func<Stream, object>> fieldHandlers)
    {
        if (fieldHandlers.ContainsKey(declaration.Name.Name))
        {
            throw new CStructLayoutException(
                $"Global {GetDeclarationKind(declaration)} name '{declaration.Name.Name}' conflicts with a built-in codec.");
        }
    }

    /// <summary>
    ///     Rejects a duplicate name anywhere in a composite's flattened, transitively-promoted namespace (its own
    ///     fields plus every anonymous promoted member's own fields, recursively - LANG-14), then validates every
    ///     *named* nested scope independently, since a named nested struct keeps its own separate namespace.
    /// </summary>
    private static void ValidateCompositeMemberNames(Struct strct, string scopeName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectFlattenedNames(strct, names, scopeName, strct);

        foreach (Field field in strct.Fields)
        {
            if (field is Struct { Name.Name.Length: > 0, } nested)
            {
                ValidateCompositeMemberNames(nested, scopeName + "." + nested.Name.Name);
            }
        }
    }

    /// <summary>
    ///     Adds every own-field name in <paramref name="strct"/> to <paramref name="names"/>, recursing into an
    ///     anonymous promoted member's own fields (but not a named nested struct's, which keeps its own
    ///     independent namespace validated separately by the caller). Throws on the first duplicate found anywhere
    ///     in that flattened set, naming <paramref name="owningStruct"/> - the composite the collision becomes
    ///     visible in, not necessarily the one where either colliding declaration was written.
    /// </summary>
    private static void CollectFlattenedNames(Struct strct, HashSet<string> names, string scopeName, Struct owningStruct)
    {
        foreach (Field field in strct.Fields)
        {
            if (field is Struct { Name.Name.Length: 0, } promoted)
            {
                CollectFlattenedNames(promoted, names, scopeName, owningStruct);
                continue;
            }

            // An anonymous nonzero-width bitfield (LANG-17) has no name at all, so multiple of them are not
            // duplicates of each other.
            if (field.Name.Name.Length == 0)
            {
                continue;
            }

            if (!names.Add(field.Name.Name))
            {
                string memberKind = owningStruct.IsUnion ? "member" : "field";
                string declarationKind = owningStruct.IsUnion ? "union" : "struct";
                throw new CStructLayoutException(
                    $"Duplicate {memberKind} name '{field.Name.Name}' in {declarationKind} '{scopeName}'.");
            }
        }
    }

    /// <summary>Rejects duplicate constants inside one enum without leaking those names into another enum or global scope.</summary>
    private static void ValidateEnumMemberNames(CstructEnum enm)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (EnumValue value in enm.Values)
        {
            if (!names.Add(value.Name.Name))
            {
                throw new CStructLayoutException(
                    $"Duplicate enum member name '{value.Name.Name}' in enum '{enm.Name.Name}'.");
            }
        }
    }
}
