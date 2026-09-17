namespace CStructSharp.Tests;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the static write plan (E2.10): serializing a fully fixed composite through the plan must be
///     byte-for-byte what the field-by-field writer produces, for every destination shape, every input shape
///     (parsed value, dictionary, POCO, typed arrays), every failure, every limit, and existing destination bytes.
/// </summary>
[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1300:ElementMustBeginWithUpperCaseLetter", Justification = "POCO members are named after layout fields")]
public class StaticWritePlanTests
{
    private const string Layout = """
        enum kind : uint8 { a = 1, b = 2 };
        struct leaf { uint8 k; uint32 v; };
        struct inner { leaf first; leaf second; uint16 pad; };
        struct root {
            uint16 magic;
            kind which;
            inner nested;
            uint32 samples[3];
            int16 deltas[2];
            uint8 none[0];
            struct { uint8 p; uint8 q; };
            leaf leaves[2];
            uint8 tail;
        };
        """;

    private static readonly byte[] Bytes =
    [
        0x34, 0x12, 2, 9, 1, 0, 0, 0, 8, 2, 0, 0, 0, 0xEE, 0xFF, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 0xFE, 0xFF, 0x10, 0x00,
        0xAA, 0xBB, 5, 6, 0, 0, 0, 7, 8, 0, 0, 0, 0x99,
    ];

    /// <summary>The plan and the general writer produce identical bytes for a parsed value, a dictionary, and a POCO, into every destination.</summary>
    [TestMethod]
    public void WritePlan_MatchesGeneralWriter_ForEveryInputAndDestination()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            byte[] bytes = aligned ? layout.Serialize("root", CreateDictionary()) : Bytes;
            StructValue parsed = (StructValue)layout.Parse(bytes, "root");
            Assert.IsTrue(layout.CompiledModel.Symbols["root"].Symbol.Definition is CompiledCompositeType { StaticPlan.SupportsWrite: true }, "plan exists");

            foreach ((string label, object data) in new (string, object)[] { ("parsed", parsed), ("dictionary", CreateDictionary()), ("poco", CreatePoco()), })
            {
                AssertSameOutcome(layout, data, null, $"{label} aligned={aligned}");
                CollectionAssert.AreEqual(bytes, layout.Serialize("root", data), $"{label} aligned={aligned}: round trip");
            }
        }
    }

    /// <summary>Every failure the general writer raises inside a planned composite is raised by the plan with the same type and message.</summary>
    [TestMethod]
    public void WritePlan_MatchesGeneralWriter_OnFailures()
    {
        var layout = new CStruct(Layout);
        foreach ((string label, Action<Dictionary<string, object?>> mutate) in new (string, Action<Dictionary<string, object?>>)[]
        {
            ("missing member", data => data.Remove("magic")),
            ("null scalar", data => data["magic"] = null),
            ("null nested", data => data["nested"] = null),
            ("null array", data => data["samples"] = null),
            ("null element", data => data["samples"] = new object?[] { 1u, null, 3u }),
            ("null nested element", data => data["leaves"] = new object?[] { new Dictionary<string, object?> { ["k"] = 1, ["v"] = 2 }, null }),
            ("overflow", data => data["magic"] = 70000),
            ("negative to unsigned", data => data["magic"] = -1),
            ("text to number", data => data["magic"] = "12"),
            ("bad text", data => data["magic"] = "twelve"),
            ("fraction", data => data["magic"] = 12.75),
            ("unknown enum", data => data["which"] = "zz"),
            ("enum out of range", data => data["which"] = 300),
            ("short array", data => data["samples"] = new uint[] { 1, 2 }),
            ("long array", data => data["samples"] = new uint[] { 1, 2, 3, 4 }),
            ("typed long array", data => data["deltas"] = new short[] { 1, 2, 3 }),
            ("non-array", data => data["samples"] = 5),
            ("short nested array", data => data["leaves"] = new object[] { new Dictionary<string, object?> { ["k"] = 1, ["v"] = 2 } }),
            ("missing in nested", data => ((Dictionary<string, object?>)data["nested"]!).Remove("pad")),
            ("missing promoted", data => data.Remove("q")),
            ("overflow in nested array element", data => data["leaves"] = new object[] { new Dictionary<string, object?> { ["k"] = 1, ["v"] = 2 }, new Dictionary<string, object?> { ["k"] = 256, ["v"] = 2 } }),
            ("int16 range", data => data["deltas"] = new object[] { 1, 40000 }),
        })
        {
            Dictionary<string, object?> data = CreateDictionary();
            mutate(data);
            AssertSameOutcome(layout, data, null, label);
        }

        for (long budget = 0; budget <= Bytes.Length + 1; budget++)
        {
            AssertSameOutcome(layout, CreateDictionary(), new WriteOptions { MaxTotalBytesWritten = budget }, $"budget {budget}");
        }

        AssertSameOutcome(layout, CreateDictionary(), new WriteOptions { MaxArrayElements = 2 }, "array limit");
        AssertSameOutcome(layout, CreateDictionary(), new WriteOptions { MaxNestingDepth = 1 }, "nesting limit 1");
        AssertSameOutcome(layout, CreateDictionary(), new WriteOptions { MaxNestingDepth = 2 }, "nesting limit 2");
        AssertSameOutcome(layout, CreateDictionary(), new WriteOptions { MaxNestingDepth = 3 }, "nesting limit 3");
        AssertSameOutcome(layout, CreateDictionary(), new WriteOptions { BindingMode = PocoBindingMode.PublicReadWrite }, "binding mode");
    }

    /// <summary>A failure inside a planned composite leaves the destination untouched.</summary>
    [TestMethod]
    public void WritePlan_LeavesTheDestinationUntouched_OnFailure()
    {
        var layout = new CStruct(Layout);
        Dictionary<string, object?> data = CreateDictionary();
        data["tail"] = "nope";
        using var destination = new MemoryStream();
        Assert.Throws<CStructWriteException>(() => layout.WriteStream(destination, "root", data));
        Assert.AreEqual(0, destination.Length);
    }

    /// <summary>Writing into a stream that already has bytes keeps whatever padding bytes held and appends beyond the end like the general writer.</summary>
    [TestMethod]
    public void WritePlan_PreservesExistingPaddingBytes()
    {
        var layout = new CStruct("struct root { uint8 a; uint32 b; uint8 c; };", aligned: true);
        var data = new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2u, ["c"] = 3 };
        foreach (int start in new[] { 0, 3, 12, 16, 20 })
        {
            byte[] existing = Enumerable.Range(0, 16).Select(index => (byte)(0xE0 + index)).ToArray();
            using var withPlan = new MemoryStream();
            withPlan.Write(existing);
            withPlan.Position = start;
            layout.WriteStream(withPlan, "root", data);

            using var withoutPlan = new MemoryStream();
            withoutPlan.Write(existing);
            withoutPlan.Position = start;
            StaticReadPlan.DisabledForTesting = true;
            layout.WriteStream(withoutPlan, "root", data);
            StaticReadPlan.DisabledForTesting = false;

            CollectionAssert.AreEqual(withoutPlan.ToArray(), withPlan.ToArray(), $"start {start}");
            Assert.AreEqual(withoutPlan.Position, withPlan.Position, $"start {start}: position");
        }
    }

    /// <summary>Typed arrays from a parse (PrimitiveArray of the codec's own type) take the bulk path; every other shape takes the element loop, with identical bytes.</summary>
    [TestMethod]
    public void WritePlan_TypedArrays_MatchElementConversion()
    {
        var layout = new CStruct("struct root { uint16> be[3]; int32< le[2]; float64< d[2]; int8 s[2]; uint24 u[2]; };");
        byte[] bytes = [0, 1, 0, 2, 0, 3, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0xF0, 0x3F, 0, 0, 0, 0, 0, 0, 0xF8, 0xBF, 0xFF, 0x7F, 1, 0, 0, 0x00, 0x10, 0x20];
        StructValue parsed = (StructValue)layout.Parse(bytes, "root");
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed), "typed round trip");
        var boxed = new Dictionary<string, object?>
        {
            ["be"] = new object[] { 1, 2, 3 },
            ["le"] = new List<object?> { 1, -1 },
            ["d"] = new[] { 1.0, -1.5 },
            ["s"] = new short[] { -1, 127 },
            ["u"] = new uint[] { 1, 0x201000 },
        };
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", boxed), "boxed round trip");
        AssertSameOutcome(layout, boxed, null, "boxed");
        var mixed = new Dictionary<string, object?>
        {
            ["be"] = ((StructValue)layout.Parse(bytes, "root"))["le"],
            ["le"] = parsed["be"],
            ["d"] = parsed["d"],
            ["s"] = parsed["s"],
            ["u"] = parsed["u"],
        };
        AssertSameOutcome(layout, mixed, null, "mismatched typed arrays");
    }

    /// <summary>Layout variables published by planned fields feed later expressions exactly as before.</summary>
    [TestMethod]
    public void WritePlan_PublishesCapturedVariables()
    {
        var layout = new CStruct("enum e : uint8 { two = 2 }; struct head { uint8 n; e m; }; struct root { head h; uint8 items[n + m]; };");
        var data = new Dictionary<string, object?>
        {
            ["h"] = new Dictionary<string, object?> { ["n"] = 1, ["m"] = "two" },
            ["items"] = new byte[] { 7, 8, 9 },
        };
        CollectionAssert.AreEqual(new byte[] { 1, 2, 7, 8, 9 }, layout.Serialize("root", data));
        AssertSameOutcome(layout, data, null, "captured");
    }

    /// <summary>Every benchmark fixture that parses re-serializes to identical bytes with and without the plan.</summary>
    [TestMethod]
    public void EveryFixture_SerializesIdenticallyWithAndWithoutThePlan()
    {
        string directory = FindFixtureDirectory();
        int compared = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { MaxDepth = 4096 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("bytes").ValueKind == JsonValueKind.Null || root.GetProperty("byteLength").GetInt64() > 2 * 1024 * 1024 ||
                root.GetProperty("expectedError").ValueKind != JsonValueKind.Null)
            {
                continue;
            }

            byte[] bytes = Materialize(directory, root.GetProperty("bytes"));
            JsonElement options = root.GetProperty("options");
            var layout = new CStruct(
                root.GetProperty("definition").GetString()!,
                options.GetProperty("pointerSize").GetByte(),
                options.GetProperty("aligned").GetBoolean(),
                options.GetProperty("littleEndian").GetBoolean());
            string rootName = root.GetProperty("root").GetString()!;
            string id = root.GetProperty("id").GetString()!;
            object parsed;
            try
            {
                parsed = layout.Parse(bytes, rootName);
            }
            catch (CStructException)
            {
                continue;
            }

            (byte[]? planned, Exception? plannedError) = Try(() => layout.Serialize(rootName, parsed));
            StaticReadPlan.DisabledForTesting = true;
            (byte[]? general, Exception? generalError) = Try(() => layout.Serialize(rootName, parsed));
            StaticReadPlan.DisabledForTesting = false;
            Assert.AreEqual(generalError?.Message, plannedError?.Message, id);
            if (general is not null)
            {
                CollectionAssert.AreEqual(general, planned, id);
            }

            compared++;
        }

        Assert.IsGreaterThan(30, compared);
    }

    private static void AssertSameOutcome(CStruct layout, object data, WriteOptions? options, string label)
    {
        foreach ((string destination, Func<object, WriteOptions?, byte[]> write) in new (string, Func<object, WriteOptions?, byte[]>)[]
        {
            ("array", (value, o) => layout.Serialize("root", value, options: o)),
            ("span", (value, o) =>
            {
                byte[] target = new byte[256];
                int written = layout.Serialize(target.AsSpan(), "root", value, options: o);
                return target[..written];
            }),
            ("buffer writer", (value, o) =>
            {
                var writer = new ArrayBufferWriter<byte>();
                layout.Serialize(writer, "root", value, options: o);
                return writer.WrittenSpan.ToArray();
            }),
            ("stream", (value, o) =>
            {
                using var stream = new MemoryStream();
                layout.WriteStream(stream, "root", value, options: o);
                return stream.ToArray();
            }),
        })
        {
            (byte[]? planned, Exception? plannedError) = Try(() => write(data, options));
            StaticReadPlan.DisabledForTesting = true;
            (byte[]? general, Exception? generalError) = Try(() => write(data, options));
            StaticReadPlan.DisabledForTesting = false;
            string caseLabel = label + " / " + destination;
            Assert.AreEqual(generalError?.GetType(), plannedError?.GetType(), caseLabel);
            Assert.AreEqual(generalError?.Message, plannedError?.Message, caseLabel);
            if (general is not null)
            {
                CollectionAssert.AreEqual(general, planned, caseLabel);
            }
        }
    }

    private static (byte[]? Result, Exception? Error) Try(Func<byte[]> write)
    {
        try
        {
            return (write(), null);
        }
        catch (CStructException exception)
        {
            return (null, exception);
        }
    }

    private static Dictionary<string, object?> CreateDictionary()
    {
        return new Dictionary<string, object?>
        {
            ["magic"] = 0x1234,
            ["which"] = "b",
            ["nested"] = new Dictionary<string, object?>
            {
                ["first"] = new Dictionary<string, object?> { ["k"] = 9, ["v"] = 1u },
                ["second"] = new Dictionary<string, object?> { ["k"] = 8, ["v"] = 2u },
                ["pad"] = 0xFFEE,
            },
            ["samples"] = new uint[] { 1, 2, 3 },
            ["deltas"] = new short[] { -2, 16 },
            ["none"] = Array.Empty<byte>(),
            ["p"] = 0xAA,
            ["q"] = 0xBB,
            ["leaves"] = new object[]
            {
                new Dictionary<string, object?> { ["k"] = 5, ["v"] = 6u },
                new Dictionary<string, object?> { ["k"] = 7, ["v"] = 8u },
            },
            ["tail"] = 0x99,
        };
    }

    private static RootPoco CreatePoco()
    {
        return new RootPoco
        {
            magic = 0x1234,
            which = "b",
            nested = new InnerPoco { first = new LeafPoco { k = 9, v = 1 }, second = new LeafPoco { k = 8, v = 2 }, pad = 0xFFEE },
            samples = [1, 2, 3],
            deltas = [-2, 16],
            none = [],
            p = 0xAA,
            q = 0xBB,
            leaves = [new LeafPoco { k = 5, v = 6 }, new LeafPoco { k = 7, v = 8 }],
            tail = 0x99,
        };
    }

    private static byte[] Materialize(string directory, JsonElement spec)
    {
        switch (spec.GetProperty("kind").GetString())
        {
        case "hex":
            return Convert.FromHexString(spec.GetProperty("hex").GetString()!);
        case "file":
            return File.ReadAllBytes(Path.Combine(directory, spec.GetProperty("file").GetString()!));
        default:
            {
                uint x = spec.GetProperty("seed").GetUInt32();
                int size = spec.GetProperty("size").GetInt32();
                byte[] result = new byte[size];
                if (x == 0)
                {
                    x = 1;
                }

                for (int index = 0; index < size; index++)
                {
                    x ^= x << 13;
                    x ^= x >> 17;
                    x ^= x << 5;
                    result[index] = (byte)(x & 0xff);
                }

                return result;
            }
        }
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

    public sealed class LeafPoco
    {
        public byte k { get; set; }

        public uint v { get; set; }
    }

    public sealed class InnerPoco
    {
        public LeafPoco first { get; set; } = null!;

        public LeafPoco second { get; set; } = null!;

        public ushort pad { get; set; }
    }

    public sealed class RootPoco
    {
        public ushort magic { get; set; }

        public string which { get; set; } = string.Empty;

        public InnerPoco nested { get; set; } = null!;

        public uint[] samples { get; set; } = [];

        public short[] deltas { get; set; } = [];

        public byte[] none { get; set; } = [];

        public byte p { get; set; }

        public byte q { get; set; }

        public LeafPoco[] leaves { get; set; } = [];

        public byte tail { get; set; }
    }
}
