namespace CStructSharp.Generators.Tests;

/// <summary>
///     Layouts at the edges of the newer generated members and shapes: names that a generated method would share, an enum of
///     <c>int8</c> in a value type, a class the consumer declared for a type that Auto would make a struct, and a constraint
///     written across lines.
/// </summary>
[TestClass]
public class ReviewFollowUpTests
{
    private const string Header = "using CStructSharp;\n\nnamespace Demo;\n\n";

    /// <summary>A fixed array of an enum stored in <c>int8</c> fills its inline array and compiles.</summary>
    [TestMethod]
    public void ValueType_HoldsAnArrayOfInt8Enums()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + "[CStructLayout(\"enum e : int8 { A = 0, B = 1 }; struct s { e v[4]; };\", Root = \"s\", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Struct)]\npublic static partial class Int8Enums { }\n").AssertClean();
        Assert.AreEqual(0, result.DiagnosticsWithId("CSG014").Count);
    }

    /// <summary>A struct named like the <c>Matches</c> method of a layout with conditions is reported as a name collision, not left to fail in the generated file.</summary>
    [TestMethod]
    public void Matches_NameCollision_IsReported()
    {
        GeneratorResult named = GeneratorRunner.Run(Header + "[CStructLayout(\"struct matches { uint8 a @expect(a == 1); };\", Root = \"matches\", Surface = GeneratedSurface.Parser)]\npublic static partial class Clash { }\n");
        Assert.IsTrue(named.DiagnosticsWithId("CSG003").Count > 0);

        GeneratorResult suffixed = GeneratorRunner.Run(Header + "[CStructLayout(\"struct hdr { uint8 a @expect(a == 1); }; struct matches_hdr { uint8 b; };\", Root = \"hdr\", Surface = GeneratedSurface.Parser)]\npublic static partial class Clash { }\n");
        Assert.IsTrue(suffixed.DiagnosticsWithId("CSG003").Count > 0);

        // Without conditions nothing is generated under that name.
        GeneratorRunner.Run(Header + "[CStructLayout(\"struct matches { uint8 a; };\", Root = \"matches\", Surface = GeneratedSurface.Parser)]\npublic static partial class Fine { }\n").AssertClean();
    }

    /// <summary>A member named like the view's <c>TryValidate</c> gets that name, and the generated check takes another.</summary>
    [TestMethod]
    public void TryValidate_NameCollision_IsAvoided()
    {
        _ = GeneratorRunner.Run(Header + "[CStructLayout(\"struct s { uint8 try_validate @expect(try_validate == 1); };\", Root = \"s\")]\npublic static partial class Viewed { }\n").AssertClean();
    }

    /// <summary>Auto keeps a class where the code declares a class for the type; Struct says why it cannot.</summary>
    [TestMethod]
    public void ConsumerClass_KeepsAutoOnTheClassShape_AndStructReportsIt()
    {
        const string Extension = "public static partial class Wire { public sealed partial class Hdr { public int Twice => 2; } }\n";
        GeneratorResult auto = GeneratorRunner.Run(Header + "[CStructLayout(\"struct hdr { uint32 a; };\", Root = \"hdr\", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Auto)]\n" + Extension).AssertClean();
        StringAssert.Contains(auto.Source, "partial class Hdr");

        GeneratorResult forced = GeneratorRunner.Run(Header + "[CStructLayout(\"struct hdr { uint32 a; };\", Root = \"hdr\", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Struct)]\n" + Extension);
        Assert.AreEqual(1, forced.DiagnosticsWithId("CSG014").Count);
    }

    /// <summary>A condition written across lines stays in its comment and the generated code compiles.</summary>
    [TestMethod]
    public void MultiLineConstraint_CompilesInReadersAndMatches()
    {
        _ = GeneratorRunner.Run(Header + "[CStructLayout(\"struct s { uint16 m @expect(m == 1 ||\\n        m == 2); };\", Root = \"s\", Surface = GeneratedSurface.Parser)]\npublic static partial class Lines { }\n").AssertClean();
    }
}
