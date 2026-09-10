namespace CStructSharp.Fuzzing;

using System.Globalization;
using System.Text.Json;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    internal static int Run(string[] args)
    {
        Dictionary<string, string?> options = ParseOptions(args);
        if (options.ContainsKey("--help"))
        {
            WriteUsage();
            return 0;
        }

        if (options.ContainsKey("--list-targets"))
        {
            foreach (string target in FuzzTargets.Names)
            {
                Console.WriteLine(target);
            }

            return 0;
        }

        string corpusPath = GetOption(
            options,
            "--corpus",
            Path.Combine(AppContext.BaseDirectory, "corpus", "fuzz-corpus.json"));
        string targetName = GetOption(options, "--target", "all");
        FuzzCorpus corpus = FuzzCorpus.Load(corpusPath);
        var session = new FuzzSession(corpus);
        FuzzReport report;

        if (options.TryGetValue("--input", out string? inputPath))
        {
            if (targetName == "all")
            {
                throw new ArgumentException("--input requires one explicit --target.");
            }

            report = session.RunSingle(targetName, File.ReadAllBytes(inputPath!));
        }
        else
        {
            int? iterations = ParseOptionalInt(options, "--iterations");
            int? maxInputBytes = ParseOptionalInt(options, "--max-input-bytes");
            ulong? seed = ParseOptionalSeed(options, "--seed");
            report = session.Run(targetName, iterations, seed, maxInputBytes);
        }

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (options.TryGetValue("--report", out string? reportPath))
        {
            string? directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath!, json + Environment.NewLine);
        }

        Console.WriteLine(json);
        return 0;
    }

    private static Dictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index++)
        {
            string name = args[index];
            if (name is "--help" or "--list-targets")
            {
                options.Add(name, null);
                continue;
            }

            if (name is not ("--corpus" or "--target" or "--iterations" or "--seed" or
                "--max-input-bytes" or "--input" or "--report"))
            {
                throw new ArgumentException($"Unknown managed fuzz option '{name}'.");
            }

            if (++index >= args.Length)
            {
                throw new ArgumentException($"Managed fuzz option '{name}' requires a value.");
            }

            options.Add(name, args[index]);
        }

        return options;
    }

    private static string GetOption(
        IReadOnlyDictionary<string, string?> options,
        string name,
        string defaultValue)
    {
        return options.TryGetValue(name, out string? value) ? value! : defaultValue;
    }

    private static int? ParseOptionalInt(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        if (!options.TryGetValue(name, out string? value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
        {
            throw new ArgumentException($"Managed fuzz option '{name}' is not a decimal Int32.");
        }

        return parsed;
    }

    private static ulong? ParseOptionalSeed(
        IReadOnlyDictionary<string, string?> options,
        string name)
    {
        if (!options.TryGetValue(name, out string? value))
        {
            return null;
        }

        ReadOnlySpan<char> digits = value.AsSpan();
        if (digits.StartsWith("0x", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (!ulong.TryParse(
                digits,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out ulong parsed))
        {
            throw new ArgumentException($"Managed fuzz option '{name}' is not a hexadecimal UInt64.");
        }

        return parsed;
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            CStructSharp bounded managed fuzz harness

            --target <all|definition|expression|path|binary-roundtrip|pointer-union>
            --corpus <fuzz-corpus.json>
            --iterations <mutations-per-target>
            --seed <hex-UInt64>
            --max-input-bytes <positive-int>
            --input <single-input-file>   Requires one explicit target.
            --report <output-json>
            --list-targets
            --help
            """);
    }
}
