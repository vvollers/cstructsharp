namespace CStructSharp.Tests;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
///     The golden reference of the differential tests: the canonical outcomes the interpreter produced for every
///     comparison, committed as manifests under <c>Engine/Golden/</c> (<see cref="GoldenManifest"/>). A comparison calls
///     <see cref="Check"/> with its outcome; normally that compares the engine's outcome with the committed one, and with
///     <c>CSTRUCTSHARP_ENGINE_GOLDEN_RECORD=1</c> the harness runs the interpreter too, requires both to agree, and records
///     the interpreter's outcome as the new reference. With <c>CSTRUCTSHARP_ENGINE_GOLDEN_RECORD=compare</c> it runs and
///     compares the interpreter as well but checks the committed outcomes instead of recording them
///     (<see cref="ComparesInterpreter"/>).
/// </summary>
/// <remarks>
///     <para>
///         Every test runs inside a <see cref="GoldenScope"/> that the assembly's global test hooks open and close
///         (<see cref="GoldenManifestTests"/>). When a passed test ends, its scope is verified against the committed
///         section: hashed groups are compared, and committed outcomes the test no longer produced fail it as stale.
///         While recording, the passed tests' sections replace their old ones when the assembly finishes
///         (<see cref="WriteRecording"/>), and sections of tests that no longer exist are dropped.
///     </para>
///     <para>
///         Outcomes are compared after <see cref="Normalize"/> (line feeds only, no trailing line feed, assembly versions
///         masked), and the harness renders them under the invariant culture (<see cref="Invariant{T}"/>), so the
///         manifests are the same on every operating system, culture and target framework. A hashed group names no
///         failing outcome; to find it, run the test with <see cref="DumpVariable"/> set on a good and a bad build and
///         compare the files.
///     </para>
/// </remarks>
internal static partial class EngineGolden
{
    /// <summary>The environment variable that switches the harness to recording (value <c>1</c>).</summary>
    public const string RecordVariable = "CSTRUCTSHARP_ENGINE_GOLDEN_RECORD";

    /// <summary>
    ///     The environment variable that names a directory to write every hashed outcome to, one file per test and group,
    ///     so the outcomes behind a differing hash can be compared between two runs (two commits, two platforms).
    /// </summary>
    public const string DumpVariable = "CSTRUCTSHARP_ENGINE_GOLDEN_DUMP";

    /// <summary>The running test's scope.</summary>
    private static readonly AsyncLocal<GoldenScope?> Current = new();

    /// <summary>While recording, the section each passed test recorded (<see langword="null"/> for none), by class and test id.</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, GoldenSection?>> Recorded = new(StringComparer.Ordinal);

    /// <summary>Gets whether this run records the reference outcomes (<see cref="RecordVariable"/> is <c>1</c>).</summary>
    public static bool Recording { get; } = Environment.GetEnvironmentVariable(RecordVariable) == "1";

    /// <summary>
    ///     Gets whether the harness also runs the interpreter and requires it to agree with the engine: while recording,
    ///     and when <see cref="RecordVariable"/> is <c>compare</c>, which checks the committed outcomes as an ordinary run
    ///     does and records nothing (the coverage collection runs this way, so the interpreter stays exercised while it
    ///     is the golden reference).
    /// </summary>
    public static bool ComparesInterpreter { get; } = Recording || Environment.GetEnvironmentVariable(RecordVariable) == "compare";

    /// <summary>Gets the directory hashed outcomes are written to (<see cref="DumpVariable"/>), or <see langword="null"/> for none.</summary>
    public static string? DumpDirectory { get; } = Environment.GetEnvironmentVariable(DumpVariable) is { Length: > 0, } directory ? directory : null;

    /// <summary>Opens the golden scope of a test that is about to run.</summary>
    /// <param name="context">The test's context.</param>
    public static void Begin(TestContext context)
    {
        string className = ClassName(context);
        string testId = GoldenTestIds.Of(context);
        bool sweep = GoldenTestIds.IsSweep(context);
        Current.Value = new GoldenScope(className, testId, Recording, sweep, () => GoldenManifest.Committed(className)?.Sections.GetValueOrDefault(testId));
    }

    /// <summary>
    ///     Closes the golden scope of a test that has run: while recording, keeps a passed test's section for
    ///     <see cref="WriteRecording"/>; otherwise verifies a passed test against its committed section.
    /// </summary>
    /// <param name="context">The test's context.</param>
    /// <exception cref="AssertFailedException">A hashed group differs, or the committed section holds stale entries.</exception>
    public static void End(TestContext context)
    {
        using GoldenScope? scope = Current.Value;
        Current.Value = null;
        if (scope is null || context.CurrentTestOutcome != UnitTestOutcome.Passed)
        {
            return;
        }

        if (Recording)
        {
            Recorded.GetOrAdd(context.FullyQualifiedTestClassName ?? scope.ClassName, _ => new ConcurrentDictionary<string, GoldenSection?>(StringComparer.Ordinal))[scope.TestId] = scope.Recorded();
            return;
        }

        if (scope.Used || GoldenManifest.Committed(scope.ClassName) is not null)
        {
            scope.Verify();
        }
    }

