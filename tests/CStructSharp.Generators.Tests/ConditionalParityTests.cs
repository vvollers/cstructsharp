namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Conditional groups (<c>if</c>/<c>else</c>, <c>switch</c>) through the generated readers, with the runtime as
///     the oracle: the guide's cases (decisions per array item, evaluate-once, unavailable locals, nested groups
///     skipped with their outer arm, switch without a match), wide selectors, and the variable rules a conditional
///     layout leans on (caller variables override defines; a local hides them). The benchmark harness's conditional
///     cases are compared by <c>CStructSharp.Generated.Parity</c>.
/// </summary>
[TestClass]
public class ConditionalParityTests
{
    private const string Unaligned = "Root = \"root\", PointerSize = 4, Aligned = false, LittleEndian = true";

    private static readonly IReadOnlyDictionary<string, int> NoVariables = new Dictionary<string, int>();

    /// <summary>
    ///     Generated readers decode the guide's conditional cases - per-item decisions, evaluate-once, unavailable
    ///     locals, nested groups, and unmatched switches - with the runtime's values and failures.
    /// </summary>
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

    /// <summary>
    ///     Generated readers apply caller variables over defines, enum constants, and selector expression failures as
    ///     the runtime does.
    /// </summary>
    [TestMethod]
    public void VariableRules_MatchTheRuntime()
    {
        // A caller variable overrides a define of the same name, and a define that depends on it follows.
        const string Defines = "#define MODE 1\n#define WIDTH (MODE * 2)\nstruct root { uint8 head; if (MODE == 1) { uint8 one; } else { uint8 two[WIDTH]; } };";
        Run("define-default", Defines, "07 2a");
        Run("define-overridden", Defines, "07 01 02 03 04", new Dictionary<string, int> { ["MODE"] = 2 });

        // A qualified enum member as a constant; a 64-bit define is an ordinary constant, and one beyond the 128-bit
        // domain fails only when selected.
        Run("enum-constant", "enum kind : uint8 { A = 1, B = 2 }; struct root { uint8 tag; if (tag == kind.B) { uint8 b; } else { uint8 other; } };", "02 2a");
        Run("enum-constant-else", "enum kind : uint8 { A = 1, B = 2 }; struct root { uint8 tag; if (tag == kind.B) { uint8 b; } else { uint8 other; } };", "01 2a");
        Run("wide-define", "#define BIG 0xFFFFFFFFFFFFFFFF\nstruct root { uint8 tag; if (BIG > tag) { uint8 a; } else { uint8 b; } };", "01 2a");
        Run("wider-define", "#define BIG 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF\nstruct root { uint8 tag; if (BIG > tag) { uint8 a; } else { uint8 b; } };", "01 2a", expectedError: "CStructReadException");
        Run("define-beyond-64", "#define HUGE (1 << 100)\nstruct root { uint8 tag; if ((tag << 100) == HUGE) { uint8 a; } else { uint8 b; } };", "01 2a");
        Run("literal-beyond-64", "struct root { uint8 tag; if ((tag << 100) == 1267650600228229401496703205376) { uint8 a; } else { uint8 b; } };", "01 2a");
        Run("literal-beyond-128", "struct root { uint8 tag; if (tag && 0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF) { uint8 a; } else { uint8 b; } };", "01 2a", expectedError: "CStructReadException");

        // An expression failure in a selector names the operation and keeps the enclosing member.
        Run("selector-divide", "struct entry { uint8 d; if (8 / d > 1) { uint8 a; } else { uint8 b; } }; struct root { entry items[1]; };", "00 2a", expectedError: "CStructReadException");
        Run("selector-overflow", "struct root { uint64 big; if (big * big > 0) { uint8 a; } else { uint8 b; } };", "ff ff ff ff ff ff ff ff 2a", expectedError: "CStructReadException");
        Run("selector-wide", "struct root { uint128 big; if (big > 0) { uint8 a; } else { uint8 b; } };", "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 80 2a", expectedError: "CStructReadException");
        Run("selector-shift-count", "struct root { uint8 n; if (1 << n) { uint8 a; } else { uint8 b; } };", "80 2a", expectedError: "CStructReadException");
    }

    /// <summary>
    ///     Generated readers evaluate selectors in the 128-bit domain as the runtime does: a <c>uint64</c> above 2^63 is
    ///     nonzero and positive, and a switch matches labels over the whole 64-bit range.
    /// </summary>
    [TestMethod]
    public void WideSelectors_MatchTheRuntime()
    {
        const string Node = "struct root { uint64 next; if (next != 0) { uint32 payload; } uint8 tail; };";
        Run("next-kernel", Node, "00 10 00 00 00 80 ff ff ef be ad de 5a");
        Run("next-null", Node, "00 00 00 00 00 00 00 00 5a");
        Run("next-positive", "struct root { uint64 size; if (size > 0) { uint8 a; } else { uint8 b; } };", "ff ff ff ff ff ff ff ff 2a");
        Run("pointer-nonzero", "struct root { uint8 *p; if (p != 0) { uint8 a; } else { uint8 b; } };", "00 00 00 00 01 00 00 00 2a");

        const string Switch = "struct root { uint64 tag; switch (tag) { case 0xFFFFFFFFFFFFFFFF: { uint8 all; } case 0x8000000000000000: { uint16 top; } case 1: { uint8 one; } default: { uint32 other; } } };";
        Run("switch-all", Switch, "ff ff ff ff ff ff ff ff 07");
        Run("switch-top", Switch, "00 00 00 00 00 00 00 80 07 08");
        Run("switch-one", Switch, "01 00 00 00 00 00 00 00 07");
        Run("switch-default", Switch, "02 00 00 00 00 00 00 00 07 08 09 0a");
        Run("switch-enum-wide", "enum big : uint64 { Low = 1, High = 0xFFFFFFFFFFFFFFFE }; struct root { big e; switch (e) { case big.High: { uint8 high; } default: { uint8 other; } } };", "fe ff ff ff ff ff ff ff 07");
    }

    /// <summary>Compares the generated reader with the runtime for one unaligned little-endian case.</summary>
    /// <param name="id">The case name used in failure messages and the generated class name.</param>
    /// <param name="definition">The layout text; its root struct is <c>root</c>.</param>
    /// <param name="hex">The input bytes as hex digits; spaces are ignored.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="expectedError">The exception type name both must throw, or <see langword="null"/>.</param>
    private static void Run(string id, string definition, string hex, IReadOnlyDictionary<string, int>? variables = null, string? expectedError = null)
    {
        byte[] bytes = Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
        ReaderParityTests.RunParity(id, definition, Unaligned, "root", bytes, variables ?? NoVariables, null, expectedError);
    }
}
