extern alias Generators;

namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using CStructAnalyzer = Generators::CStructSharp.Generators.CStructAnalyzer;

/// <summary>
///     The analyzer: CSG200 for a constant path that cannot resolve against a visibly constructed layout (a
///     local, a field, an inline construction, a generated class's <c>Layout</c>), CSG201 for <c>Parse</c> on a
///     union or scalar root, CSG300 for <c>dynamic</c> over a parsed value when the project publishes trimmed or
///     AOT - and silence wherever the layout is not plainly visible.
/// </summary>
[TestClass]
public class AnalyzerTests
{
    private const string Header = """
        using CStructSharp;
        using CStructSharp.Values;

        namespace Demo;

        """;

    [TestMethod]
    public async Task Csg200_ReportsUnresolvablePaths_AndStaysSilentWithoutAVisibleLayout()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            public static class Program
            {
                private static readonly CStruct Field = new CStruct("struct hdr { uint8 kind; uint16 len; }; struct root { hdr h; uint8 items[4]; uint16 *link; };");

                public static void Main(byte[] bytes, CStruct unknown, string dynamicPath)
                {
                    var local = new CStruct("struct root { uint8 tag; };");
                    var value = local.Parse(bytes, "root");
                    local.Parse(bytes, "roof");
                    Field.ReadValue(bytes, "root.h.kind");
                    Field.ReadValue(bytes, "root.h.size");
                    Field.ReadValue(bytes, "root.items[1][2]");
                    Field.ReadValue(bytes, "root.tag[0]");
                    Field.ReadValue(bytes, "root.link.value");
                    Field.ReadValue(bytes, "root.link.target");
                    Field.ReadValue(bytes, "root..h");
                    new CStruct("struct root { uint8 a; };").GetArrayLength(bytes, "root.b");
                    CStruct.GetOrCompile("struct root { uint8 a; };").ResolveAddress(bytes, "other");
                    unknown.Parse(bytes, "anything");
                    local.Parse(bytes, dynamicPath);
                    Packet.Layout.Parse(bytes, "root.missing");
                    Packet.Layout.Parse(bytes, "root");
                }
            }

            [CStructLayout("struct root { uint8 a; };")]
            public static partial class Packet { }
            """");

        string[] paths = diagnostics.Where(diagnostic => diagnostic.Id == "CSG200").Select(diagnostic => diagnostic.GetMessage()).ToArray();
        Assert.AreEqual(9, paths.Length, string.Join("\n", diagnostics));
        StringAssert.Contains(paths[0], "'roof'");
        StringAssert.Contains(paths[0], "declared: root");
        StringAssert.Contains(paths[1], "'hdr' has no member 'size'");
        StringAssert.Contains(paths[2], "'items' has 1 dimension(s), not 2");
        StringAssert.Contains(paths[3], "'root' has no member 'tag'");
        StringAssert.Contains(paths[4], "'link' is a pointer");
        StringAssert.Contains(paths[5], "empty segment");
        StringAssert.Contains(paths[6], "'root' has no member 'b'");
        StringAssert.Contains(paths[7], "'other'");
        StringAssert.Contains(paths[8], "'root' has no member 'missing'");
        Assert.AreEqual(0, diagnostics.Count(diagnostic => diagnostic.Id is "CSG201" or "CSG300"));
    }

    [TestMethod]
    public async Task Csg201_ReportsParseOnUnionsAndScalars()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """
            public static class Program
            {
                public static void Main(byte[] bytes)
                {
                    var layout = new CStruct("union choice { uint8 a; uint16 b; }; typedef uint32 word; struct root { choice c; };");
                    layout.Parse(bytes, "choice");
                    layout.Parse(bytes, "word");
                    layout.Parse(bytes, "root");
                    layout.ReadValue(bytes, "choice");
                    layout.ParseMany(bytes, "choice");
                    layout.ParseManyAsync(new System.IO.MemoryStream(bytes), "word");
                    layout.ParseAsync(new System.IO.MemoryStream(bytes), "root");
                    layout.ParseMany(bytes, "root.missing");
                }
            }
            """);

        string[] messages = diagnostics.Where(diagnostic => diagnostic.Id == "CSG201").Select(diagnostic => diagnostic.GetMessage()).ToArray();
        Assert.AreEqual(4, messages.Length, string.Join("\n", diagnostics));
        StringAssert.Contains(messages[0], "'choice' is a union");
        StringAssert.Contains(messages[1], "'word' is not a struct");
        StringAssert.Contains(messages[2], "'choice' is a union");
        StringAssert.Contains(messages[3], "'word' is not a struct");
        StringAssert.Contains(diagnostics.Single(diagnostic => diagnostic.Id == "CSG200").GetMessage(), "'root' has no member 'missing'");
        Assert.AreEqual(DiagnosticSeverity.Info, diagnostics.First(diagnostic => diagnostic.Id == "CSG201" && diagnostic.GetMessage().Contains("choice", StringComparison.Ordinal)).Severity);
    }

    [TestMethod]
    public async Task Csg300_ReportsDynamicOnlyWhenPublishingTrimmedOrAot()
    {
        const string Source = Header + """
            public static class Program
            {
                public static object Main(byte[] bytes)
                {
                    var layout = new CStruct("struct root { uint8 a; };");
                    StructValue parsed = layout.Parse(bytes, "root");
                    dynamic view = parsed;
                    dynamic union = (dynamic)UnionValue.FromRaw("choice", bytes);
                    dynamic plain = bytes;
                    return view.a;
                }
            }
            """;
        IReadOnlyList<Diagnostic> silent = await Analyze(Source);
        Assert.AreEqual(0, silent.Count(diagnostic => diagnostic.Id == "CSG300"), string.Join("\n", silent));

        IReadOnlyList<Diagnostic> aot = await Analyze(Source, new Dictionary<string, string> { ["build_property.PublishAot"] = "true" });
        Assert.AreEqual(2, aot.Count(diagnostic => diagnostic.Id == "CSG300"), string.Join("\n", aot));
        StringAssert.Contains(aot.First(diagnostic => diagnostic.Id == "CSG300").GetMessage(), "StructValue");

        IReadOnlyList<Diagnostic> trimmed = await Analyze(Source, new Dictionary<string, string> { ["build_property.PublishTrimmed"] = "True" });
        Assert.AreEqual(2, trimmed.Count(diagnostic => diagnostic.Id == "CSG300"));
    }

    private static async Task<IReadOnlyList<Diagnostic>> Analyze(string source, IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        GeneratorResult generated = GeneratorRunner.Run(source);
        Assert.IsEmpty(generated.Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), string.Join("\n", generated.Output.GetDiagnostics()));
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, GeneratorRunner.OptionsProvider(globalOptions, null));
        CompilationWithAnalyzers withAnalyzers = generated.Output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new CStructAnalyzer()), options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToList();
    }
}
