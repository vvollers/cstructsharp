namespace CStructSharp.Generators.Tests;

using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>
///     CSG021 and CSG022: a layout generated as classes on a surface that could take a cheaper shape is told so, as information; a layout that
///     chose a shape, or cannot have a cheaper one, is not.
/// </summary>
[TestClass]
public class ShapeSuggestionTests
{
    private const string Header = "using CStructSharp;\nnamespace Demo;\n";

    /// <summary>A one-number layout qualifies for Scalar, and the message names the value type.</summary>
    [TestMethod]
    public void OneNumber_SuggestsScalar()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + "[CStructLayout(\"struct s { uint16 a; };\", Root = \"s\", Surface = GeneratedSurface.Parser)]\npublic static partial class Magic { }");
        Diagnostic suggestion = Assert.ContainsSingle(result.DiagnosticsWithId("CSG021"));
        Assert.AreEqual(DiagnosticSeverity.Info, suggestion.Severity);
        StringAssert.Contains(suggestion.GetMessage(), "'Magic'");
        Assert.IsEmpty(result.DiagnosticsWithId("CSG022"));
    }

    /// <summary>A fixed struct of several members qualifies for Struct.</summary>
    [TestMethod]
    public void FixedStruct_SuggestsStruct()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + "[CStructLayout(\"struct s { uint16 a; uint32 b; };\", Root = \"s\", Surface = GeneratedSurface.Reader)]\npublic static partial class Pair { }");
        Assert.ContainsSingle(result.DiagnosticsWithId("CSG022"));
        Assert.IsEmpty(result.DiagnosticsWithId("CSG021"));
    }

    /// <summary>A variable-size layout, the Full surface, and a layout that already chose a shape are not told anything.</summary>
    [TestMethod]
    public void NoSuggestion_WhenNothingCheaperFitsOrAShapeWasChosen()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """"
            [CStructLayout("struct s { uint8 n; uint8 d[n]; };", Root = "s", Surface = GeneratedSurface.Parser)]
            public static partial class Variable { }

            [CStructLayout("struct s { uint16 a; uint32 b; };", Root = "s")]
            public static partial class Everything { }

            [CStructLayout("struct s { uint16 a; uint32 b; };", Root = "s", Surface = GeneratedSurface.Parser, Shape = GeneratedShape.Auto)]
            public static partial class Chosen { }
            """");
        Assert.IsEmpty(result.AllDiagnostics.Where(diagnostic => diagnostic.Id is "CSG021" or "CSG022"), string.Join("\n", result.AllDiagnostics));
    }
}
