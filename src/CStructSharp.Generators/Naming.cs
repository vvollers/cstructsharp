namespace CStructSharp.Generators;

using System.Text;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
///     The naming rules: a layout identifier becomes a PascalCase C# name - split on underscores,
///     each part capitalized with the rest lower-cased when the part is all caps, digits kept, leading underscores
///     dropped - unless <c>KeepNames</c> keeps the spelling. A C# keyword that survives is escaped with <c>@</c>.
/// </summary>
internal static class Naming
{
    /// <summary>The C# identifier for a layout identifier.</summary>
    /// <param name="identifier">The identifier as the layout spells it.</param>
    /// <param name="keepNames">Whether to keep the spelling instead of converting it to PascalCase.</param>
    /// <returns>
    ///     A valid C# identifier: <c>_</c> for an empty result, prefixed with <c>_</c> when it starts with a digit, and
    ///     escaped with <c>@</c> when it is a keyword.
    /// </returns>
    public static string ToCSharp(string identifier, bool keepNames)
    {
        string name = keepNames ? identifier : ToPascalCase(identifier);
        if (name.Length == 0)
        {
            name = "_";
        }

        if (char.IsDigit(name[0]))
        {
            // A layout identifier may start with a digit inside an enum (`32BIT_MACHINE`); a C# one may not.
            name = "_" + name;
        }

        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.VarKeyword
                   ? "@" + name
                   : name;
    }

    /// <summary>PascalCase: <c>chunk_type</c> → <c>ChunkType</c>, <c>PNG_SIG</c> → <c>PngSig</c>, <c>iPhone</c> → <c>IPhone</c>, <c>_reserved</c> → <c>Reserved</c>.</summary>
    /// <param name="identifier">The identifier as the layout spells it.</param>
    /// <returns>The converted name, which may be empty or start with a digit.</returns>
    public static string ToPascalCase(string identifier)
    {
        var builder = new StringBuilder(identifier.Length);
        foreach (string rawPart in identifier.Split('_'))
        {
            if (rawPart.Length == 0)
            {
                continue;
            }

            bool allCaps = true;
            foreach (char character in rawPart)
            {
                if (char.IsLetter(character) && !char.IsUpper(character))
                {
                    allCaps = false;
                    break;
                }
            }

            builder.Append(char.ToUpperInvariant(rawPart[0]));
            string rest = rawPart.Substring(1);
            builder.Append(allCaps ? rest.ToLowerInvariant() : rest);
        }

        return builder.ToString();
    }
}
