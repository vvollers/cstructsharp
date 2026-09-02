namespace CStructSharp.Tests;

using System.Text.Json;

/// <summary>Checks the reviewed compiler observations against the exact Portable fixtures they can inform.</summary>
[TestClass]
public class CompilerDifferentialFixtureTests
{
    /// <summary>Every checked-in observation retains provenance and expressly declines to claim an ABI profile.</summary>
    [TestMethod]
    public void Baselines_AreObservationOnlyAndHaveCompleteProvenance()
    {
        JsonElement[] baselines = LoadBaselines();
        var families = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonElement root in baselines)
        {
            Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.AreEqual("compiler-observation", root.GetProperty("evidenceKind").GetString());
            Assert.AreEqual("observation-only", root.GetProperty("claim").GetString());
            Assert.IsFalse(root.TryGetProperty("profile", out _));

            JsonElement fixture = root.GetProperty("fixture");
            Assert.AreEqual("portable-host-facts", fixture.GetProperty("id").GetString());
            Assert.AreEqual(
                "tools/compiler-fixtures/portable-host-facts.c",
                fixture.GetProperty("source").GetString());
            Assert.AreEqual(64, fixture.GetProperty("sha256").GetString()?.Length);

            JsonElement compiler = root.GetProperty("compiler");
            families.Add(compiler.GetProperty("family").GetString() ?? string.Empty);
            Assert.IsFalse(string.IsNullOrWhiteSpace(compiler.GetProperty("version").GetString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(compiler.GetProperty("versionOutput").GetString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(compiler.GetProperty("target").GetString()));
            Assert.AreEqual("C11", compiler.GetProperty("language").GetString());
            Assert.AreEqual(5, compiler.GetProperty("flags").GetArrayLength());
        }

        CollectionAssert.AreEquivalent(new[] { "Clang", "GCC", }, families.ToArray());
    }

    /// <summary>Native fixed-width aggregates agree with the equivalent little-endian aligned Portable examples.</summary>
    [TestMethod]
    public void FixedWidthAggregates_MatchExactPortableExamples()
    {
        using JsonDocument contract = LoadJson("portable-v1.json");
        Dictionary<string, JsonElement> examples = contract.RootElement
            .GetProperty("layoutExamples")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("id").GetString() ?? string.Empty,
                item => item.Clone(),
                StringComparer.Ordinal);

        JsonElement[] baselines = LoadBaselines();
        foreach (JsonElement baseline in baselines)
        {
            JsonElement facts = baseline.GetProperty("facts");
            Assert.AreEqual("little", facts.GetProperty("endian").GetString());
            AssertLayoutMatches(
                examples["aligned-mixed"],
                facts.GetProperty("fixedWidthAggregate"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tag"] = "sample.a",
                    ["count"] = "sample.b",
                    ["code"] = "sample.c",
                });
            AssertLayoutMatches(
                examples["aligned-nested-array"],
                facts.GetProperty("nestedArray"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["prefix"] = "root.prefix",
                    ["items0Tag"] = "root.items[0].tag",
                    ["items0Value"] = "root.items[0].value",
                    ["items1Tag"] = "root.items[1].tag",
                    ["items1Value"] = "root.items[1].value",
                    ["tail"] = "root.tail",
                });
            AssertLayoutMatches(
                examples["aligned-union"],
                facts.GetProperty("union"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["small"] = "choice.small",
                    ["large"] = "choice.large",
                });
        }
    }

    /// <summary>Observed native widths are evidence, not aliases for Portable's deterministic primitive contract.</summary>
    [TestMethod]
    public void NativeScalarDifferences_DoNotBecomePortableClaims()
    {
        using JsonDocument contract = LoadJson("portable-v1.json");
        int portableLongSize = contract.RootElement
            .GetProperty("fixedPrimitives")
            .EnumerateArray()
            .Single(item => item.GetProperty("spelling").GetString() == "long")
            .GetProperty("bytes")
            .GetInt32();
        Assert.AreEqual(8, portableLongSize);

        JsonElement[] baselines = LoadBaselines();
        foreach (JsonElement baseline in baselines)
        {
            JsonElement facts = baseline.GetProperty("facts");
            Assert.AreEqual(4, facts.GetProperty("long").GetProperty("size").GetInt32());
            Assert.AreNotEqual(
                portableLongSize,
                facts.GetProperty("long").GetProperty("size").GetInt32());
            Assert.AreEqual(8, facts.GetProperty("pointer").GetProperty("size").GetInt32());
            Assert.AreEqual(8, facts.GetProperty("pointerAggregate").GetProperty("offsets")
                .GetProperty("target").GetInt32());
        }
    }

    /// <summary>Implementation-defined bitfields retain raw native images without being normalized into Portable.</summary>
    [TestMethod]
    public void Bitfields_RetainNativeByteImagesWithoutPortableParityClaim()
    {
        using JsonDocument contract = LoadJson("portable-v1.json");
        JsonElement portable = contract.RootElement
            .GetProperty("layoutExamples")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "portable-bitfields");
        Assert.AreEqual(3, portable.GetProperty("size").GetInt32());
        Assert.AreEqual("8D3412", portable.GetProperty("bytes").GetString());

        JsonElement[] baselines = LoadBaselines();
        foreach (JsonElement baseline in baselines)
        {
            JsonElement native = baseline.GetProperty("facts").GetProperty("bitfield");
            Assert.AreEqual(4, native.GetProperty("size").GetInt32());
            Assert.AreEqual(2, native.GetProperty("offsets").GetProperty("next").GetInt32());
            Assert.AreEqual("8D003412", native.GetProperty("bytes").GetString());
            Assert.AreNotEqual(portable.GetProperty("bytes").GetString(), native.GetProperty("bytes").GetString());
        }
    }

    private static void AssertLayoutMatches(
        JsonElement expected,
        JsonElement actual,
        IReadOnlyDictionary<string, string> offsetMap)
    {
        Assert.AreEqual(expected.GetProperty("size").GetInt32(), actual.GetProperty("size").GetInt32());
        Assert.AreEqual(
            expected.GetProperty("alignment").GetInt32(),
            actual.GetProperty("alignment").GetInt32());
        Assert.AreEqual(expected.GetProperty("bytes").GetString(), actual.GetProperty("bytes").GetString());

        JsonElement expectedOffsets = expected.GetProperty("offsets");
        JsonElement actualOffsets = actual.GetProperty("offsets");
        foreach (KeyValuePair<string, string> offset in offsetMap)
        {
            Assert.AreEqual(
                expectedOffsets.GetProperty(offset.Value).GetInt64(),
                actualOffsets.GetProperty(offset.Key).GetInt64(),
                offset.Key);
        }
    }

    private static JsonElement[] LoadBaselines()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "compiler-fixtures", "baselines");
        string[] paths = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(2, paths.Length);
        return paths.Select(
            path =>
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.Clone();
            }).ToArray();
    }

    private static JsonDocument LoadJson(string relativePath)
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relativePath)));
    }
}
