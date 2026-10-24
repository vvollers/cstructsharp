namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;

/// <summary>
///     <c>Shape = Auto</c>: a layout that can be value types is, and any other layout is a class, with nothing reported, so
///     one shape can be set for a whole project; consumer code that uses the members both shapes share compiles with either.
/// </summary>
[TestClass]
public class AutoShapeTests
{
    private const string Header = "using CStructSharp;\n\nnamespace Demo;\n\n";

    private const string Consumer = Header + """"
        using System;

        [CStructLayout("struct rec { uint32 id; uint8 sig[4]; char name[4]; };", Root = "rec", Shape = GeneratedShape.Auto, Surface = GeneratedSurface.Parser)]
        public static partial class FixedRec { }

        [CStructLayout("struct rec { uint8 n; uint8 sig[4]; uint8 items[n]; };", Root = "rec", Shape = GeneratedShape.Auto, Surface = GeneratedSurface.Parser)]
        public static partial class RuntimeRec { }

        [CStructLayout("struct rec { uint32 id; uint8 sig[4]; };", Root = "rec", Shape = GeneratedShape.Auto)]
        public static partial class FullRec { }

        [CStructLayout("struct rec { uint32 id; uint8 sig[4]; };", Root = "rec", Shape = GeneratedShape.Auto, Surface = GeneratedSurface.Views)]
        public static partial class ViewsRec { }

        [CStructLayout("struct rec { uint32 id; };", Root = "rec", Shape = GeneratedShape.Auto, Surface = GeneratedSurface.Parser)]
        public static partial class InlineArrays { }

        [CStructLayout("struct rec { uint32 id; uint8 sig[4]; };", Root = "rec", Shape = GeneratedShape.Auto, Surface = GeneratedSurface.Reader)]
        public static partial class ReaderRec { }

        public static class Probe
        {
            // The same text compiles whether the layout turned out a struct (an inline array) or a class (a T[]).
            public static int FixedUse(byte[] bytes)
            {
                var value = FixedRec.Parse(bytes);
                return (int)value.Id + value.Sig[1] + value.Sig.Length + value.Sig.AsSpan()[2] + value.Name.Length;
            }

            public static int RuntimeUse(byte[] bytes)
            {
                var value = RuntimeRec.Parse(bytes);
                return value.N + value.Sig[1] + value.Sig.Length + value.Sig.AsSpan()[2] + value.Items.Length;
            }

            public static int ReaderUse(byte[] bytes)
            {
                bool ok = ReaderRec.TryParse(bytes, out var value);
                return ok ? (int)value.Id + value.Sig[0] + value.Sig.Length : -1;
            }
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        GeneratorResult result = GeneratorRunner.Run(Consumer).AssertClean();
        Assert.AreEqual(0, result.DiagnosticsWithId("CSG014").Count);
        assembly = result.Load();
    }

    /// <summary>The type a layout's struct became.</summary>
    /// <param name="layout">The generated class.</param>
    /// <returns>The type of its <c>Rec</c>.</returns>
    private static Type Rec(string layout) => assembly.GetType("Demo." + layout)!.GetNestedType("Rec")!;

    /// <summary>A layout that qualifies is a value type, and one that does not (a runtime-sized array) is a class.</summary>
    [TestMethod]
    public void Auto_TakesTheValueTypeWhenTheLayoutQualifies()
    {
        Assert.IsTrue(Rec("FixedRec").IsValueType);
        Assert.AreEqual("ByteArray4", Rec("FixedRec").GetField("Sig")!.FieldType.Name);
        Assert.IsTrue(Rec("RuntimeRec").IsClass);
        Assert.AreEqual(typeof(byte[]), Rec("RuntimeRec").GetProperty("Sig")!.PropertyType);
    }

    /// <summary>A surface that has no value-type form keeps the class and reports nothing.</summary>
    [TestMethod]
    public void Auto_OnASurfaceWithoutValueTypes_KeepsTheClass()
    {
        Assert.IsTrue(Rec("FullRec").IsClass);
        Assert.IsNull(assembly.GetType("Demo.ViewsRec")!.GetNestedType("Rec"));
    }

    /// <summary>A class named like the inline array container still generates, as a class, because the value shape would have reserved the name.</summary>
    [TestMethod]
    public void Auto_KeepsTheClass_WhenTheValueShapeWouldClashWithTheClassName()
    {
        Assert.IsTrue(Rec("InlineArrays").IsClass);
    }

    /// <summary>Consumer code written against the members both shapes share runs on either.</summary>
    [TestMethod]
    public void SharedMembers_CompileAndReadWithEitherShape()
    {
        Type probe = assembly.GetType("Demo.Probe")!;
        Assert.AreEqual(7 + 2 + 4 + 3 + 4, (int)probe.GetMethod("FixedUse")!.Invoke(null, [new byte[] { 7, 0, 0, 0, 1, 2, 3, 4, (byte)'a', (byte)'b', (byte)'c', (byte)'d' }])!);
        Assert.AreEqual(2 + 2 + 4 + 3 + 2, (int)probe.GetMethod("RuntimeUse")!.Invoke(null, [new byte[] { 2, 1, 2, 3, 4, 9, 9 }])!);
        Assert.AreEqual(5 + 1 + 4, (int)probe.GetMethod("ReaderUse")!.Invoke(null, [new byte[] { 5, 0, 0, 0, 1, 2, 3, 4 }])!);
        Assert.AreEqual(-1, (int)probe.GetMethod("ReaderUse")!.Invoke(null, [new byte[] { 5, 0 }])!);
    }

    /// <summary>The project default <c>Auto</c> applies to every layout that does not name a shape, and an attribute that names one keeps it.</summary>
    [TestMethod]
    public void ProjectDefault_Auto_AppliesToLayoutsThatNameNoShape()
    {
        const string Source = Header + """
            [CStructLayout("struct rec { uint32 id; };", Root = "rec")]
            public static partial class Plain { }

