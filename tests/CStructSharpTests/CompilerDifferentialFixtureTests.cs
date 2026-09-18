namespace CStructSharp.Tests;

using System.Text.Json;
using CStructSharp.Values;

/// <summary>
///     Checks the recorded compiler observations (<c>contracts/quality/compiler-fixtures/baselines</c>) against the
///     library: every shape in <c>shapes.json</c> claims the <see cref="BitfieldPacking"/> mode(s) in which the
///     library reproduces a compiler of the same ABI family byte for byte, and every claim is verified against every
///     baseline of that family. Scalar facts that differ by design (native <c>long</c>, pointers) stay observations.
/// </summary>
[TestClass]
public class CompilerDifferentialFixtureTests
{
    /// <summary>Every baseline records compiler identity, ABI family, target, flags, language, host, and fixture identity.</summary>
    [TestMethod]
    public void Baselines_AreObservationOnlyAndHaveCompleteProvenance()
    {
        foreach ((string file, JsonElement root) in LoadBaselines())
        {
            Assert.AreEqual(2, root.GetProperty("schemaVersion").GetInt32(), file);
            Assert.AreEqual("compiler-observation", root.GetProperty("evidenceKind").GetString(), file);
            Assert.AreEqual("observation-only", root.GetProperty("claim").GetString(), file);
            Assert.IsFalse(root.TryGetProperty("profile", out _), file);

            JsonElement fixture = root.GetProperty("fixture");
            Assert.AreEqual("portable-host-facts", fixture.GetProperty("id").GetString(), file);
            Assert.AreEqual("tools/compiler-fixtures/portable-host-facts.c", fixture.GetProperty("source").GetString(), file);
            Assert.AreEqual(64, fixture.GetProperty("sha256").GetString()?.Length, file);

            JsonElement compiler = root.GetProperty("compiler");
            CollectionAssert.Contains(new[] { "GCC", "Clang", "MSVC" }, compiler.GetProperty("family").GetString(), file);
            CollectionAssert.Contains(new[] { "sysv", "msvc" }, compiler.GetProperty("abi").GetString(), file);
            foreach (string key in new[] { "version", "versionOutput", "target", "executable" })
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(compiler.GetProperty(key).GetString()), $"{file}: {key}");
            }

