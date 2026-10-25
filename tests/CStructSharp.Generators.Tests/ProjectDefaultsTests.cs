namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
///     The project-wide defaults: the <c>CStructSharp*</c> build properties set the value of every
///     <c>[CStructLayout]</c> option the attribute does not name, an option the attribute names keeps its own value,
///     and a property that does not parse is reported and ignored.
/// </summary>
[TestClass]
public class ProjectDefaultsTests
{
    private const string Header = "using CStructSharp;\n\nnamespace Demo;\n\n";

    /// <summary>Builds the global options MSBuild would hand the generator for these properties.</summary>
    /// <param name="properties">Property name (without the <c>build_property.</c> prefix) and value pairs.</param>
    /// <returns>The options.</returns>
    private static Dictionary<string, string> Options(params (string Name, string Value)[] properties)
        => properties.ToDictionary(property => "build_property." + property.Name, property => property.Value, StringComparer.Ordinal);

    /// <summary>The public static member names of a generated class.</summary>
    /// <param name="type">The class.</param>
    /// <returns>The names.</returns>
    private static string[] Names(Type type)
        => [.. type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(member => member.Name).Distinct()];

    /// <summary>A project that sets the reader surface gets it for every layout, and an attribute that names a surface keeps its own.</summary>
    [TestMethod]
    public void Surface_DefaultsToTheProject_AndTheAttributeWins()
    {
        const string Source = Header + """
            [CStructLayout("struct root { uint32 a; };")]
            public static partial class Plain { }

            [CStructLayout("struct root { uint32 a; };", Surface = GeneratedSurface.Full)]
            public static partial class Chosen { }
            """;
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpSurface", "Reader"))).AssertClean();
        var assembly = result.Load();
        string[] plain = Names(assembly.GetType("Demo.Plain")!);
        CollectionAssert.Contains(plain, "Parse");
        CollectionAssert.DoesNotContain(plain, "Serialize");
        CollectionAssert.DoesNotContain(plain, "Layout");
        CollectionAssert.Contains(Names(assembly.GetType("Demo.Chosen")!), "Serialize");

        // Without the property nothing changes: both layouts are full.
        var unchanged = GeneratorRunner.Run(Source).AssertClean().Load();
        CollectionAssert.Contains(Names(unchanged.GetType("Demo.Plain")!), "Serialize");
    }

    /// <summary>The property names are case-insensitive in value, and the views default applies unless the attribute says otherwise.</summary>
    [TestMethod]
    public void Views_DefaultsToTheProject_AndTheAttributeWins()
    {
        const string Source = Header + """
            [CStructLayout("struct root { uint32 a; };")]
            public static partial class Plain { }

            [CStructLayout("struct root { uint32 a; };", Views = true)]
            public static partial class WithViews { }
            """;
        var assembly = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpSurface", "reader"), ("CStructSharpViews", "false"))).AssertClean().Load();
        Assert.IsNull(assembly.GetType("Demo.Plain")!.GetNestedType("RootView"), "the project turned views off");
        Assert.IsNotNull(assembly.GetType("Demo.WithViews")!.GetNestedType("RootView"), "the attribute turned them back on");
    }

    /// <summary>A project byte order, alignment and pointer width are the layout's defaults; the attribute's own values win.</summary>
    [TestMethod]
    public void LayoutOptions_DefaultToTheProject()
    {
        const string Source = Header + """
            [CStructLayout("struct root { uint8 a; uint32 b; };")]
            public static partial class Plain { }

            [CStructLayout("struct root { uint8 a; uint32 b; };", LittleEndian = true, Aligned = false)]
            public static partial class Own { }
            """;
        var assembly = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpLittleEndian", "false"), ("CStructSharpAligned", "true"))).AssertClean().Load();
        var plain = (CStruct)assembly.GetType("Demo.Plain")!.GetProperty("Layout")!.GetValue(null)!;
        var own = (CStruct)assembly.GetType("Demo.Own")!.GetProperty("Layout")!.GetValue(null)!;
        Assert.AreEqual(8, plain.GetStructSizeInBytes("root"), "aligned: one byte, three padding bytes, four");
        Assert.IsFalse(plain.IsLittleEndian);
        Assert.AreEqual(5, own.GetStructSizeInBytes("root"));
        Assert.IsTrue(own.IsLittleEndian);
    }

