// Checks documentation on changed named C# declarations using the same Roslyn version as generator tests.
// Usage: dotnet run --file tools/quality/CSharpComments.cs < changed-source.json
#:package Microsoft.CodeAnalysis.CSharp@4.8.0
#:property PublishAot=false

using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Syntax-only documentation checker; it never executes or compiles the inspected source.</summary>
internal static class CSharpComments
{
    /// <summary>Reads source/range entries from stdin and prints missing-comment locations as one JSON result line.</summary>
    /// <returns>Zero after inspecting valid input; malformed input throws and the process fails.</returns>
    public static int Main()
    {
        using JsonDocument input = JsonDocument.Parse(Console.In.ReadToEnd());
        var issues = new List<string>();
        foreach (JsonElement entry in input.RootElement.GetProperty("entries").EnumerateArray())
        {
            string source = entry.GetProperty("source").GetString()!;
            string file = entry.GetProperty("file").GetString()!;
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse));
            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (node is not (TypeDeclarationSyntax or BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or DelegateDeclarationSyntax))
                {
                    continue;
                }

                FileLinePositionSpan span = tree.GetLineSpan(node.FullSpan);
                int first = span.StartLinePosition.Line + 1;
                int last = span.EndLinePosition.Line + 1;
                bool changed = false;
                foreach (JsonElement range in entry.GetProperty("ranges").EnumerateArray())
                {
                    changed |= range.GetProperty("start").GetInt32() <= last && range.GetProperty("end").GetInt32() >= first;
                }

                if (changed && !HasDocumentation(node))
                {
                    int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    issues.Add($"{file}:{line}: {node.Kind()} needs a non-empty XML summary or accurate inheritdoc.");
                }
            }
        }

        Console.WriteLine("DOC-COMMENT-RESULT:" + JsonSerializer.Serialize(issues));
        return 0;
    }

    /// <summary>Recognizes a non-empty XML summary or explicit inherited documentation immediately on a declaration.</summary>
    /// <param name="node">The named type, method, constructor, operator or local function to inspect.</param>
    /// <returns>Whether documentation is present; contract correctness still needs human review.</returns>
    private static bool HasDocumentation(SyntaxNode node)
    {
        foreach (SyntaxTrivia trivia in node.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax documentation)
            {
                continue;
            }

            foreach (XmlNodeSyntax content in documentation.Content)
            {
                if (content is XmlEmptyElementSyntax empty && empty.Name.LocalName.Text == "inheritdoc")
                {
                    return true;
                }

                // Inspect token values, not exterior "///" trivia that can make an empty multiline summary look non-empty.
                if (content is XmlElementSyntax element && element.StartTag.Name.LocalName.Text == "summary" &&
                    element.Content.SelectMany(part => part.DescendantTokens()).Any(token => !string.IsNullOrWhiteSpace(token.ValueText)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
