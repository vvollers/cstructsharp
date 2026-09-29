namespace CStructSharp.Tests;

/// <summary>
///     The write eligibility report: for every root of the repository's layout corpora (<see cref="EngineCorpora"/>),
///     whether the compiled engine has a write program for it - so <c>Serialize</c> to an array or a span, and
///     <c>WriteAsync</c>, run on the engine - and if not, the first reason. The report is pinned in
///     <c>WriteProgramEligibility.txt</c> beside this file, so the writer's coverage grows deliberately, like the reader's
///     (<see cref="ReadProgramEligibilityTests"/>).
/// </summary>
/// <remarks>
///     <para>Regenerate the list after an intended change and review its diff before committing:</para>
///     <code>
///         CSTRUCTSHARP_ENGINE_ELIGIBILITY_UPDATE=1 dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --filter "FullyQualifiedName~WriteProgramEligibilityTests"
///     </code>
/// </remarks>
[TestClass]
public class WriteProgramEligibilityTests
{
    /// <summary>Gets the path of the checked-in list.</summary>
    private static string ExpectedPath => Path.Combine(TestFixtures.RepositoryRoot, "tests", "CStructSharpTests", "Engine", "WriteProgramEligibility.txt");

    /// <summary>
    ///     Every corpus root's write eligibility equals the checked-in list, line for line; the summary per corpus and the
    ///     most common reasons are written to the test output.
    /// </summary>
    [TestMethod]
    public void EligibilityReport_MatchesTheCheckedInList()
    {
        EligibilityReport.AssertMatchesCheckedInList(
            ExpectedPath,
            "write-program",
            nameof(WriteProgramEligibilityTests),
            (layout, root) => layout.Compilation.GetRootWriteProgram(root) is { IsEligible: false, } outcome ? outcome.Reason : null);
    }

    /// <summary>The checked-in list is exactly what the report renders, header included, so a regeneration changes only report lines.</summary>
    [TestMethod]
    public void CheckedInList_HasTheRenderedHeader()
    {
        string text = File.ReadAllText(ExpectedPath).ReplaceLineEndings("\n");
        Assert.AreEqual(EligibilityReport.Render(EligibilityReport.Parse(text), "write-program", nameof(WriteProgramEligibilityTests)), text);
    }
}
