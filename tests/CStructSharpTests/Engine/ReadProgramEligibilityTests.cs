namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Introspection;

/// <summary>
///     The eligibility report: for every root of the repository's layout corpora (<see cref="EngineCorpora"/>: parity
///     layouts, benchmark fixtures, manual and Portable fixtures, well-known formats, the inspector catalog), whether the
///     compiled engine has a read program for it, and if not, the first reason. The report is pinned in
///     <c>ReadProgramEligibility.txt</c> beside this file, so the engine's coverage grows deliberately, stage by stage.
/// </summary>
/// <remarks>
///     <para>
///         When a change makes roots eligible (or ineligible, or changes a reason), the test fails and lists every line
///         that differs. If the change is intended, regenerate the list and review its diff before committing:
///     </para>
///     <code>
///         CSTRUCTSHARP_ENGINE_ELIGIBILITY_UPDATE=1 dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --filter "FullyQualifiedName~ReadProgramEligibilityTests"
///     </code>
///     <para>
///         The run with the variable set rewrites the file and passes; an unset variable never writes. A root whose layout
///         does not compile, or which has no struct to read, is listed too, so a corpus change shows up here as well.
///     </para>
/// </remarks>
[TestClass]
public class ReadProgramEligibilityTests
{
    /// <summary>The environment variable that makes the test rewrite the checked-in list instead of comparing with it.</summary>
    private const string UpdateVariable = "CSTRUCTSHARP_ENGINE_ELIGIBILITY_UPDATE";

    /// <summary>The report line of an eligible root.</summary>
    private const string Eligible = "eligible";

    /// <summary>Gets the path of the checked-in list.</summary>
    private static string ExpectedPath => Path.Combine(TestFixtures.RepositoryRoot, "tests", "CStructSharpTests", "Engine", "ReadProgramEligibility.txt");

    /// <summary>
    ///     Every corpus root's eligibility equals the checked-in list, line for line; the summary per corpus and the most
    ///     common reasons are written to the test output.
    /// </summary>
    [TestMethod]
    public void EligibilityReport_MatchesTheCheckedInList()
    {
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        (string Name, IReadOnlyDictionary<string, EngineCorpusCase> Cases)[] corpora =
        [
            ("parity", EngineCorpora.Parity),
            ("benchmarks", EngineCorpora.Benchmarks),
            ("manual", EngineCorpora.Manual),
            ("portable", EngineCorpora.Portable),
            ("well-known", EngineCorpora.WellKnown),
            ("inspector", EngineCorpora.Inspector),
        ];
        foreach ((string name, IReadOnlyDictionary<string, EngineCorpusCase> cases) in corpora)
        {
            foreach (EngineCorpusCase item in cases.Values)
            {
                actual[name + "/" + item.Id] = Describe(item);
            }
        }

        WriteSummary(actual);
        string rendered = Render(actual);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(ExpectedPath, rendered);
            return;
        }

