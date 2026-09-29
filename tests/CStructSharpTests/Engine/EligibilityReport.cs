namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Diagnostics;
using CStructSharp.Introspection;

/// <summary>
///     The shared machinery of the engine's eligibility reports (<see cref="ReadProgramEligibilityTests"/>,
///     <see cref="WriteProgramEligibilityTests"/>): for every root of the repository's layout corpora, whether the compiled
///     engine has a program for it, rendered one line per root and compared with a list checked in beside the tests, so
///     the engine's coverage grows deliberately.
/// </summary>
/// <remarks>
///     When a change makes roots eligible (or ineligible, or changes a reason), a report test fails and lists every line
///     that differs. If the change is intended, run the test with <see cref="UpdateVariable"/> set to <c>1</c>, which
///     rewrites the list and passes, and review the list's diff before committing; an unset variable never writes.
/// </remarks>
internal static class EligibilityReport
{
    /// <summary>The environment variable that makes a report test rewrite its checked-in list instead of comparing with it.</summary>
    public const string UpdateVariable = "CSTRUCTSHARP_ENGINE_ELIGIBILITY_UPDATE";

    /// <summary>The report line of an eligible root.</summary>
    public const string Eligible = "eligible";

    /// <summary>
    ///     Computes the report over every corpus, writes its summary to the test output, and compares it with the checked-in
    ///     list at <paramref name="expectedPath"/> (or rewrites the list when <see cref="UpdateVariable"/> is set).
    /// </summary>
    /// <param name="expectedPath">The checked-in list.</param>
    /// <param name="title">The report's subject for the list's header, such as <c>read-program</c>.</param>
    /// <param name="testName">The test class that owns the list, named in its header.</param>
    /// <param name="outcome">Returns a root's eligibility: <see langword="null"/> when eligible, otherwise the reason.</param>
    public static void AssertMatchesCheckedInList(string expectedPath, string title, string testName, Func<CStruct, string, string?> outcome)
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
                actual[name + "/" + item.Id] = Describe(item, outcome);
            }
        }

        WriteSummary(actual);
        string rendered = Render(actual, title, testName);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(expectedPath, rendered);
            return;
        }

        SortedDictionary<string, string> expected = Parse(File.ReadAllText(expectedPath));
        string difference = Difference(expected, actual);
        Assert.AreEqual(
            string.Empty,
            difference,
            $"The engine's {title} eligibility changed. If intended, set {UpdateVariable}=1, rerun the test to rewrite {expectedPath}, and review the diff.\n{difference}");
    }

    /// <summary>Renders a report: a header comment, then one <c>corpus/id =&gt; value</c> line per root in ordinal order.</summary>
    /// <param name="entries">The report.</param>
    /// <param name="title">The report's subject, such as <c>read-program</c>.</param>
    /// <param name="testName">The test class that owns the list.</param>
    /// <returns>The text, with LF line endings.</returns>
    public static string Render(SortedDictionary<string, string> entries, string title, string testName)
    {
        var text = new StringBuilder();
        text.Append("# The compiled engine's ").Append(title).Append(" eligibility of every corpus root (").Append(testName).Append(").\n");
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
    public static SortedDictionary<string, string> Parse(string text)
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
    public static string Difference(SortedDictionary<string, string> expected, SortedDictionary<string, string> actual)
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

    /// <summary>Returns a case's report line: <see cref="Eligible"/>, or the first reason its root has no program.</summary>
    /// <param name="item">The corpus case.</param>
    /// <param name="outcome">Returns a root's eligibility: <see langword="null"/> when eligible, otherwise the reason.</param>
    /// <returns>The line's value.</returns>
    private static string Describe(EngineCorpusCase item, Func<CStruct, string, string?> outcome)
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
            return "(the root is a nested path, not a root the report classifies)";
        }

        return outcome(layout, segments[0].Name) ?? Eligible;
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
}
