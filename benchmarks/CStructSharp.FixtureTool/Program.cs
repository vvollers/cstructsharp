namespace CStructSharp.FixtureTool;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
///     fill   — parse every fixture with the managed library and record the canonical expected JSON (or its SHA-256
///              when larger than 64 KiB), the expected exception type for malformed inputs, and the consumed length.
///     verify — recompute and compare against the recorded expectations (<see cref="FixtureVerification"/>, which
///              the managed test suite applies as well); exit 1 on any difference.
/// </summary>
internal static class Program
{
    private const int InlineExpectedLimit = 64 * 1024;

    /// <summary>
    ///     Fills or verifies every fixture under <c>cases/</c> in the fixture directory, printing one line per fixture
    ///     and a final count. In <c>fill</c> mode each fixture's JSON file is rewritten with its computed expectations.
    /// </summary>
    /// <param name="args">
    ///     The mode (<c>fill</c> or <c>verify</c>), then optionally the fixture directory; without it, the directory is
    ///     searched for from the current directory.
    /// </param>
    /// <returns>0 when every fixture succeeds, 1 when any fails or mismatches, and 2 for invalid usage.</returns>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("fill" or "verify"))
        {
            Console.Error.WriteLine("Usage: CStructSharp.FixtureTool (fill|verify) [fixtureDirectory]");
            return 2;
        }

        bool fill = args[0] == "fill";
        string directory = args.Length > 1 ? Path.GetFullPath(args[1]) : FixtureLoader.FindFixtureDirectory(Directory.GetCurrentDirectory());
        int failures = 0;
        int count = 0;
        foreach (FixtureDocument fixture in FixtureLoader.LoadAll(directory))
        {
            count++;
            try
            {
                FixtureOutcome outcome = FixtureVerification.Evaluate(directory, fixture);
                if (fill)
                {
                    Apply(fixture, outcome);
                    string path = Path.Combine(directory, "cases", fixture.Id + ".json");
                    File.WriteAllText(path, JsonSerializer.Serialize(fixture, FixtureDocument.SerializerOptions) + "\n");
                    Console.WriteLine($"{fixture.Id}: {outcome.Describe()}");
                }
                else
                {
                    string? mismatch = FixtureVerification.Compare(fixture, outcome);
                    if (mismatch is null)
                    {
                        Console.WriteLine($"{fixture.Id}: ok");
                    }
                    else
                    {
                        failures++;
                        Console.Error.WriteLine($"{fixture.Id}: MISMATCH {mismatch}");
                    }
                }
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"{fixture.Id}: ERROR {exception.GetType().Name}: {exception.Message}");
            }
        }

        Console.WriteLine($"{count} fixtures, {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    ///     Records an outcome as the fixture's expectations: the exception type, or the canonical JSON's hash and length
    ///     with the value itself when it fits inline. Warns when a successful read left bytes unconsumed and the fixture
    ///     does not allow it.
    /// </summary>
    /// <param name="fixture">The fixture, updated in place.</param>
    /// <param name="outcome">The outcome computed for it.</param>
    private static void Apply(FixtureDocument fixture, FixtureOutcome outcome)
    {
        fixture.ExpectedError = outcome.ErrorType;
        if (outcome.Json is null)
        {
            fixture.Expected = null;
            fixture.ExpectedSha256 = null;
            fixture.ExpectedJsonLength = null;
            return;
        }

        fixture.ExpectedJsonLength = outcome.Json.Length;
        fixture.ExpectedSha256 = FixtureVerification.Sha256(outcome.Json);
        fixture.Expected = outcome.Json.Length <= InlineExpectedLimit
                               ? JsonNode.Parse(outcome.Json, documentOptions: new JsonDocumentOptions { MaxDepth = 4096 })
                               : null;
        if (outcome.Consumed is long consumed && outcome.Length is long length && consumed != length &&
            !fixture.Scenario.Equals("S-POINTER", StringComparison.Ordinal) &&
            !fixture.Tags.Contains("partial-consume"))
        {
            Console.Error.WriteLine($"  warning: {fixture.Id} consumed {consumed} of {length} bytes");
        }
    }
}