            Assert.AreEqual("C11", compiler.GetProperty("language").GetString(), file);
            Assert.IsTrue(compiler.GetProperty("flags").GetArrayLength() >= 4, file);
            CollectionAssert.Contains(new[] { "Linux", "macOS", "Windows" }, root.GetProperty("host").GetProperty("os").GetString(), file);
        }
    }

    /// <summary>
    ///     The selected native fixtures use fixed-width integers and match the equivalent aligned little-endian
    ///     Portable examples on every recorded compiler.
    /// </summary>
    [TestMethod]
    public void FixedWidthAggregates_MatchExactPortableExamples()
    {
        using JsonDocument contract = LoadJson("portable-v1.json");
        Dictionary<string, JsonElement> examples = contract.RootElement
            .GetProperty("layoutExamples")
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString() ?? string.Empty, item => item.Clone(), StringComparer.Ordinal);

        foreach ((string file, JsonElement baseline) in LoadBaselines())
        {
            JsonElement facts = baseline.GetProperty("facts");
            Assert.AreEqual("little", facts.GetProperty("endian").GetString(), file);
            AssertLayoutMatches(examples["aligned-mixed"], facts.GetProperty("fixedWidthAggregate"), new Dictionary<string, string>(StringComparer.Ordinal) { ["tag"] = "sample.a", ["count"] = "sample.b", ["code"] = "sample.c", }, file);
            AssertLayoutMatches(examples["aligned-nested-array"], facts.GetProperty("nestedArray"), new Dictionary<string, string>(StringComparer.Ordinal) { ["prefix"] = "root.prefix", ["items0Tag"] = "root.items[0].tag", ["items0Value"] = "root.items[0].value", ["items1Tag"] = "root.items[1].tag", ["items1Value"] = "root.items[1].value", ["tail"] = "root.tail", }, file);
            AssertLayoutMatches(examples["aligned-union"], facts.GetProperty("union"), new Dictionary<string, string>(StringComparer.Ordinal) { ["small"] = "choice.small", ["large"] = "choice.large", }, file);
        }
    }

    /// <summary>
    ///     Portable <c>long</c> is eight bytes regardless of the host; a compiler whose <c>long</c> is four bytes is
    ///     matched only through <c>CLongWidth = 32</c>, and pointer widths stay a constructor choice.
    /// </summary>
    [TestMethod]
    public void NativeScalarDifferences_DoNotBecomePortableClaims()
    {
        using JsonDocument contract = LoadJson("portable-v1.json");
        JsonElement longAlias = contract.RootElement.GetProperty("aliasSpellings").EnumerateArray().Single(item => item.GetProperty("spelling").GetString() == "long");
        string canonical = longAlias.GetProperty("canonical").GetString()!;
        int portableLongSize = contract.RootElement.GetProperty("fixedPrimitives").EnumerateArray().Single(item => item.GetProperty("spelling").GetString() == canonical).GetProperty("bytes").GetInt32();
        Assert.AreEqual(8, portableLongSize);
        Assert.AreEqual("int32", longAlias.GetProperty("cLongWidth32").GetString());

        foreach ((string file, JsonElement baseline) in LoadBaselines())
        {
            JsonElement facts = baseline.GetProperty("facts");
            int nativeLong = facts.GetProperty("long").GetProperty("size").GetInt32();
            CollectionAssert.Contains(new[] { 4, 8 }, nativeLong, file);
            int pointer = facts.GetProperty("pointer").GetProperty("size").GetInt32();
            Assert.AreEqual(pointer, facts.GetProperty("pointerAggregate").GetProperty("offsets").GetProperty("target").GetInt32(), file);
            Assert.AreEqual(pointer, new CStruct("struct p { uint8 marker; uint16 *target; };", pointerSize: (byte)pointer, aligned: true).GetStructSizeInBytes("p") - pointer, file);
        }
    }

    /// <summary>
    ///     The Portable packed bitfield example is three bytes, <c>8D3412</c>; the natural (aligned) native image is
    ///     four bytes with padding before <c>next</c>, which aligned Portable placement reproduces.
    /// </summary>
    [TestMethod]
    public void Bitfields_PackedExampleVersusAlignedNativeImage()
    {
        using JsonDocument contract = LoadJson("portable-v1.json");
        JsonElement portable = contract.RootElement.GetProperty("layoutExamples").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "portable-bitfields");
        Assert.AreEqual(3, portable.GetProperty("size").GetInt32());
        Assert.AreEqual("8D3412", portable.GetProperty("bytes").GetString());

        foreach ((string file, JsonElement baseline) in LoadBaselines())
        {
            JsonElement native = baseline.GetProperty("facts").GetProperty("bitfield");
            Assert.AreEqual(4, native.GetProperty("size").GetInt32(), file);
            Assert.AreEqual(2, native.GetProperty("offsets").GetProperty("next").GetInt32(), file);
            Assert.AreEqual("8D003412", native.GetProperty("bytes").GetString(), file);
            var aligned = new CStruct("struct bits { uint8 low:3; uint8 high:5; uint16 next; };", aligned: true, compilationOptions: PackingFor(baseline));
            CollectionAssert.AreEqual(Convert.FromHexString("8D003412"), aligned.Serialize("bits", new Dictionary<string, object?> { ["low"] = 5, ["high"] = 17, ["next"] = 0x1234 }), file);
        }
    }

    /// <summary>
    ///     Every shape's claim holds: in the mode named for the baseline's ABI family, the library's size, alignment,
    ///     and bytes equal the compiler's, and the compiler's bytes parse back to the shape's values.
    /// </summary>
    [TestMethod]
    public void Shapes_MatchEveryBaselineOfTheClaimedAbiFamily()
    {
        using JsonDocument shapes = LoadJson(Path.Combine("compiler-fixtures", "shapes.json"));
        (string File, JsonElement Root)[] baselines = LoadBaselines();
        int verified = 0;
        foreach (JsonElement shape in shapes.RootElement.GetProperty("shapes").EnumerateArray())
        {
            string id = shape.GetProperty("id").GetString()!;
            JsonElement portable = shape.GetProperty("portable");
            foreach ((string file, JsonElement baseline) in baselines)
            {
                string abi = baseline.GetProperty("compiler").GetProperty("abi").GetString()!;
                bool claimed = portable.GetProperty(abi).GetBoolean();
                JsonElement recorded = baseline.GetProperty("facts").GetProperty("shapes").GetProperty(id);
                string context = $"{id} against {file}";

                CStruct layout = Compile(portable, baseline);
                const string root = "s";
                byte[] nativeBytes = Convert.FromHexString(recorded.GetProperty("bytes").GetString()!);
                object value = ToValue(portable.GetProperty("values"))!;
                if (portable.TryGetProperty("union", out JsonElement isUnion) && isUnion.GetBoolean())
                {
                    // A union writes one selected member; the fixture sets exactly one.
                    KeyValuePair<string, object?> member = ((Dictionary<string, object?>)value).Single();
                    value = UnionValue.FromMember(root, member.Key, member.Value);
                }

                byte[] portableBytes = layout.Serialize(root, value);

                // A `#pragma pack(1)` object has alignment 1 in C; the library's packed placement keeps reporting the
                // types' natural alignment, so alignment is compared for naturally placed shapes only.
                bool matches = layout.GetStructSizeInBytes(root) == recorded.GetProperty("size").GetInt32() &&
                               (!portable.GetProperty("aligned").GetBoolean() || layout.GetStructAlignmentInBytes(root) == recorded.GetProperty("alignment").GetInt32()) &&
                               portableBytes.AsSpan().SequenceEqual(nativeBytes);
                Assert.AreEqual(claimed, matches, $"{context}: claim {claimed}, library {Convert.ToHexString(portableBytes)} size {layout.GetStructSizeInBytes(root)} align {layout.GetStructAlignmentInBytes(root)}; compiler {recorded.GetProperty("bytes").GetString()} size {recorded.GetProperty("size").GetInt32()} align {recorded.GetProperty("alignment").GetInt32()}");
                if (matches)
                {
                    object? parsed = layout.ReadValue(nativeBytes, root);
                    AssertValues(portable.GetProperty("values"), parsed, context);
                    verified++;
                }
            }
        }

        Assert.IsTrue(verified > 0, "No shape was verified against any baseline.");
    }

    private static CStruct Compile(JsonElement portable, JsonElement baseline)
    {
        bool longFromFacts = portable.TryGetProperty("cLongWidthFromFacts", out JsonElement fromFacts) && fromFacts.GetBoolean();
        var options = new CStructCompilationOptions
        {
            BitfieldPacking = PackingFor(baseline).BitfieldPacking,
            CLongWidth = longFromFacts ? baseline.GetProperty("facts").GetProperty("long").GetProperty("size").GetInt32() * 8 : 64,
        };

        int pointerSize = baseline.GetProperty("facts").GetProperty("pointer").GetProperty("size").GetInt32();
        return new CStruct(portable.GetProperty("layout").GetString()!, pointerSize: (byte)pointerSize, aligned: portable.GetProperty("aligned").GetBoolean(), compilationOptions: options);
    }

    private static CStructCompilationOptions PackingFor(JsonElement baseline)
    {
        return new CStructCompilationOptions
        {
            BitfieldPacking = baseline.GetProperty("compiler").GetProperty("abi").GetString() == "msvc" ? BitfieldPacking.Msvc : BitfieldPacking.SysV,
        };
    }

    private static object? ToValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
        case JsonValueKind.Object:
            {
                var members = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    members[property.Name] = ToValue(property.Value);
                }

                return members;
            }

        case JsonValueKind.Array:
            return element.EnumerateArray().Select(ToValue).ToList();
        case JsonValueKind.True:
            return true;
        case JsonValueKind.False:
            return false;
        case JsonValueKind.Number:
            if (element.TryGetInt64(out long integer))
            {
                return integer;
            }

            if (element.TryGetUInt64(out ulong wide))
            {
                return wide;
            }

            return element.GetDouble();
        default:
            return element.GetString();
        }
    }

    private static void AssertValues(JsonElement expected, object? actual, string context)
    {
        switch (expected.ValueKind)
        {
        case JsonValueKind.Object:
            {
                var members = (IReadOnlyDictionary<string, object?>)actual!;
                foreach (JsonProperty property in expected.EnumerateObject())
                {
                    AssertValues(property.Value, members[property.Name], context + "." + property.Name);
                }

                break;
            }

        case JsonValueKind.Array:
            {
                var items = ((System.Collections.IEnumerable)actual!).Cast<object?>().ToList();
                JsonElement[] expectedItems = expected.EnumerateArray().ToArray();
                Assert.AreEqual(expectedItems.Length, items.Count, context);
                for (int index = 0; index < items.Count; index++)
                {
                    AssertValues(expectedItems[index], items[index], $"{context}[{index}]");
                }

                break;
            }

        case JsonValueKind.True or JsonValueKind.False:
            Assert.AreEqual(expected.GetBoolean(), (bool)actual!, context);
            break;
        case JsonValueKind.Number when expected.TryGetInt64(out long integer):
            Assert.AreEqual(integer, Convert.ToInt64(actual is EnumValueResult enumValue ? (object)(long)enumValue.Value : actual), context);
            break;
        case JsonValueKind.Number when expected.TryGetUInt64(out ulong wide):
            Assert.AreEqual(wide, Convert.ToUInt64(actual), context);
            break;
        case JsonValueKind.Number:
            Assert.AreEqual(expected.GetDouble(), Convert.ToDouble(actual), 0.0, context);
            break;
        default:
            Assert.AreEqual(expected.GetString(), actual?.ToString(), context);
            break;
        }
    }

    private static void AssertLayoutMatches(JsonElement expected, JsonElement actual, IReadOnlyDictionary<string, string> offsetMap, string file)
    {
        Assert.AreEqual(expected.GetProperty("size").GetInt32(), actual.GetProperty("size").GetInt32(), file);
        Assert.AreEqual(expected.GetProperty("alignment").GetInt32(), actual.GetProperty("alignment").GetInt32(), file);
        Assert.AreEqual(expected.GetProperty("bytes").GetString(), actual.GetProperty("bytes").GetString(), file);
        JsonElement expectedOffsets = expected.GetProperty("offsets");
        JsonElement actualOffsets = actual.GetProperty("offsets");
        foreach (KeyValuePair<string, string> offset in offsetMap)
        {
            Assert.AreEqual(expectedOffsets.GetProperty(offset.Value).GetInt64(), actualOffsets.GetProperty(offset.Key).GetInt64(), $"{file}: {offset.Key}");
        }
    }

    private static (string File, JsonElement Root)[] LoadBaselines()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "compiler-fixtures", "baselines");
        string[] paths = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(paths.Length >= 1, "At least one compiler baseline must be checked in.");
        return paths.Select(path =>
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return (Path.GetFileName(path), document.RootElement.Clone());
        }).ToArray();
    }

    private static JsonDocument LoadJson(string relativePath)
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, relativePath)));
    }
}
