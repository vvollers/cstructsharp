extern alias Generators;

namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Naming = Generators::CStructSharp.Generators.Naming;

/// <summary>The generator's frame: attribute discovery, the incremental request, the emitted class shell, and the diagnostics CSG001-CSG005/CSG010.</summary>
[TestClass]
public class SkeletonTests
{
    private const string Header = """
        using CStructSharp;

        namespace Demo;

        """;

    [TestMethod]
    public void InlineDefinition_EmitsTheFrameAndTheRuntimeLayout()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            [CStructLayout("struct header { uint16 kind; uint32 length; };", LittleEndian = false, Aligned = true, PointerSize = 4)]
            public static partial class HeaderLayout { }
            """).AssertClean();

        Snapshot.Match("Skeleton.InlineDefinition", result.Source);
        Assert.AreEqual("Demo.HeaderLayout.CStructLayout.g.cs", result.GeneratedSources[0].HintName);

        Type generated = result.Load().GetType("Demo.HeaderLayout")!;
        Assert.AreEqual("struct header { uint16 kind; uint32 length; };", generated.GetField("Definition")!.GetValue(null));
        Assert.AreEqual("header", generated.GetField("RootName")!.GetValue(null));
        var layout = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
        Assert.IsFalse(layout.IsLittleEndian);
        Assert.IsTrue(layout.Aligned);
        Assert.AreEqual(4, layout.PointerSize);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("header"));
        Assert.AreSame(layout, generated.GetProperty("Layout")!.GetValue(null), "the runtime layout is built once");
    }

    [TestMethod]
    public void LayoutFile_IsReadFromAdditionalFiles_AndRootAndDefinedApply()
    {
        GeneratorResult result = GeneratorRunner.Run(
            Header + """
                [CStructLayout(File = "layouts/png.cstruct", Root = "root", Defined = new[] { "WITH_TAIL" }, BitfieldPacking = BitfieldPacking.Msvc, BitfieldAllocation = BitfieldAllocation.HighBitFirst, CLongWidth = 32, KeepNames = true, Views = false)]
                internal static partial class Png { }
                """,
            [("/project/layouts/png.cstruct", "struct chunk { uint32 length; };\n#ifdef WITH_TAIL\nstruct root { chunk first; uint8 tail; };\n#else\nstruct root { chunk first; };\n#endif\n")]).AssertClean();

        Snapshot.Match("Skeleton.LayoutFile", result.Source);
        Type generated = result.Load().GetType("Demo.Png")!;
        Assert.AreEqual("root", generated.GetField("RootName")!.GetValue(null));
        var layout = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
        Assert.AreEqual(5, layout.GetStructSizeInBytes("root"), "the Defined symbol keeps the tail");
        Assert.AreEqual(BitfieldPacking.Msvc, layout.CompilationOptions.BitfieldPacking);
        Assert.AreEqual(32, layout.CompilationOptions.CLongWidth);
    }

    /// <summary>A file the build marks with CStructSharpLayout=true is a layout file whatever its extension; DisableCStructSharpGenerator=true skips generation.</summary>
    [TestMethod]
    public void MarkedFiles_AreLayoutFiles_AndTheDisableSwitchSkipsGeneration()
    {
        const string Consumer = Header + """
            [CStructLayout(File = "layouts/record.layout")]
            public static partial class Record { }
            """;
        var files = new[] { ("/project/layouts/record.layout", "struct root { uint8 a; uint16 b; };") };
        Assert.AreEqual(1, GeneratorRunner.Run(Consumer, files).DiagnosticsWithId("CSG002").Count, "an unmarked file with another extension is not a layout file");

        var marked = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["/project/layouts/record.layout"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["build_metadata.AdditionalFiles.CStructSharpLayout"] = "true" },
        };
        GeneratorResult result = GeneratorRunner.Run(Consumer, files, fileOptions: marked).AssertClean();
        Assert.AreEqual(3, ((CStruct)result.Load().GetType("Demo.Record")!.GetProperty("Layout")!.GetValue(null)!).GetStructSizeInBytes("root"));

        GeneratorResult disabled = GeneratorRunner.Run(Consumer, files, globalOptions: new Dictionary<string, string>(StringComparer.Ordinal) { ["build_property.DisableCStructSharpGenerator"] = "true" }, fileOptions: marked);
        Assert.AreEqual(0, disabled.GeneratedSources.Count);
        Assert.AreEqual(0, disabled.GeneratorDiagnostics.Length);
    }

    [TestMethod]
    public void MissingFile_ReportsCSG002()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            [CStructLayout(File = "missing.cstruct")]
            public static partial class Missing { }
            """);

        Diagnostic diagnostic = result.DiagnosticsWithId("CSG002").Single();
        StringAssert.Contains(diagnostic.GetMessage(), "missing.cstruct");
        Assert.IsEmpty(result.GeneratedSources);
    }

    [TestMethod]
    public void NeitherDefinitionNorFile_ReportsCSG001()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            [CStructLayout]
            public static partial class Empty { }
            """);

        StringAssert.Contains(result.DiagnosticsWithId("CSG001").Single().GetMessage(), "File");
    }

    [TestMethod]
    public void InvalidLayout_ReportsCSG001WithTheRuntimeMessage_LocatedInsideARawLiteral()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + "[CStructLayout(\"\"\"\n    struct ok { uint8 a; };\n    struct bad { uint8 ; };\n    \"\"\")]\npublic static partial class Broken { }\n");

        Diagnostic diagnostic = result.DiagnosticsWithId("CSG001").Single();
        CStructSharp.Diagnostics.CStructLayoutException runtime = Assert.Throws<CStructSharp.Diagnostics.CStructLayoutException>(() => new CStruct("struct ok { uint8 a; };\nstruct bad { uint8 ; };\n"));
        Assert.AreEqual(runtime.Message, diagnostic.GetMessage());
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.AreEqual(5, span.StartLinePosition.Line, "the layout's line 2 is the consumer file's line 6 (zero-based 5)");
        System.Text.RegularExpressions.Match position = System.Text.RegularExpressions.Regex.Match(runtime.Message, @"line (\d+), column (\d+)");
        Assert.AreEqual("2", position.Groups[1].Value);
        Assert.AreEqual(4 + int.Parse(position.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) - 1, span.StartLinePosition.Character, "the layout column plus the raw literal's indentation");
        Assert.IsEmpty(result.GeneratedSources);
    }

    [TestMethod]
    public void InvalidLayout_InARegularLiteral_IsLocatedAtTheArgument()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            [CStructLayout("struct bad { uint8 ; };")]
            public static partial class Broken { }
            """);

        Diagnostic diagnostic = result.DiagnosticsWithId("CSG001").Single();
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.AreEqual("Consumer.cs", span.Path);
        Assert.AreEqual(3, span.StartLinePosition.Line);
        Assert.AreEqual("[CStructLayout(".Length, span.StartLinePosition.Character, "the whole argument, since a regular literal's columns are not the layout's");
    }

    [TestMethod]
    public void NotStaticPartial_ReportsCSG005()
    {
        GeneratorResult notPartial = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 a; };")]
            public static class Plain { }
            """);
        StringAssert.Contains(notPartial.DiagnosticsWithId("CSG005").Single().GetMessage(), "Plain");

        GeneratorResult notStatic = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 a; };")]
            public partial class Instance { }
            """);
        Assert.HasCount(1, notStatic.DiagnosticsWithId("CSG005"));

        GeneratorResult containerNotPartial = GeneratorRunner.Run(Header + """
            public class Outer
            {
                [CStructLayout("struct root { uint8 a; };")]
                public static partial class Inner { }
            }
            """);
        Assert.HasCount(1, containerNotPartial.DiagnosticsWithId("CSG005"));
    }

    [TestMethod]
    public void NestedInPartialContainers_EmitsTheChain()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            public partial class Outer
            {
                internal partial struct Middle
                {
                    [CStructLayout("struct root { uint8 a; };")]
                    private static partial class Inner { }
                }
            }
            """).AssertClean();

        Snapshot.Match("Skeleton.Nested", result.Source);
        Assert.AreEqual("Demo.Outer.Middle.Inner.CStructLayout.g.cs", result.GeneratedSources[0].HintName);
    }

    [TestMethod]
    public void GlobalNamespace_EmitsWithoutANamespaceBlock()
    {
        GeneratorResult result = GeneratorRunner.Run("""
            [CStructSharp.CStructLayout("struct root { uint8 a; };")]
            public static partial class GlobalLayout { }
            """).AssertClean();

        Assert.AreEqual("GlobalLayout.CStructLayout.g.cs", result.GeneratedSources[0].HintName);
        Assert.IsFalse(result.Source.Contains("namespace ", StringComparison.Ordinal));
        Assert.IsNotNull(result.Load().GetType("GlobalLayout"));
    }

    [TestMethod]
    public void UnknownRoot_ReportsCSG004AndUsesTheFirstStruct()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            [CStructLayout("struct first { uint8 a; }; struct second { uint8 b; };", Root = "third")]
            public static partial class Roots { }
            """);

        Diagnostic diagnostic = result.DiagnosticsWithId("CSG004").Single();
        StringAssert.Contains(diagnostic.GetMessage(), "'third'");
        StringAssert.Contains(diagnostic.GetMessage(), "'first'");
        Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        StringAssert.Contains(result.Source, "RootName = \"first\"");
    }

    [TestMethod]
    public void OldLanguageVersion_ReportsCSG010()
    {
        GeneratorResult result = GeneratorRunner.Run(
            Header + """
                [CStructLayout("struct root { uint8 a; };")]
                public static partial class Old { }
                """,
            languageVersion: LanguageVersion.CSharp11);

        StringAssert.Contains(result.DiagnosticsWithId("CSG010").Single().GetMessage(), "11");
        Assert.IsEmpty(result.GeneratedSources);
    }

    [TestMethod]
    public void NameCollision_ReportsCSG003()
    {
        GeneratorResult collision = GeneratorRunner.Run(Header + """
            [CStructLayout("struct a_b { uint8 x; }; struct aB { uint8 y; };")]
            public static partial class Names { }
            """);
        StringAssert.Contains(collision.DiagnosticsWithId("CSG003").Single().GetMessage(), "KeepNames");
        Assert.IsEmpty(collision.GeneratedSources);

        GeneratorResult container = GeneratorRunner.Run(Header + """
            [CStructLayout("struct names { uint8 x; };")]
            public static partial class Names { }
            """);
        StringAssert.Contains(container.DiagnosticsWithId("CSG003").Single().GetMessage(), "containing class");

        // A class named like a generated member (Layout, Parse, Sizes, ...) cannot receive it: C# forbids a member named like its type.
        GeneratorResult reserved = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 x; };")]
            public static partial class Layout { }
            """);
        StringAssert.Contains(reserved.DiagnosticsWithId("CSG003").Single().GetMessage(), "rename the class");
        Assert.IsEmpty(reserved.GeneratedSources);

        GeneratorResult kept = GeneratorRunner.Run(Header + """
            [CStructLayout("struct a_b { uint8 x; }; struct aB { uint8 y; };", KeepNames = true)]
            public static partial class Names { }
            """).AssertClean();
        Assert.IsEmpty(kept.GeneratorDiagnostics);
    }

    [TestMethod]
    public void IncrementalPipeline_CachesAnUnchangedRequest()
    {
        const string source = Header + """
            [CStructLayout("struct root { uint8 a; };")]
            public static partial class Cached { }
            """;
        GeneratorResult first = GeneratorRunner.Run(source).AssertClean();
        GeneratorResult second = GeneratorRunner.Run(source + "\n// an unrelated edit\n").AssertClean();
        Assert.AreEqual(first.Source, second.Source);
    }

    [TestMethod]
    public void Naming_FollowsAppendixC()
    {
        Assert.AreEqual("ChunkType", Naming.ToPascalCase("chunk_type"));
        Assert.AreEqual("BitDepth", Naming.ToPascalCase("bit_depth"));
        Assert.AreEqual("Ihdr", Naming.ToPascalCase("IHDR"));
        Assert.AreEqual("PngSig", Naming.ToPascalCase("PNG_SIG"));
        Assert.AreEqual("Reserved", Naming.ToPascalCase("_reserved"));
        Assert.AreEqual("IPhone", Naming.ToPascalCase("iPhone"));
        Assert.AreEqual("Rgb", Naming.ToPascalCase("Rgb"));
        Assert.AreEqual("Read", Naming.ToPascalCase("READ"));
        Assert.AreEqual("Crc32", Naming.ToPascalCase("crc32"));
        Assert.AreEqual("Class", Naming.ToCSharp("class", keepNames: false));
        Assert.AreEqual("@class", Naming.ToCSharp("class", keepNames: true));
        Assert.AreEqual("@event", Naming.ToCSharp("event", keepNames: true));
        Assert.AreEqual("chunk_type", Naming.ToCSharp("chunk_type", keepNames: true));
    }
}
