extern alias Generators;

namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using LayoutUsageAnalyzer = Generators::CStructSharp.Generators.LayoutUsageAnalyzer;

/// <summary>
///     CSG016 for a layout class nothing references and CSG017 for one that generates more than the members used need: reported only
///     for a class no other assembly can see and whose uses are all visible, and checked against what each surface really generates.
/// </summary>
[TestClass]
public class LayoutUsageAnalyzerTests
{
    private const string Header = """
        using System;
        using CStructSharp;

        namespace Demo;

        """;

    /// <summary>Runs the analyzer over a source and its generated output.</summary>
    /// <param name="source">The consumer source.</param>
    /// <param name="globalOptions">The build properties.</param>
    /// <returns>The analyzer's diagnostics, in source order.</returns>
    private static async Task<IReadOnlyList<Diagnostic>> Analyze(string source, IReadOnlyDictionary<string, string>? globalOptions = null)
    {
        GeneratorResult generated = GeneratorRunner.Run(source, globalOptions: globalOptions);
        Assert.IsEmpty(generated.Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), string.Join("\n", generated.Output.GetDiagnostics()));
        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, GeneratorRunner.OptionsProvider(globalOptions, null));
        CompilationWithAnalyzers withAnalyzers = generated.Output.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutUsageAnalyzer()), options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToList();
    }

    /// <summary>A read-only layout lists the members no code reads; a member that is read, a conditional presence flag and a layout that writes are not listed.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task UnreadMembers_AreListed_ForAReadOnlyLayout()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct root { uint8 kind; uint8 reserved[3]; uint8 present; uint8 extra @expect(present != 2); uint32 length; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            internal static partial class Header { }

            [CStructLayout("struct root { uint8 kind; uint8 reserved[3]; };", Root = "root", Views = false)]
            internal static partial class Writer { }

            public static class Program
            {
                public static uint Use(byte[] bytes)
                {
                    Header.Root root = Header.Parse(bytes);
                    return root.Length + root.Kind;
                }

                public static int Write(byte[] bytes) => Writer.Serialize(Writer.Parse(bytes)).Length;
            }
            """");
        Diagnostic unread = Assert.ContainsSingle(diagnostics.Where(diagnostic => diagnostic.Id == "CSG023"), string.Join("\n", diagnostics));
        StringAssert.Contains(unread.GetMessage(), "'Header'");
        StringAssert.Contains(unread.GetMessage(), "Root.Reserved");
        StringAssert.Contains(unread.GetMessage(), "Root.Present");
        StringAssert.Contains(unread.GetMessage(), "Root.Extra");
        Assert.IsFalse(unread.GetMessage().Contains("Root.Length", StringComparison.Ordinal) || unread.GetMessage().Contains("Root.Kind", StringComparison.Ordinal), unread.GetMessage());
        Assert.AreEqual(DiagnosticSeverity.Info, unread.Severity);
    }

    /// <summary>A layout class nothing names is reported once; a used one, a public one and one used by name only are not.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Unused_IsReported_ButNotAUsedOrPublicLayout()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            internal static partial class Dead { }

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            internal static partial class Live { }

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            public static partial class Exposed { }

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            internal static partial class Named { }

            public static class Program
            {
                public static uint Use(byte[] bytes) => Live.Parse(bytes).A;

                public static string Name() => nameof(Named);
            }

            public static class Holder
            {
                [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
                private static partial class Inside { }
            }
            """");
        string[] unused = diagnostics.Where(diagnostic => diagnostic.Id == "CSG016").Select(diagnostic => diagnostic.GetMessage()).ToArray();
        Assert.AreEqual(2, unused.Length, string.Join("\n", diagnostics));
        StringAssert.Contains(unused[0], "'Dead'");
        StringAssert.Contains(unused[1], "'Inside'");
        Assert.AreEqual(DiagnosticSeverity.Info, diagnostics.First(diagnostic => diagnostic.Id == "CSG016").Severity);
    }

    /// <summary>A layout used only through members every surface has is advised the Views surface, and not also <c>Views = false</c>, which that surface cannot combine with.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Overgeneration_NeverAdvisesViewsFalseWithTheViewsSurface()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct root { uint32 a; };", Root = "root")]
            internal static partial class DefinitionOnly { }

            public static class Program
            {
                public static string Use() => DefinitionOnly.Definition;
            }
            """");
        string message = diagnostics.Single(diagnostic => diagnostic.Id == "CSG017").GetMessage();
        StringAssert.Contains(message, "Surface = GeneratedSurface.Views");
        StringAssert.DoesNotMatch(message, new System.Text.RegularExpressions.Regex("Views = false"));
    }

    /// <summary>A layout that generates more than its uses need says which surface and whether the views are dead weight.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task Overgeneration_SuggestsTheSmallestSurfaceAndNoViews()
    {
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Header + """"
            [CStructLayout("struct root { uint32 a; };", Root = "root")]
            internal static partial class OnlyParse { }

            [CStructLayout("struct root { uint32 a; };", Root = "root")]
            internal static partial class TryToo { }

            [CStructLayout("struct root { uint32 a; };", Root = "root")]
            internal static partial class UsesLayout { }

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser)]
            internal static partial class UsesView { }

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser)]
            internal static partial class ParserWithViews { }

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            internal static partial class Exact { }

            public static class Program
            {
                public static void Use(byte[] bytes)
                {
                    ReadOnlySpan<byte> span = bytes;
                    _ = OnlyParse.Parse(span);
                    _ = OnlyParse.Parse(span);
                    _ = TryToo.Parse(span);
                    _ = TryToo.TryParse(span, out var value);
                    _ = UsesLayout.Layout;
                    _ = new UsesView.RootView(bytes).A;
                    _ = ParserWithViews.Parse(span);
                    _ = Exact.Parse(span);
                }
            }
            """");
        Dictionary<string, string> messages = diagnostics.Where(diagnostic => diagnostic.Id == "CSG017").ToDictionary(diagnostic => diagnostic.GetMessage().Split('\'')[1], diagnostic => diagnostic.GetMessage());
        StringAssert.Contains(messages["OnlyParse"], "used only through Parse;", "a member used twice is named once");
        StringAssert.Contains(messages["OnlyParse"], "Surface = GeneratedSurface.Parser");
        StringAssert.Contains(messages["OnlyParse"], "Views = false");
        StringAssert.Contains(messages["TryToo"], "Surface = GeneratedSurface.Reader");
        StringAssert.Contains(messages["TryToo"], "Views = false");
        StringAssert.Contains(messages["UsesLayout"], "Views = false");
        StringAssert.DoesNotMatch(messages["UsesLayout"], new System.Text.RegularExpressions.Regex("Surface ="));
        StringAssert.Contains(messages["ParserWithViews"], "Views = false");
        StringAssert.DoesNotMatch(messages["ParserWithViews"], new System.Text.RegularExpressions.Regex("Surface ="));
        StringAssert.Contains(messages["UsesView"], "Surface = GeneratedSurface.Views");
        StringAssert.DoesNotMatch(messages["UsesView"], new System.Text.RegularExpressions.Regex("Views = false"));
        Assert.IsFalse(messages.ContainsKey("Exact"));
    }

    /// <summary>A project default surface and views setting count as what the layout generates.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task ProjectDefaults_AreWhatALayoutGenerates()
    {
        var options = new Dictionary<string, string> { ["build_property.CStructSharpSurface"] = "Parser", ["build_property.CStructSharpViews"] = "false" };
        const string Source = Header + """"
            [CStructLayout("struct root { uint32 a; };", Root = "root")]
            internal static partial class Plain { }

            public static class Program
            {
                public static uint Use(byte[] bytes) => Plain.Parse(bytes).A;
            }
            """";
        IReadOnlyList<Diagnostic> diagnostics = await Analyze(Source, options);
        Assert.IsEmpty(diagnostics.Where(diagnostic => diagnostic.Id is "CSG016" or "CSG017"), string.Join("\n", diagnostics));
    }

    /// <summary>A friend assembly may use an internal layout, so nothing is reported unless the project says to analyze despite friends.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task FriendAssemblies_SilenceTheAnalysisUnlessAsked()
    {
        const string Source = """"
            using CStructSharp;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Friend")]

            namespace Demo;

            [CStructLayout("struct root { uint32 a; };", Root = "root", Surface = GeneratedSurface.Parser, Views = false)]
            internal static partial class Dead { }
            """";
        Assert.IsEmpty((await Analyze(Source)).Where(diagnostic => diagnostic.Id == "CSG016"));
        var options = new Dictionary<string, string> { ["build_property.CStructSharpAnalyzeWithFriends"] = "true" };
        Assert.AreEqual(1, (await Analyze(Source, options)).Count(diagnostic => diagnostic.Id == "CSG016"));
    }

    /// <summary>A layout that is named as a whole, or that a mapped class may resolve, is never reported.</summary>
    /// <returns>A task that completes after the diagnostics are checked.</returns>
    [TestMethod]
    public async Task WholeTypeUses_AndMappedClasses_AreNotReported()
    {
        IReadOnlyList<Diagnostic> whole = await Analyze(Header + """"
            [CStructLayout("struct root { uint32 a; };", Root = "root")]
            internal static partial class Reflected { }

            public static class Program
            {
                public static Type Use() => typeof(Reflected);
            }
            """");
        Assert.IsEmpty(whole.Where(diagnostic => diagnostic.Id is "CSG016" or "CSG017"), string.Join("\n", whole));

        IReadOnlyList<Diagnostic> mapped = await Analyze(Header + """"
            [CStructLayout("struct header { uint16 length; };", Root = "header")]
            internal static partial class Wire { }

            [CStructMapped(Layout = "header")]
            public partial class HeaderModel
            {
                public ushort Length { get; set; }
            }
            """");
        Assert.IsEmpty(mapped.Where(diagnostic => diagnostic.Id is "CSG016" or "CSG017"), string.Join("\n", mapped));
    }

    /// <summary>
    ///     The table that maps a used member to the surfaces that generate it matches the generator: for a rich layout, the set of
    ///     surfaces whose output has each member of the full surface is the mask the analyzer assigns it.
    /// </summary>
    [TestMethod]
    public void SurfaceTable_MatchesWhatEachSurfaceGenerates()
    {
        const string Layout = "enum kind : uint8 { A = 1, B = 2 }; struct inner { uint16 a; uint8 b; }; struct root { uint32 id; kind k; inner nested; uint8 n; uint8 data[n]; char name[4]; };";
        GeneratedSurfaceCase[] surfaces =
        [
            new("Views", 0),
            new("Parser", 1),
            new("Reader", 2),
            new("Full", 3),
        ];
        var perSurface = new Dictionary<int, Dictionary<string, ISymbol>>();
        foreach (GeneratedSurfaceCase surface in surfaces)
        {
            GeneratorResult result = GeneratorRunner.Run(Header + "[CStructLayout(\"" + Layout + "\", Root = \"root\", Surface = GeneratedSurface." + surface.Name + ")]\npublic static partial class Probe { }").AssertClean();
            INamedTypeSymbol type = result.Output.GetTypeByMetadataName("Demo.Probe")!;
            perSurface[surface.Rank] = type.GetMembers().Where(member => member.DeclaredAccessibility == Accessibility.Public && !member.IsImplicitlyDeclared && member is not IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor }).ToDictionary(Key, member => member);
        }

        Assert.IsGreaterThan(20, perSurface[3].Count);
        var mismatches = new List<string>();
        foreach ((string key, ISymbol symbol) in perSurface[3])
        {
            int generatedBy = surfaces.Where(surface => perSurface[surface.Rank].ContainsKey(key)).Sum(surface => 1 << surface.Rank);
            if (generatedBy != LayoutUsageAnalyzer.SurfaceMask(symbol))
            {
                mismatches.Add(key + " (" + symbol.Kind + "): generated by mask " + generatedBy + ", table says " + LayoutUsageAnalyzer.SurfaceMask(symbol));
            }
        }

        Assert.IsEmpty(mismatches, string.Join("\n", mismatches));
    }

    /// <summary>A member's identity across surfaces: its name, and for a method its parameter types.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The key.</returns>
    private static string Key(ISymbol member)
        => member is IMethodSymbol method ? member.Name + "(" + string.Join(",", method.Parameters.Select(parameter => parameter.Type.ToDisplayString())) + ")" : member.Name;

    /// <summary>A surface the table test generates, with its rank.</summary>
    /// <param name="Name">The <c>GeneratedSurface</c> member.</param>
    /// <param name="Rank">The rank the analyzer gives it.</param>
    private sealed record GeneratedSurfaceCase(string Name, int Rank);
}
