namespace CStructSharp.Generators;

using System.Text;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
///     The naming rules (plan Appendix C): a layout identifier becomes a PascalCase C# name - split on underscores,
///     each part capitalized with the rest lower-cased when the part is all caps, digits kept, leading underscores
///     dropped - unless <c>KeepNames</c> keeps the spelling. A C# keyword that survives is escaped with <c>@</c>.
/// </summary>
internal static class Naming
{
    /// <summary>The C# identifier for a layout identifier.</summary>
    public static string ToCSharp(string identifier, bool keepNames)
    {
        string name = keepNames ? identifier : ToPascalCase(identifier);
        if (name.Length == 0)
        {
            name = "_";
        }

        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.VarKeyword
                   ? "@" + name
                   : name;
    }

    /// <summary>PascalCase per Appendix C: <c>chunk_type</c> → <c>ChunkType</c>, <c>PNG_SIG</c> → <c>PngSig</c>, <c>iPhone</c> → <c>IPhone</c>, <c>_reserved</c> → <c>Reserved</c>.</summary>
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
