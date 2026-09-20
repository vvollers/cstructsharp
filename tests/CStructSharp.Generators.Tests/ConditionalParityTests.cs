namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Conditional groups (<c>if</c>/<c>else</c>, <c>switch</c>) through the generated readers, with the runtime as
///     the oracle: the guide's cases (decisions per array item, evaluate-once, unavailable locals, nested groups
///     skipped with their outer arm, switch without a match), the benchmark harness's conditional cases, and the
///     variable rules a conditional layout leans on (caller variables override defines; a local hides them).
/// </summary>
[TestClass]
public class ConditionalParityTests
{
    private const string Unaligned = "Root = \"root\", PointerSize = 4, Aligned = false, LittleEndian = true";

    private static readonly IReadOnlyDictionary<string, int> NoVariables = new Dictionary<string, int>();

    [TestMethod]
    public void GuideCases_MatchTheRuntime()
    {
        const string PerItem = """
            struct entry {
                uint8 tag;
                int8 some_parameter;
                if (some_parameter * 20 > 10) { uint8 high; } else { uint8 low; }
                switch (tag) { case 1: { uint8 first; } case 2: { uint8 second; } default: { uint8 other; } }
            };
            struct root { entry items[3]; };
            """;
        Run("per-item", PerItem, "01 00 0a 0b 02 01 14 15 03 ff 1e 1f");
        Run("per-item-retagged", PerItem, "02 00 0a 0b 02 01 14 15 03 ff 1e 1f");

        // Evaluate once: the group's decision holds for a, b, c even though `tag` is not re-read.
        Run("once", "struct root { uint8 tag; if (tag == 1) { uint8 a; uint8 b; uint8 c; } uint8 tail; };", "01 0a 0b 0c 63");
        Run("once-inactive", "struct root { uint8 tag; if (tag == 1) { uint8 a; uint8 b; uint8 c; } uint8 tail; };", "00 63");

        // An unavailable local: the second item never reads `count`, so its condition fails; the caller's `count`
        // and a define cannot stand in for it.
        const string Scope = "#define count 99\nstruct entry { uint8 tag; if (tag) { uint8 count; } if (count > 0) { uint8 payload[count]; } };\nstruct root { entry items[2]; };";
        Run("scope", Scope, "01 01 2a 00", expectedError: "CStructReadException");
        Run("scope-caller", Scope, "01 01 2a 00", new Dictionary<string, int> { ["count"] = 1 }, expectedError: "CStructReadException");
        Run("scope-guarded", "struct entry { uint8 tag; if (tag) { uint8 count; } if (tag != 0 && count > 0) { uint8 payload[count]; } };\nstruct root { entry items[2]; };", "01 01 2a 00");

        // A nested group is skipped with its inactive outer arm; reached, its unknown name fails.
        const string Nested = "struct root { uint8 tag; if (tag) { if (missing > 0) { uint8 value; } } uint8 tail; };";
        Run("nested-skipped", Nested, "00 09");
        Run("nested-reached", Nested, "01 09", expectedError: "CStructReadException");
        Run("nested-supplied", Nested, "01 09 07", new Dictionary<string, int> { ["missing"] = 1 });

        // A switch without a matching case and without a default contributes nothing; with a define as a label.
        Run("switch-nomatch", "#define TWO 2\nstruct root { uint8 tag; switch (tag) { case 1: { uint16 one; } case TWO: { uint32 two; } } uint8 tail; };", "03 63");
        Run("switch-define", "#define TWO 2\nstruct root { uint8 tag; switch (tag) { case 1: { uint16 one; } case TWO: { uint32 two; } } uint8 tail; };", "02 01 02 03 04 63");

        // A named nested struct cannot replace the conditional parent's own local; a promoted one is the parent's.
        Run("nested-child", "struct child { uint8 tag; }; struct root { uint8 tag; child c; if (tag == 1) { uint8 yes; } else { uint8 no; } };", "01 00 2a");
        Run("promoted-local", "struct root { uint8 kind; struct { uint8 tag; }; if (tag == 1) { uint8 yes; } else { uint8 no; } };", "00 01 2a");

        // Conditional composites, arrays, and strings inside arms (bitfields must sit in a named struct there).
        Run("arm-shapes", "struct pair { uint8 a; uint8 b:3; uint8 c:5; }; struct root { uint8 tag; if (tag & 1) { pair p; cstring name; } else { uint16 words[2]; } uint8 tail; };", "01 0a 2b 68 69 00 63");
        Run("arm-shapes-else", "struct pair { uint8 a; uint8 b:3; uint8 c:5; }; struct root { uint8 tag; if (tag & 1) { pair p; cstring name; } else { uint16 words[2]; } uint8 tail; };", "02 01 00 02 00 63");

        // A member read in an arm sizes a later array, and drives a later group.
        Run("chained", "struct root { uint8 tag; if (tag) { uint8 n; } else { uint8 m; } uint8 data[tag ? n : m]; if (n == 2) { uint8 extra; } };", "01 02 0a 0b 2a");
        Run("chained-else", "struct root { uint8 tag; if (tag) { uint8 n; } else { uint8 m; } uint8 data[tag ? n : m]; if (n == 2) { uint8 extra; } };", "00 01 0a", expectedError: "CStructReadException");
    }

