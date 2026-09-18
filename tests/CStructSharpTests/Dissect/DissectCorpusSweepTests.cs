namespace CStructSharp.Tests.Dissect;

using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>
///     Opt-in progress meter for dissect.cstruct parity: compiles every definition string extracted from the dissect
///     ecosystem (see <c>tools/quality/extract-dissect-corpus.mjs</c>) and compares the outcome against a recorded
///     status file so a definition that compiled before can never silently stop compiling.
/// </summary>
/// <remarks>
///     The corpus is not part of the repository (it is Apache-2.0 source owned by the dissect project), so the test is
///     inconclusive unless <c>CSTRUCTSHARP_DISSECT_CORPUS</c> names the extracted JSON file. The status file defaults
///     to <c>corpus-status.json</c> next to the corpus and is rewritten as <c>corpus-status.latest.json</c> on every
///     run; setting <c>CSTRUCTSHARP_DISSECT_CORPUS_RATCHET=1</c> also replaces the recorded status with the latest one.
/// </remarks>
[TestClass]
public class DissectCorpusSweepTests
{
    private static readonly string[] EcosystemCustomTypeNames = ["varint", "EverythingVarInt", "BeaconCallback", "Elf_Type",];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    ///     Every corpus definition that compiled when the status file was recorded still compiles; newly compiling
    ///     definitions are reported so the status can be ratcheted forward.
    /// </summary>
    [TestMethod]
    public void Corpus_NeverRegresses()
    {
        string? corpusPath = Environment.GetEnvironmentVariable("CSTRUCTSHARP_DISSECT_CORPUS");
        if (string.IsNullOrEmpty(corpusPath) || !File.Exists(corpusPath))
        {
            Assert.Inconclusive("Set CSTRUCTSHARP_DISSECT_CORPUS to the JSON file written by tools/quality/extract-dissect-corpus.mjs.");
            return;
        }

        List<CorpusEntry> corpus = JsonSerializer.Deserialize<List<CorpusEntry>>(File.ReadAllText(corpusPath), JsonOptions) ?? [];
        Assert.IsNotEmpty(corpus, "corpus entries");

        string statusPath = Environment.GetEnvironmentVariable("CSTRUCTSHARP_DISSECT_CORPUS_STATUS") ??
                            Path.Combine(Path.GetDirectoryName(corpusPath)!, "corpus-status.json");
        Dictionary<string, string> recorded = File.Exists(statusPath)
                                                  ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(statusPath), JsonOptions) ?? []
                                                  : [];

        var latest = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var regressions = new List<string>();
        var improvements = new List<string>();
        int passing = 0;
        var preludes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CorpusEntry entry in corpus.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Line))
        {
            // A module loads its definitions into one cstruct instance in order, so a later definition may use the
            // types of an earlier one in the same file: the earlier definitions that compiled are its prelude.
            preludes.TryGetValue(entry.Path, out string? prelude);
            string outcome = Compile(entry.Source, prelude);
            if (outcome == "ok")
            {
                preludes[entry.Path] = prelude is null ? entry.Source : prelude + "\n" + entry.Source;
            }

            latest[entry.Id] = outcome;
            bool ok = outcome == "ok";
            if (ok)
            {
                passing++;
            }

            bool wasOk = recorded.TryGetValue(entry.Id, out string? previous) && previous == "ok";
            if (wasOk && !ok)
            {
                regressions.Add($"{entry.Id}: {outcome}");
            }
            else if (!wasOk && ok && recorded.Count > 0)
            {
                improvements.Add(entry.Id);
            }
        }

        string latestPath = Path.Combine(Path.GetDirectoryName(statusPath)!, "corpus-status.latest.json");
        File.WriteAllText(latestPath, JsonSerializer.Serialize(latest, JsonOptions));
        if (Environment.GetEnvironmentVariable("CSTRUCTSHARP_DISSECT_CORPUS_RATCHET") == "1")
        {
            File.WriteAllText(statusPath, JsonSerializer.Serialize(latest, JsonOptions));
        }

        Console.WriteLine($"dissect corpus: {passing}/{corpus.Count} definitions compile; {improvements.Count} newly passing; latest status written to {latestPath}");
        foreach (string improvement in improvements)
        {
            Console.WriteLine("  newly passing: " + improvement);
        }

        var failures = latest.Where(pair => pair.Value != "ok").GroupBy(pair => Classify(pair.Value)).OrderByDescending(group => group.Count());
        foreach (IGrouping<string, KeyValuePair<string, string>> group in failures)
        {
            Console.WriteLine($"  {group.Count(),3} x {group.Key}");
        }

        Assert.IsEmpty(regressions, "definitions that compiled before no longer compile:" + Environment.NewLine + string.Join(Environment.NewLine, regressions));
    }

    private static string Compile(string source, string? prelude)
    {
        // The types the ecosystem registers from Python (`cs.add_custom_type`) before loading a definition; the
        // sweep measures language coverage, so it supplies the same names as opaque codecs unless the definition
        // (or its prelude) declares the name itself.
        string context = prelude is null ? source : prelude + "\n" + source;
        ICustomCodec[] codecs = EcosystemCustomTypeNames.
            Where(name => !Regex.IsMatch(context, @"\b(struct|union|enum|flag|typedef\s+\S+)\s+" + Regex.Escape(name) + @"\b")).
            Select(name => (ICustomCodec)new StubCodec(name)).
            ToArray();
        try
        {
            _ = new CStruct(source, compilationOptions: new CStructCompilationOptions { Codecs = codecs, Prelude = prelude, });
            return "ok";
        }
        catch (CStructException exception)
        {
            string message = exception.Message;
            int newline = message.IndexOf('\n');
            return exception.GetType().Name + ": " + (newline < 0 ? message : message[..newline]);
        }
    }

    /// <summary>Collapses a failure message to its shape (the identifier that failed is replaced) so failures can be counted by cause.</summary>
    private static string Classify(string outcome)
    {
        int colon = outcome.IndexOf(": ", StringComparison.Ordinal);
        string text = colon < 0 ? outcome : outcome[(colon + 2)..];
        int detail = text.IndexOf(':');
        if (detail > 0)
        {
            text = text[..detail];
        }

        int at = text.IndexOf(" at line", StringComparison.Ordinal);
        if (at > 0)
        {
            text = text[..at];
        }

        return text.Length > 90 ? text[..90] : text;
    }

    private sealed class CorpusEntry
    {
        public string Id { get; set; } = string.Empty;

        public string Repository { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public int Line { get; set; }

        public string Source { get; set; } = string.Empty;
    }

    /// <summary>A variable-length opaque codec standing in for a Python-registered type; never reads or writes in the sweep.</summary>
    private sealed class StubCodec(string name) : ICustomCodec
    {
        public string Name => name;

        public int? FixedSize => null;

        public int Alignment => 1;

        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed) => throw new NotSupportedException();

        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten) => throw new NotSupportedException();
    }
}
