namespace CStructSharpTests;

using System.Text.Json;
using CStructSharp;
using CStructSharp.Compilation;

/// <summary>
///     Every benchmark fixture whose root had a static read plan when the dissect-parity work started (Phase 0,
///     2026-09-16) must still have one: a language feature that made a fixed composite look dynamic to the planner
///     would silently move that fixture to the general reader and cost the plan's speed-up.
/// </summary>
[TestClass]
public class StaticPlanEligibilityRegressionTests
{
    /// <summary>The fixture ids that were plan-eligible at Phase 0, recorded from the pre-parity library.</summary>
    private static readonly string[] EligibleAtPhase0 =
    [
        "aligned-x256", "array-struct-100", "array-struct-10000", "array-u32-be-16384", "array-u32-be-256",
        "array-u32-be-262144", "array-u32-le-16384", "array-u32-le-256", "array-u32-le-262144",
        "array-u32-neutral-262144", "array-u64-le-1000000", "array-u8-1024", "array-u8-1048576",
        "array-u8-16m-stream", "array-u8-65536", "compile-large-512", "compile-medium-128", "compile-nested",
        "compile-small", "cond-plain128", "enum-x1k", "malformed-budget-exceeded", "malformed-truncated",
        "mixed-endian-record", "nested-x1", "nested-x256", "prim-be-record", "prim-be-x1k", "prim-le-record",
        "prim-le-x1k", "real-bmp", "real-jpg", "real-png", "real-tar", "real-wav",
    ];

    /// <summary>The parity fixtures whose roots are fixed and must be planned too (aliases resolve at construction; a promoted union keeps the general reader).</summary>
    private static readonly string[] EligibleParityFixtures = ["parity-alias-x1k",];

    /// <summary>Plan eligibility of every recorded fixture root is unchanged, and the fixed parity roots are planned.</summary>
    [TestMethod]
    public void FixtureRoots_KeepTheirStaticPlans()
    {
        string directory = FindFixtureDirectory();
        var eligible = new List<string>();
        var unplanned = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 4096, });
            JsonElement root = document.RootElement;
            string id = root.GetProperty("id").GetString()!;
            JsonElement options = root.GetProperty("options");
            var layout = new CStruct(
                root.GetProperty("definition").GetString()!,
                options.GetProperty("pointerSize").GetByte(),
                options.GetProperty("aligned").GetBoolean(),
                options.GetProperty("littleEndian").GetBoolean());
            string rootName = root.GetProperty("root").GetString()!;
            bool planned = HasPlan(layout, rootName);
            (planned ? eligible : unplanned).Add(id);
        }

        string[] lost = EligibleAtPhase0.Where(id => !eligible.Contains(id)).ToArray();
        Assert.IsEmpty(lost, "fixture roots that lost their static read plan: " + string.Join(", ", lost));
        string[] missingParity = EligibleParityFixtures.Where(id => !eligible.Contains(id)).ToArray();
        Assert.IsEmpty(missingParity, "parity fixture roots without a static read plan: " + string.Join(", ", missingParity));
        Console.WriteLine($"{eligible.Count} planned, {unplanned.Count} general-reader fixtures");
    }

    /// <summary>A composite root with a static read plan; a synthetic root (<c>uint32[256]</c>) has no composite and reads through the bulk primitive path instead.</summary>
    private static bool HasPlan(CStruct layout, string rootName)
    {
        return layout.CompiledModel.Symbols.TryGetValue(rootName, out CompiledTypeReference reference) &&
               reference.Symbol.Definition is CompiledCompositeType { StaticPlan: not null, };
    }

    private static string FindFixtureDirectory()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "benchmarks", "fixtures", "manifest.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("benchmarks/fixtures/manifest.json not found");
    }
}