        SortedDictionary<string, string> expected = Parse(File.ReadAllText(ExpectedPath));
        string difference = Difference(expected, actual);
        Assert.AreEqual(
            string.Empty,
            difference,
            $"The engine's read-program eligibility changed. If intended, set {UpdateVariable}=1, rerun this test to rewrite {ExpectedPath}, and review the diff.\n{difference}");
    }

    /// <summary>The list parses what it renders, so an update round-trips.</summary>
    [TestMethod]
    public void EligibilityList_RoundTrips()
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a/x"] = Eligible, ["b/y: z"] = "s.f: why: nested", };
        CollectionAssert.AreEqual(entries, Parse(Render(entries)));
        StringAssert.Contains(Difference(entries, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a/x"] = "s.f: why" }), "- b/y: z");
    }

    /// <summary>Returns a case's report line: <see cref="Eligible"/>, or the first reason its root has no program.</summary>
    /// <param name="item">The corpus case.</param>
    /// <returns>The line's value.</returns>
    private static string Describe(EngineCorpusCase item)
    {
        CStruct layout;
        try
        {
            layout = item.Compile();
        }
        catch (CStructLayoutException)
        {
            return "(the layout does not compile)";
        }

        string? root = item.Root ?? layout.Layout.Declarations.FirstOrDefault(declaration => declaration.Kind is LayoutDeclarationKind.Struct or LayoutDeclarationKind.Union)?.Name;
        if (root is null)
        {
            return "(the layout has no struct or union to read)";
        }

        IReadOnlyList<PathSegment> segments;
        try
        {
            segments = layout.ParsePath(root);
        }
        catch (CStructPathException)
        {
            return "(the root is not a valid path)";
        }

        if (segments.Count != 1 || segments[0].Indexes.Count > 0)
        {
            return "(the root is a nested path, stage 8)";
        }

        ReadProgramOutcome outcome = layout.Compilation.GetRootReadProgram(segments[0].Name);
        return outcome.IsEligible ? Eligible : outcome.Reason!;
    }

    /// <summary>Writes the eligible count per corpus and the most common reasons (without their locations) to the test output.</summary>
    /// <param name="entries">The report.</param>
    private static void WriteSummary(SortedDictionary<string, string> entries)
    {
        foreach (IGrouping<string, KeyValuePair<string, string>> corpus in entries.GroupBy(entry => entry.Key[..entry.Key.IndexOf('/', StringComparison.Ordinal)]))
        {
            Console.WriteLine($"{corpus.Key}: {corpus.Count(entry => entry.Value == Eligible)} / {corpus.Count()} eligible");
        }

        IEnumerable<IGrouping<string, KeyValuePair<string, string>>> reasons = entries.Where(entry => entry.Value != Eligible).
            GroupBy(entry => entry.Value[(entry.Value.LastIndexOf(": ", StringComparison.Ordinal) + 1)..].Trim()).
            OrderByDescending(group => group.Count()).
            ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (IGrouping<string, KeyValuePair<string, string>> reason in reasons)
        {
            Console.WriteLine($"  {reason.Count(),4} x {reason.Key}");
        }
    }

    /// <summary>Renders the report: a header comment, then one <c>corpus/id =&gt; value</c> line per root in ordinal order.</summary>
    /// <param name="entries">The report.</param>
    /// <returns>The text, with LF line endings.</returns>
    private static string Render(SortedDictionary<string, string> entries)
    {
        var text = new StringBuilder();
        text.Append("# The compiled engine's read-program eligibility of every corpus root (ReadProgramEligibilityTests).\n");
        text.Append("# Each line is `corpus/id => eligible` or `corpus/id => first reason`. Regenerate with\n");
        text.Append("# CSTRUCTSHARP_ENGINE_ELIGIBILITY_UPDATE=1 (see the test class) and review the diff.\n");
        foreach (KeyValuePair<string, string> entry in entries)
        {
            text.Append(entry.Key).Append(" => ").Append(entry.Value).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Parses a rendered report.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The entries.</returns>
    private static SortedDictionary<string, string> Parse(string text)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int arrow = line.IndexOf(" => ", StringComparison.Ordinal);
            entries.Add(line[..arrow], line[(arrow + 4)..]);
        }

        return entries;
    }

    /// <summary>Lists the lines that differ: removed (<c>-</c>), added (<c>+</c>), and changed (both).</summary>
    /// <param name="expected">The checked-in report.</param>
    /// <param name="actual">The computed report.</param>
    /// <returns>The difference, one line each; empty when the reports are equal.</returns>
    private static string Difference(SortedDictionary<string, string> expected, SortedDictionary<string, string> actual)
    {
        var lines = new List<string>();
        foreach (string key in expected.Keys.Union(actual.Keys).Order(StringComparer.Ordinal))
        {
            bool had = expected.TryGetValue(key, out string? before);
            bool has = actual.TryGetValue(key, out string? after);
            if (had && has && before == after)
            {
                continue;
            }

            if (had)
            {
                lines.Add("- " + key + " => " + before);
            }

            if (has)
            {
                lines.Add("+ " + key + " => " + after);
            }
        }

        return string.Join("\n", lines);
    }
}