    [TestMethod]
    public void VariableRules_MatchTheRuntime()
    {
        // A caller variable overrides a define of the same name, and a define that depends on it follows.
        const string Defines = "#define MODE 1\n#define WIDTH (MODE * 2)\nstruct root { uint8 head; if (MODE == 1) { uint8 one; } else { uint8 two[WIDTH]; } };";
        Run("define-default", Defines, "07 2a");
        Run("define-overridden", Defines, "07 01 02 03 04", new Dictionary<string, int> { ["MODE"] = 2 });

        // A qualified enum member as a constant; a wide define fails only when selected.
        Run("enum-constant", "enum kind : uint8 { A = 1, B = 2 }; struct root { uint8 tag; if (tag == kind.B) { uint8 b; } else { uint8 other; } };", "02 2a");
        Run("enum-constant-else", "enum kind : uint8 { A = 1, B = 2 }; struct root { uint8 tag; if (tag == kind.B) { uint8 b; } else { uint8 other; } };", "01 2a");
        Run("wide-define", "#define BIG 4294967295\nstruct root { uint8 tag; if (BIG > tag) { uint8 a; } else { uint8 b; } };", "01 2a", expectedError: "CStructReadException");

        // An expression failure in a selector names the operation and keeps the enclosing member.
        Run("selector-divide", "struct entry { uint8 d; if (8 / d > 1) { uint8 a; } else { uint8 b; } }; struct root { entry items[1]; };", "00 2a", expectedError: "CStructReadException");
        Run("selector-overflow", "struct root { uint32 big; if (big + 1 > 0) { uint8 a; } else { uint8 b; } };", "ff ff ff 7f 2a", expectedError: "CStructReadException");
        Run("selector-wide", "struct root { uint32 big; if (big > 0) { uint8 a; } else { uint8 b; } };", "00 00 00 80 2a", expectedError: "CStructReadException");
    }

    /// <summary>The benchmark harness's conditional cases with the harness's fill rule (every byte is the fill value).</summary>
    [TestMethod]
    public void BenchmarkConditionalCases_MatchTheRuntime()
    {
        string path = System.IO.Path.Combine(BenchmarkFixtures.Root, "benchmarks", "fixtures", "conditional-cases.json");
        using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        int cases = 0;
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            string definition = item.GetProperty("definition").GetString()!;
            int size = item.GetProperty("size").GetInt32();
            byte fill = (byte)item.GetProperty("fill").GetInt32();
            byte[] bytes = new byte[size];
            Array.Fill(bytes, fill);
            ReaderParityTests.RunParity("bench-" + item.GetProperty("name").GetString(), definition, Unaligned, "root", bytes, NoVariables, null, null);
            cases++;
        }

        Assert.AreEqual(12, cases);
    }

    private static void Run(string id, string definition, string hex, IReadOnlyDictionary<string, int>? variables = null, string? expectedError = null)
    {
        byte[] bytes = Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
        ReaderParityTests.RunParity(id, definition, Unaligned, "root", bytes, variables ?? NoVariables, null, expectedError);
    }
}
