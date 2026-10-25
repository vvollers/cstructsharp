extern alias Generators;

namespace CStructSharp.Generators.Tests;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using CStructAnalyzer = Generators::CStructSharp.Generators.CStructAnalyzer;

/// <summary>
///     CSG018 (conditions on one member that no value can satisfy) and CSG019 (one label for different conditions): found in the
///     layout text of an attribute and of a <c>new CStruct(...)</c> literal, and silent for everything the analysis cannot prove.
/// </summary>
[TestClass]
public class ConstraintAnalyzerTests
{
    private const string Header = """
        using CStructSharp;

        namespace Demo;

        """;

    /// <summary>
    ///     The XFS superblock carries two magic numbers in different structs (the filesystem's and its secondary-superblock record's)
    ///     under one label: CSG019, and nothing else for the satisfiable conditions.
    /// </summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Csg019_ReportsTheXfsTwoMagicLayout_WithOneLabelForTwoConditions()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("""
                struct xfs_sb { uint32 sb_magicnum @expect(sb_magicnum == 0x58465342, "xfs.magic"); uint32 sb_blocksize; };
                struct xfs_agf { uint32 agf_magicnum @expect(agf_magicnum == 0x58414746, "xfs.magic"); uint32 agf_seqno; };
                """, Root = "xfs_sb")]
            public static partial class Xfs { }
            """");

        Diagnostic only = Assert.ContainsSingle(diagnostics, diagnostics.ToString());
        Assert.AreEqual("CSG019", only.Id);
        Assert.AreEqual(DiagnosticSeverity.Info, only.Severity);
        StringAssert.Contains(only.GetMessage(), "'xfs.magic'");
        StringAssert.Contains(only.GetMessage(), "xfs_sb.sb_magicnum");
        StringAssert.Contains(only.GetMessage(), "xfs_agf.agf_magicnum");
    }

    /// <summary>The same condition repeated under one label is one idea, not a clash.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Csg019_IgnoresTheSameConditionRepeated()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct a { uint8 v @expect(v == 1, \"one\"); }; struct b { uint8 v @expect(v == 1, \"one\"); };")]
            public static partial class Same { }
            """");

        Assert.IsEmpty(diagnostics, string.Join("\n", diagnostics));
    }

    /// <summary>Conditions that exclude each other, or a bound outside the type, are CSG018 (a warning) in an attribute and in a construction.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Csg018_ReportsConditionsNoValueSatisfies()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("#define LIMIT 300\nstruct a { uint8 kind @expect(kind == 1) @expect(kind == 2); uint8 big @expect(big > LIMIT); int8 s @expect(s < -128); uint16 ok @expect(ok > 5 && ok < 10); };")]
            public static partial class Attr { }

            public static class Program
            {
                public static CStruct Make() => new CStruct("struct b { uint32 v @supports(v >= 4 && v <= 2); };");
                public static CStruct Fine() => CStruct.GetOrCompile("struct c { uint32 v @supports(v != 0) @expect(v < 100 || v > 200); };");
            }
            """");

        string[] messages = diagnostics.Where(diagnostic => diagnostic.Id == "CSG018").Select(diagnostic => diagnostic.GetMessage()).ToArray();
        Assert.AreEqual(4, messages.Length, string.Join("\n", diagnostics));
        Assert.IsTrue(diagnostics.All(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
        StringAssert.Contains(messages[0], "'a.kind'");
        StringAssert.Contains(messages[0], "kind == 1' and 'kind == 2");
        StringAssert.Contains(messages[1], "'a.big'");
        StringAssert.Contains(messages[1], "uint8 (0 to 255)");
        StringAssert.Contains(messages[2], "'a.s'");
        StringAssert.Contains(messages[3], "'b.v'");
        Assert.AreEqual(4, diagnostics.Count);
    }

    /// <summary>Conditions the analysis cannot reduce (other members, masks, arithmetic on the member) and layouts that do not compile stay silent.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Csg018_StaysSilentWhenItCannotProve()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct a { uint8 lo; uint8 hi @expect(hi > lo) @expect(hi < 1); uint8 flags @expect(flags & 0x80 == 0x80) @expect(flags == 3); float32 f @expect(f > 1); uint8 many[4] @expect(many[0] == 1); };")]
            public static partial class Open { }

            public static class Program
            {
                public static CStruct Broken(string text) => new CStruct(text);
            }
            """");

        Assert.IsEmpty(diagnostics, string.Join("\n", diagnostics));
    }

    /// <summary>A bitfield's range is its width: five bits hold 0 to 31.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Csg018_UsesTheWidthOfABitfield()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct a { uint8 low : 5 @expect(low == 31); uint8 high : 3 @expect(high > 7); };")]
            public static partial class Bits { }
            """");

        Diagnostic only = Assert.ContainsSingle(diagnostics, string.Join("\n", diagnostics));
        Assert.AreEqual("CSG018", only.Id);
        StringAssert.Contains(only.GetMessage(), "'a.high'");
        StringAssert.Contains(only.GetMessage(), "uint3 (0 to 7)");
    }

    /// <summary>A magic after a variable-size member is CSG020; one at a fixed offset is not.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Csg020_ReportsAMagicAfterAVariableSizeMember()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct late { uint8 n; uint8 data[n]; uint16 magic @magic(magic == 0xEF53, \"late.magic\"); };")]
            public static partial class Late { }

            [CStructLayout("struct early { uint16 magic @magic(magic == 0xEF53, \"early.magic\"); uint8 n; uint8 data[n]; };")]
            public static partial class Early { }
            """");

        Diagnostic only = Assert.ContainsSingle(diagnostics, string.Join("\n", diagnostics));
        Assert.AreEqual("CSG020", only.Id);
        Assert.AreEqual(DiagnosticSeverity.Warning, only.Severity);
        StringAssert.Contains(only.GetMessage(), "'late.magic'");
    }

    /// <summary>Runs the analyzer over a consumer source.</summary>
    /// <param name="source">The consumer source.</param>
    /// <returns>The analyzer diagnostics in source order.</returns>
    private static async Task<IReadOnlyList<Diagnostic>> Analyze(string source)
    {
        GeneratorResult generated = GeneratorRunner.Run(source);
        Assert.IsEmpty(generated.Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), string.Join("\n", generated.Output.GetDiagnostics()));
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, GeneratorRunner.OptionsProvider(null, null));
        CompilationWithAnalyzers withAnalyzers = generated.Output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new CStructAnalyzer()), options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToList();
    }
}