            [CStructLayout("struct rec { uint32 id; };", Root = "rec", Shape = GeneratedShape.Class)]
            public static partial class Chosen { }

            [CStructLayout("struct rec { uint8 n; uint8 items[n]; };", Root = "rec")]
            public static partial class Variable { }
            """;
        var options = new Dictionary<string, string> { ["build_property.CStructSharpSurface"] = "Parser", ["build_property.CStructSharpShape"] = "Auto" };
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: options).AssertClean();
        Assert.AreEqual(0, result.DiagnosticsWithId("CSG012").Count);
        Assembly loaded = result.Load();
        Assert.IsTrue(loaded.GetType("Demo.Plain")!.GetNestedType("Rec")!.IsValueType);
        Assert.IsTrue(loaded.GetType("Demo.Chosen")!.GetNestedType("Rec")!.IsClass);
        Assert.IsTrue(loaded.GetType("Demo.Variable")!.GetNestedType("Rec")!.IsClass);
    }

    /// <summary>The generated source of an Auto layout that became a struct is the source of the same layout that asked for a struct.</summary>
    [TestMethod]
    public void Auto_GeneratesWhatStructGenerates()
    {
        const string Layout = "[CStructLayout(\"struct rec { uint32 id; uint8 sig[4]; };\", Root = \"rec\", Shape = GeneratedShape.@@, Surface = GeneratedSurface.Parser)]\npublic static partial class Sample { }";
        string auto = GeneratorRunner.Run(Header + Layout.Replace("@@", "Auto", StringComparison.Ordinal)).AssertClean().Source;
        string structural = GeneratorRunner.Run(Header + Layout.Replace("@@", "Struct", StringComparison.Ordinal)).AssertClean().Source;
        Assert.AreEqual(structural, auto);
    }
}
