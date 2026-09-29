namespace CStructSharp.Tests;

using System.Reflection;

/// <summary>
///     Hosts the assembly's golden hooks - every test runs inside a <see cref="GoldenScope"/>, and a recording run writes
///     the manifests when the assembly finishes - and checks the committed manifests and the golden machinery itself.
/// </summary>
/// <remarks>
///     The manifests under <c>Engine/Golden/</c> are regenerated only for an intended, explained behaviour change (see
///     CONTRIBUTING.md):
///     <code>
///         node tools/quality/engine-golden.mjs record
///     </code>
///     which runs the golden tests on net10.0 with <c>CSTRUCTSHARP_ENGINE_GOLDEN_RECORD=1</c> (the interpreter must agree
///     with the engine on every case), checks the new manifests on net8.0 and net10.0, and reports the changed files.
/// </remarks>
[TestClass]
public class GoldenManifestTests
{
    /// <summary>A small readable outcome with an empty line inside it.</summary>
    private const string Outcome = "result = StructValue {1}\n\nresult.a = Byte 1";

    /// <summary>Gets or sets the running test's context, which the test-id check reads.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Opens the golden scope of every test in the assembly before it runs.</summary>
    /// <param name="context">The test's context.</param>
    [GlobalTestInitialize]
    public static void BeginGoldenScope(TestContext context) => EngineGolden.Begin(context);

    /// <summary>
    ///     Closes the golden scope of every test after it ran: a passed test is verified against its committed section
    ///     (hashed groups, stale entries), or while recording its section is kept for the manifests.
    /// </summary>
    /// <param name="context">The test's context.</param>
    [GlobalTestCleanup]
    public static void EndGoldenScope(TestContext context) => EngineGolden.End(context);

    /// <summary>Writes the recorded manifests when a recording run finishes; an ordinary run writes nothing.</summary>
    [AssemblyCleanup]
    public static void WriteGoldenRecording()
    {
        if (EngineGolden.Recording)
        {
            EngineGolden.WriteRecording();
        }
    }

    /// <summary>
    ///     Every committed manifest belongs to a test class, is in its canonical form (line feeds only, the rendered
    ///     header, sections in order), and names only tests the class declares, so a renamed or deleted test cannot leave
    ///     an orphan section behind.
    /// </summary>
    [TestMethod]
    public void CommittedManifests_AreCanonicalAndNameDeclaredTests()
    {
        if (EngineGolden.Recording)
        {
            // A recording run rewrites the manifests after its tests; the next ordinary run checks what it wrote.
            return;
        }

        string[] files = Directory.GetFiles(GoldenManifest.Directory);
        Assert.IsNotEmpty(files, "no golden manifests");
        foreach (string file in files)
        {
            Assert.AreEqual(".txt", Path.GetExtension(file), file + " is not a manifest");
            string className = Path.GetFileNameWithoutExtension(file);
            Type? type = typeof(GoldenManifestTests).Assembly.GetType(typeof(GoldenManifestTests).Namespace + "." + className);
            Assert.IsNotNull(type?.GetCustomAttribute<TestClassAttribute>(), file + " names no test class");

            string text = File.ReadAllText(file);
            Assert.DoesNotContain("\r", text, file + " contains carriage returns");
            GoldenManifest manifest = GoldenManifest.Parse(className, text);
            Assert.AreEqual(manifest.Render(), text, file + " is not in its canonical form");
            Assert.IsNotEmpty(manifest.Sections, file + " is empty");

            HashSet<string> declared = GoldenTestIds.Declared(type!);
            string[] orphans = [.. manifest.Sections.Keys.Where(testId => !declared.Contains(testId))];
            Assert.IsEmpty(orphans, file + " has sections of tests the class does not declare: " + string.Join(", ", orphans));
        }
    }

    /// <summary>A manifest renders and parses back unchanged: readable outcomes with empty lines, empty outcomes, and hashed groups.</summary>
    [TestMethod]
    public void Manifest_RoundTripsItsFormat()
    {
        var manifest = new GoldenManifest(
            "Sample",
            [
                new GoldenSection("B(\"x\", 2)", true, [GoldenEntry.Hashed(string.Empty, new string('a', 64), 3), GoldenEntry.Hashed("count/packed", new string('0', 64), 1)]),
                new GoldenSection("A", false, [GoldenEntry.Readable("Parse rec (Span) (Fastest)", Outcome), GoldenEntry.Readable("empty", string.Empty)]),
            ]);
        string text = manifest.Render();
        StringAssert.Contains(text, "\n@test A\n@case Parse rec (Span) (Fastest)\n  result = StructValue {1}\n\n  result.a = Byte 1\n@case empty\n\n@test B(\"x\", 2)\n@hash ");
        StringAssert.Contains(text, "@hash " + new string('a', 64) + " 3\n@hash " + new string('0', 64) + " 1 count/packed\n");

        GoldenManifest parsed = GoldenManifest.Parse("Sample", text);
        Assert.AreEqual(text, parsed.Render());
        Assert.AreEqual(Outcome, parsed.Sections["A"].ByKey["Parse rec (Span) (Fastest)"].Text);
        Assert.AreEqual(3, parsed.Sections["B(\"x\", 2)"].ByKey[string.Empty].Count);
        Assert.Throws<FormatException>(() => GoldenManifest.Parse("Sample", "@case outside\n"));
        Assert.Throws<FormatException>(() => GoldenManifest.Parse("Sample", "@test A\n@hash 12 1\n"));
        Assert.Throws<FormatException>(() => GoldenManifest.Parse("Sample", "@test A\n@case x\n  a\r\n"));
    }

