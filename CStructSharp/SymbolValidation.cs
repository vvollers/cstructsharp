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

    /// <summary>Rejects two fields in one composite scope and then validates every nested inline scope independently.</summary>
    private static void ValidateCompositeMemberNames(Struct strct, string scopeName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Field field in strct.Fields)
        {
            // An anonymous nonzero-width bitfield (LANG-17) has no name at all, so multiple of them in one
            // composite are not duplicates of each other.
            if (field.Name.Name.Length == 0)
            {
                continue;
            }

            if (!names.Add(field.Name.Name))
            {
                string memberKind = strct.IsUnion ? "member" : "field";
                string declarationKind = strct.IsUnion ? "union" : "struct";
                throw new CStructLayoutException(
                    $"Duplicate {memberKind} name '{field.Name.Name}' in {declarationKind} '{scopeName}'.");
            }
        }

        foreach (Field field in strct.Fields)
        {
            if (field is Struct nested)
            {
                ValidateCompositeMemberNames(nested, scopeName + "." + nested.Name.Name);
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