    /// <summary>The constraint failure mode comes from the project and the attribute's own choice wins; a value the generator does not know is reported as CSG012.</summary>
    [TestMethod]
    public void ConstraintFailures_DefaultToTheProject_AndTheAttributeWins()
    {
        const string Source = Header + """
            [CStructLayout("struct root { uint8 a @expect(a == 1, \"root.a\", \"A must be one.\"); };", Surface = GeneratedSurface.Reader)]
            public static partial class Plain { }

            [CStructLayout("struct root { uint8 a @expect(a == 1, \"root.a\", \"A must be one.\"); };", Surface = GeneratedSurface.Reader, ConstraintFailures = ConstraintFailureMode.Constraint)]
            public static partial class Own { }
            """;
        var assembly = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpConstraintFailures", "formatexceptions"))).AssertClean().Load();
        var plain = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Demo.Plain")!.GetMethod("Parse", [typeof(byte[]), typeof(IReadOnlyDictionary<string, int>), typeof(ReadOptions)])!.Invoke(null, [new byte[] { 0 }, null, null]));
        Assert.IsInstanceOfType<System.IO.InvalidDataException>(plain.InnerException);
        var own = Assert.ThrowsExactly<TargetInvocationException>(() => assembly.GetType("Demo.Own")!.GetMethod("Parse", [typeof(byte[]), typeof(IReadOnlyDictionary<string, int>), typeof(ReadOptions)])!.Invoke(null, [new byte[] { 0 }, null, null]));
        Assert.IsInstanceOfType<CStructSharp.Diagnostics.CStructConstraintException>(own.InnerException);

        // Without the property the default stays the constraint exception.
        var unchanged = GeneratorRunner.Run(Source).AssertClean().Load();
        var constraint = Assert.ThrowsExactly<TargetInvocationException>(() => unchanged.GetType("Demo.Plain")!.GetMethod("Parse", [typeof(byte[]), typeof(IReadOnlyDictionary<string, int>), typeof(ReadOptions)])!.Invoke(null, [new byte[] { 0 }, null, null]));
        Assert.IsInstanceOfType<CStructSharp.Diagnostics.CStructConstraintException>(constraint.InnerException);

        GeneratorResult typo = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpConstraintFailures", "Format")));
        Assert.AreEqual(1, typo.DiagnosticsWithId("CSG012").Count);
        StringAssert.Contains(typo.DiagnosticsWithId("CSG012")[0].GetMessage(), "CStructSharpConstraintFailures");
    }

    /// <summary>A value the generator does not understand is reported once as CSG012, ignored, and generation continues with the library's default.</summary>
    [TestMethod]
    public void InvalidProperty_ReportsCSG012_AndIsIgnored()
    {
        const string Source = Header + """
            [CStructLayout("struct root { uint32 a; };")]
            public static partial class Plain { }
            """;
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpSurface", "Tiny"), ("CStructSharpPointerSize", "3"), ("CStructSharpViews", "false")));
        Assert.AreEqual(2, result.DiagnosticsWithId("CSG012").Count);
        StringAssert.Contains(result.DiagnosticsWithId("CSG012")[0].GetMessage(), "CStructSharpSurface");
        StringAssert.Contains(result.DiagnosticsWithId("CSG012")[1].GetMessage(), "CStructSharpPointerSize");
        CollectionAssert.Contains(Names(result.Load().GetType("Demo.Plain")!), "Serialize");
    }

    /// <summary>The views surface from the project together with a views default of false generates nothing and reports CSG011.</summary>
    [TestMethod]
    public void ViewsSurfaceWithoutViews_FromTheProject_ReportsCSG011()
    {
        const string Source = Header + """
            [CStructLayout("struct root { uint32 a; };")]
            public static partial class Plain { }
            """;
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpSurface", "Views"), ("CStructSharpViews", "false")));
        Assert.AreEqual(1, result.DiagnosticsWithId("CSG011").Count);
        Assert.AreEqual(0, result.GeneratedSources.Count);
    }

    /// <summary>A mapped class that names a layout sees that layout compiled with the project's defaults.</summary>
    [TestMethod]
    public void MappedClass_ResolvesTheLayoutWithTheProjectDefaults()
    {
        const string Source = Header + """
            [CStructLayout("struct header { uint16 length; };", Root = "header")]
            public static partial class Wire { }

            [CStructMapped(Layout = "header")]
            public partial class HeaderModel
            {
                public ushort Length { get; set; }
            }
            """;
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: Options(("CStructSharpLittleEndian", "false"))).AssertClean();
        Assert.AreEqual(0, result.DiagnosticsWithId("CSG102").Count);
    }
}