    /// <summary>
    ///     Checks one outcome of the running test against the golden reference, or records it as the reference while
    ///     recording (see <see cref="GoldenScope.Check"/>).
    /// </summary>
    /// <param name="key">The outcome's key within the current part: one line, such as an operation's name.</param>
    /// <param name="outcome">The outcome's canonical rendering.</param>
    /// <exception cref="AssertFailedException">The outcome differs from the committed one, or none is committed.</exception>
    /// <exception cref="InvalidOperationException">No test is running on this flow.</exception>
    public static void Check(string key, string outcome) => Scope().Check(key, outcome);

    /// <summary>Enters a named part of the running test, which keys and groups the outcomes checked inside it.</summary>
    /// <param name="name">The part's name: one line, without surrounding whitespace.</param>
    /// <returns>The handle that leaves the part.</returns>
    /// <exception cref="InvalidOperationException">No test is running on this flow.</exception>
    public static IDisposable Part(string name) => Scope().Part(name);

    /// <summary>
    ///     Normalizes an outcome for storage and comparison: line feeds only, without trailing line feeds, and every
    ///     assembly version in an assembly-qualified type name masked as <c>Version=*</c>. A message that names a generic
    ///     type spells its arguments with their assembly's version, which differs between the target frameworks (the
    ///     runtime library's) and between releases (the library's own).
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The normalized outcome.</returns>
    public static string Normalize(string outcome)
        => AssemblyVersion().Replace(outcome.Replace("\r\n", "\n", StringComparison.Ordinal), "Version=*").TrimEnd('\n');

    /// <summary>
    ///     Runs <paramref name="render"/> under the invariant culture for both formatting and resource lookup, so numbers
    ///     and system messages in an outcome do not depend on the machine's culture.
    /// </summary>
    /// <typeparam name="T">The rendering's type.</typeparam>
    /// <param name="render">Produces the rendering.</param>
    /// <returns>The rendering.</returns>
    public static T Invariant<T>(Func<T> render)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo interfaceCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = interfaceCulture;
        }
    }

    /// <summary>
    ///     After a recording run, rewrites the manifest of every class that recorded a section or has a committed one:
    ///     the passed tests' sections replace their old ones (a test that checked nothing loses its section), sections of
    ///     tests the class no longer declares are dropped, and a manifest left empty is deleted. A test that failed keeps
    ///     its old section. Each file is replaced whole, so a concurrent reader never sees half of one.
    /// </summary>
    public static void WriteRecording()
    {
        foreach ((string fullName, ConcurrentDictionary<string, GoldenSection?> tests) in Recorded)
        {
            string className = fullName[(fullName.LastIndexOf('.') + 1)..];
            GoldenManifest? existing = GoldenManifest.Load(className);
            if (existing is null && tests.Values.All(section => section is null))
            {
                continue;
            }

            GoldenManifest manifest = existing ?? new GoldenManifest(className, []);
            foreach ((string testId, GoldenSection? section) in tests)
            {
                if (section is null)
                {
                    manifest.Sections.Remove(testId);
                }
                else
                {
                    manifest.Sections[testId] = section;
                }
            }

            Type type = typeof(EngineGolden).Assembly.GetType(fullName) ?? throw new InvalidOperationException("No test class " + fullName + ".");
            HashSet<string> declared = GoldenTestIds.Declared(type);
            foreach (string stale in manifest.Sections.Keys.Where(testId => !declared.Contains(testId)).ToList())
            {
                manifest.Sections.Remove(stale);
            }

            string path = GoldenManifest.PathOf(className);
            if (manifest.Sections.Count == 0)
            {
                File.Delete(path);
                continue;
            }

            string text = manifest.Render();
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
            {
                continue;
            }

            System.IO.Directory.CreateDirectory(GoldenManifest.Directory);
            string temporary = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
    }

    /// <summary>Matches the assembly version of an assembly-qualified type name, such as <c>Version=10.0.0.0</c>.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"Version=\d+(\.\d+){1,3}(?=, Culture=)", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyVersion();

    /// <summary>Returns the running test's scope.</summary>
    /// <returns>The scope.</returns>
    /// <exception cref="InvalidOperationException">No test is running on this flow.</exception>
    private static GoldenScope Scope() => Current.Value ?? throw new InvalidOperationException("A golden outcome is checked outside a running test.");

    /// <summary>Returns the running test's class name without its namespace.</summary>
    /// <param name="context">The test's context.</param>
    /// <returns>The class name.</returns>
    private static string ClassName(TestContext context)
    {
        string fullName = context.FullyQualifiedTestClassName ?? string.Empty;
        return fullName[(fullName.LastIndexOf('.') + 1)..];
    }
}
