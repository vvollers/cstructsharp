namespace CStructSharp.Tests;

/// <summary>
///     The read eligibility report: for every root of the repository's layout corpora (<see cref="EngineCorpora"/>: parity
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
    /// <summary>Gets the path of the checked-in list.</summary>
    private static string ExpectedPath => Path.Combine(TestFixtures.RepositoryRoot, "tests", "CStructSharpTests", "Engine", "ReadProgramEligibility.txt");

    /// <summary>
    ///     Every corpus root's eligibility equals the checked-in list, line for line; the summary per corpus and the most
    ///     common reasons are written to the test output.
    /// </summary>
    [TestMethod]
    public void EligibilityReport_MatchesTheCheckedInList()
    {
        EligibilityReport.AssertMatchesCheckedInList(
            ExpectedPath,
            "read-program",
            nameof(ReadProgramEligibilityTests),
            (layout, root) => layout.Compilation.GetRootReadProgram(root) is { IsEligible: false, } outcome ? outcome.Reason : null);
    }

    /// <summary>
    ///     The debug programs a debug parse runs are eligible for exactly the roots the read programs are, with the same
    ///     reasons, so the one checked-in list pins both.
    /// </summary>
    [TestMethod]
    public void DebugEligibility_MatchesTheCheckedInList()
    {
        EligibilityReport.AssertMatchesCheckedInList(
            ExpectedPath,
            "read-program",
            nameof(ReadProgramEligibilityTests),
            (layout, root) => layout.Compilation.GetRootDebugReadProgram(root) is { IsEligible: false, } outcome ? outcome.Reason : null);
    }

    /// <summary>The list parses what it renders, so an update round-trips.</summary>
    [TestMethod]
    public void EligibilityList_RoundTrips()
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a/x"] = EligibilityReport.Eligible, ["b/y: z"] = "s.f: why: nested", };
        CollectionAssert.AreEqual(entries, EligibilityReport.Parse(EligibilityReport.Render(entries, "read-program", nameof(ReadProgramEligibilityTests))));
        StringAssert.Contains(EligibilityReport.Difference(entries, new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a/x"] = "s.f: why" }), "- b/y: z");
    }

    /// <summary>The checked-in list is exactly what the report renders, header included, so a regeneration changes only report lines.</summary>
    [TestMethod]
    public void CheckedInList_HasTheRenderedHeader()
    {
        string text = File.ReadAllText(ExpectedPath).ReplaceLineEndings("\n");
        Assert.AreEqual(EligibilityReport.Render(EligibilityReport.Parse(text), "read-program", nameof(ReadProgramEligibilityTests)), text);
    }
}