    /// <summary>
    ///     A readable reference reports a changed outcome with a line diff, an unknown key as missing, and an outcome the
    ///     test no longer checks as stale; repeated keys are numbered, and parts prefix the keys.
    /// </summary>
    [TestMethod]
    public void ReadableReference_ReportsChangedMissingAndStaleOutcomes()
    {
        var committed = new GoldenSection(
            "T",
            false,
            [GoldenEntry.Readable("op", "a = 1"), GoldenEntry.Readable("op #2", "a = 2"), GoldenEntry.Readable("variant / op", "b = 1"), GoldenEntry.Readable("gone", "x")]);
        using GoldenScope scope = Compare(committed);
        scope.Check("op", "a = 1\n");
        AssertFailedException changed = Assert.Throws<AssertFailedException>(() => scope.Check("op", "a = 3\r\n"));
        StringAssert.Contains(changed.Message, "op #2: the golden outcome (-) and the engine (+) differ:\n- a = 2\n+ a = 3\n");
        using (scope.Part("variant"))
        {
            scope.Check("op", "b = 1");
        }

        StringAssert.Contains(Assert.Throws<AssertFailedException>(() => scope.Check("new", "c")).Message, "no golden outcome for 'new'");
        StringAssert.Contains(Assert.Throws<AssertFailedException>(scope.Verify).Message, "stale golden entries the test no longer produces (record the golden outcomes again): 'gone'");
        using GoldenScope uncommitted = Compare(null);
        StringAssert.Contains(Assert.Throws<AssertFailedException>(() => uncommitted.Check("op", "a")).Message, "S.T has no golden outcomes");
    }

    /// <summary>
    ///     A recording keeps a small test readable and hashes a sweep or a large test per part; the recorded hashes verify
    ///     the same outcomes and report a changed outcome, and a group the test no longer reaches, when the test ends.
    /// </summary>
    [TestMethod]
    public void RecordedSections_VerifyTheOutcomesTheyRecorded()
    {
        using var small = new GoldenScope("S", "T", true, false, () => null);
        small.Check("op", Outcome);
        GoldenSection readable = small.Recorded()!;
        Assert.IsFalse(readable.Hashed);
        Assert.AreEqual(Outcome, readable.ByKey["op"].Text);
        using var idle = new GoldenScope("S", "T", true, false, () => null);
        Assert.IsNull(idle.Recorded(), "a test that checks nothing records nothing");
        using var sweep = new GoldenScope("S", "T", true, true, () => null);
        sweep.Check("op", Outcome);
        Assert.IsTrue(sweep.Recorded()!.Hashed, "a sweep is hashed however small it is");

        // Records the large outcomes of two parts, or compares them with a recorded section.
        void Large(GoldenScope scope, int changed = -1, int parts = 2)
        {
            for (int part = 0; part < parts; part++)
            {
                using (scope.Part("part " + part))
                {
                    for (int index = 0; index < 40; index++)
                    {
                        scope.Check("op", new string('x', 1000) + (part == 0 && index == changed ? "!" : string.Empty));
                    }
                }
            }
        }

        using var recording = new GoldenScope("S", "T", true, false, () => null);
        Large(recording);
        GoldenSection hashed = recording.Recorded()!;
        Assert.IsTrue(hashed.Hashed);
        CollectionAssert.AreEqual(new[] { "part 0", "part 1", }, hashed.Entries.Select(entry => entry.Key).ToArray());
        Assert.AreEqual(40, hashed.ByKey["part 1"].Count);

        using GoldenScope same = Compare(hashed);
        Large(same);
        same.Verify();

        using GoldenScope different = Compare(hashed);
        Large(different, changed: 7);
        string message = Assert.Throws<AssertFailedException>(different.Verify).Message;
        StringAssert.Contains(message, "the group 'part 0' differs from its golden outcomes: 40 outcomes hash to ");
        Assert.DoesNotContain("'part 1' differs", message);

        using GoldenScope fewer = Compare(hashed);
        Large(fewer, parts: 1);
        StringAssert.Contains(Assert.Throws<AssertFailedException>(fewer.Verify).Message, "stale golden entries the test no longer produces (record the golden outcomes again): 'part 1'");
    }

    /// <summary>The id of a running data-driven test is one its class declares, with its text argument quoted and escaped.</summary>
    /// <param name="text">A text argument with a quote and a backslash.</param>
    /// <param name="number">A number argument.</param>
    [TestMethod]
    [DataRow("a\"b\\c", -3)]
    public void RunningTestId_IsDeclared(string text, int number)
    {
        string id = GoldenTestIds.Of(this.TestContext);
        Assert.AreEqual("RunningTestId_IsDeclared(\"a\\\"b\\\\c\", -3)", id, text + number);
        Assert.Contains(id, GoldenTestIds.Declared(typeof(GoldenManifestTests)));
    }

    /// <summary>A scope that compares outcomes with <paramref name="committed"/>.</summary>
    /// <param name="committed">The committed section, or <see langword="null"/> for none.</param>
    /// <returns>The scope of test <c>S.T</c>.</returns>
    private static GoldenScope Compare(GoldenSection? committed) => new("S", "T", false, false, () => committed);
}
