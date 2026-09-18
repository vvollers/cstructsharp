namespace CStructSharp.FixtureTool;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CStructSharp.Diagnostics;

/// <summary>
///     fill   — parse every fixture with the managed library and record the canonical expected JSON (or its SHA-256
///              when larger than 64 KiB), the expected exception type for malformed inputs, and the consumed length.
///     verify — recompute and compare against the recorded expectations; exit 1 on any difference.
/// </summary>
internal static class Program
{
    private const int InlineExpectedLimit = 64 * 1024;

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
                Outcome outcome = Evaluate(directory, fixture);
                if (fill)
                {
                    Apply(fixture, outcome);
                    string path = Path.Combine(directory, "cases", fixture.Id + ".json");
                    File.WriteAllText(path, JsonSerializer.Serialize(fixture, FixtureDocument.SerializerOptions) + "\n");
                    Console.WriteLine($"{fixture.Id}: {outcome.Describe()}");
                }
                else
                {
                    string? mismatch = Compare(fixture, outcome);
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

    private static Outcome Evaluate(string directory, FixtureDocument fixture)
    {
        if (fixture.Definitions is { Count: > 0 })
        {
            foreach (string definition in fixture.Definitions)
            {
                _ = new CStruct(definition, fixture.Options.PointerSize, fixture.Options.Aligned, fixture.Options.LittleEndian);
            }

            return new Outcome(null, null, null, null);
        }

        CStruct layout = FixtureLoader.CreateLayout(fixture);
        if (fixture.Bytes is null)
        {
            return new Outcome(null, null, null, null);
        }

        byte[] bytes = FixtureLoader.MaterializeBytes(directory, fixture);
        ReadOptions options = FixtureLoader.CreateReadOptions(fixture);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            object result = layout.Parse(stream, fixture.Root, fixture.Variables, options);
            long consumed = stream.Position;
            string json = CanonicalJson.Serialize(result);
            return new Outcome(json, null, consumed, bytes.Length);
        }
        catch (CStructException exception)
        {
            return new Outcome(null, exception.GetType().Name, null, bytes.Length);
        }
    }

    private static void Apply(FixtureDocument fixture, Outcome outcome)
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
        fixture.ExpectedSha256 = Sha256(outcome.Json);
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

    private static string? Compare(FixtureDocument fixture, Outcome outcome)
    {
        if (!string.Equals(fixture.ExpectedError, outcome.ErrorType, StringComparison.Ordinal))
        {
            return $"expected error '{fixture.ExpectedError}', got '{outcome.ErrorType}'";
        }

        if (outcome.Json is null)
        {
            return null;
        }

        string sha = Sha256(outcome.Json);
        if (!string.Equals(fixture.ExpectedSha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            return $"expected SHA-256 {fixture.ExpectedSha256}, got {sha}";
        }

        return null;
    }

    private static string Sha256(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private sealed record Outcome(string? Json, string? ErrorType, long? Consumed, long? Length)
    {
        public string Describe()
        {
            if (this.ErrorType is not null)
            {
                return "throws " + this.ErrorType;
            }

            if (this.Json is null)
            {
                return "compiled";
            }

            return $"{this.Json.Length} JSON chars, consumed {this.Consumed}/{this.Length}";
        }
    }
}
