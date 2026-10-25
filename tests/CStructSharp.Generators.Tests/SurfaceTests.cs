namespace CStructSharp.Generators.Tests;

using System;
using System.Linq;
using System.Reflection;
using CStructSharp;

/// <summary>
///     The generated surface: <c>Full</c> keeps everything, <c>Reader</c> keeps the typed classes and the readers over
///     bytes in memory, and <c>Views</c> keeps only the views, constants, and enums. Each smaller surface reads the same
///     values as the runtime layout, drops the members it promises to drop, and generates much less code.
/// </summary>
[TestClass]
public class SurfaceTests
{
    private const string Definition = "enum kind : uint8 { A = 1, B = 2 };\\nstruct hdr { uint16 length; kind tag; uint8 flags:3; uint8 level:5; char name[4]; uint32 values[2]; };\\nstruct root { uint8 magic; hdr header; uint32 id; };";

    private const string Probe = """
        public static class Probe
        {
            public static object[] Read(byte[] bytes)
            {
                var root = new Demo.Surfaced.RootView(bytes);
                var header = root.Header;
                return new object[] { root.Magic, header.Length, (int)header.Tag, header.Level, header.Name, header.Values(1), root.Id, Demo.Surfaced.Sizes.Root };
            }

        }
        """;

    /// <summary>The bytes of one <c>root</c>: magic, a 12-byte header, and an id (packed, little-endian).</summary>
    private static readonly byte[] Bytes = [7, 0x02, 0x01, 2, 0b0001_0101, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 1, 0, 0, 0, 9, 0, 0, 0, 0x78, 0x56, 0x34, 0x12];

    /// <summary>Generates one layout with a surface.</summary>
    /// <param name="arguments">The attribute arguments after the definition.</param>
    /// <param name="extra">More consumer source after the probe, such as a class that calls the generated members.</param>
    /// <returns>The generator result.</returns>
    private static GeneratorResult Generate(string arguments, string extra = "")
        => GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"" + Definition + "\", Root = \"root\"" + arguments + ")]\npublic static partial class Surfaced { }\n\n" + Probe + "\n" + extra);

    /// <summary>The members of the generated class: public static methods and properties, and the nested types.</summary>
    /// <param name="type">The generated class.</param>
    /// <returns>The member and nested type names.</returns>
    private static string[] Names(Type type)
        => [.. type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(member => member.Name).Distinct()];

    /// <summary>The reader surface reads the runtime layout's values and drops the writer, stream, path, and runtime-layout members.</summary>
    [TestMethod]
    public void ReaderSurface_ReadsLikeTheRuntime_AndDropsTheRest()
    {
        GeneratorResult result = Generate(", Surface = GeneratedSurface.Reader").AssertClean();
        Snapshot.Match("Surface.Reader", result.Source);
        Assembly assembly = result.Load();
        Type surfaced = assembly.GetType("Demo.Surfaced")!;
        string[] names = Names(surfaced);
        foreach (string present in new[] { "Parse", "ParseRoot", "ParseHdr", "TryParse", "TryParseRoot", "Sizes", "Offsets", "Definition", "RootName", "Root", "RootView", "Kind" })
        {
            CollectionAssert.Contains(names, present);
        }

        foreach (string absent in new[] { "Serialize", "SerializeRoot", "Write", "ParseAsync", "ParseRootAsync", "RecordsRoot", "Records", "Layout", "Update", "UpdatePath", "ParseWithDebug", "ResolveAddress", "ToStructValue", "ReadValue" })
        {
            CollectionAssert.DoesNotContain(names, absent);
        }

        var runtime = new CStruct(Definition.Replace("\\n", "\n", StringComparison.Ordinal));
        object parsed = surfaced.GetMethod("Parse", [typeof(byte[]), typeof(IReadOnlyDictionary<string, int>), typeof(ReadOptions)])!.Invoke(null, [Bytes, null, null])!;
        ParityComparer.AssertSame(runtime.Parse(Bytes, "root"), parsed, "root", strict: true);

        // The sequence and stream forms of the typed reader are gone, the memory forms stay; TryParse takes a span, an array, and a memory.
        Assert.IsEmpty(surfaced.GetMethods().Where(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType.Name is "Stream" or "ReadOnlySequence`1"));
        string[] tryInputs = [.. surfaced.GetMethods().Where(method => method.Name == "TryParse").Select(method => method.GetParameters()[0].ParameterType.Name).Distinct().Order()];
        CollectionAssert.AreEqual(new[] { "Byte[]", "ReadOnlyMemory`1", "ReadOnlySpan`1" }, tryInputs);
        Assert.IsNull(surfaced.GetNestedType("RootView")!.GetMethod("Enumerate"), "the enumerator is opt-in outside the full surface");
        object[] readings = (object[])assembly.GetType("Demo.Probe")!.GetMethod("Read")!.Invoke(null, [Bytes])!;
        Assert.AreEqual((byte)7, readings[0]);
        Assert.AreEqual((ushort)0x0102, readings[1]);
    }

    /// <summary>The views surface reads views and constants only: no class, no reader, no <c>ToObject</c>, but the enum stays.</summary>
    [TestMethod]
    public void ViewsSurface_KeepsOnlyViewsConstantsAndEnums()
    {
        GeneratorResult result = Generate(", Surface = GeneratedSurface.Views").AssertClean();
        Snapshot.Match("Surface.Views", result.Source);
        Assembly assembly = result.Load();
        Type surfaced = assembly.GetType("Demo.Surfaced")!;
        string[] names = Names(surfaced);
        foreach (string present in new[] { "Sizes", "Offsets", "Definition", "RootName", "RootView", "HdrView", "Kind" })
        {
            CollectionAssert.Contains(names, present);
        }

        foreach (string absent in new[] { "Parse", "ParseRoot", "TryParse", "Serialize", "Layout", "Update", "Root", "Hdr" })
        {
            CollectionAssert.DoesNotContain(names, absent);
        }

        Assert.IsNull(surfaced.GetNestedType("RootView")!.GetMethod("ToObject"));
        Assert.IsNull(surfaced.GetNestedType("RootView")!.GetMethod("Enumerate"), "the enumerator is opt-in outside the full surface");
        object[] readings = (object[])assembly.GetType("Demo.Probe")!.GetMethod("Read")!.Invoke(null, [Bytes])!;
        Assert.AreEqual((byte)7, readings[0]);
        Assert.AreEqual((ushort)0x0102, readings[1]);
        Assert.AreEqual(2, readings[2]);
        Assert.AreEqual((byte)2, readings[3]);
        Assert.AreEqual("abcd", readings[4]);
        Assert.AreEqual(9u, readings[5]);
        Assert.AreEqual(0x12345678u, readings[6]);
        Assert.AreEqual(21, readings[7]);
    }

    /// <summary>The parser surface reads the runtime layout's values through one span <c>Parse</c> per declaration and drops everything else, including <c>TryParse</c>, the array and memory overloads, and the definition constants.</summary>
    [TestMethod]
    public void ParserSurface_KeepsOneSpanParse_AndDropsTheRest()
    {
        const string ParserProbe = "public static class ParserProbe { public static object Parse(byte[] bytes) => Demo.Surfaced.Parse(bytes); }\n";
        GeneratorResult result = Generate(", Surface = GeneratedSurface.Parser", ParserProbe).AssertClean();
        Snapshot.Match("Surface.Parser", result.Source);
        Assembly assembly = result.Load();
        Type surfaced = assembly.GetType("Demo.Surfaced")!;
        string[] names = Names(surfaced);
        foreach (string present in new[] { "Parse", "ParseRoot", "ParseHdr", "Sizes", "Offsets", "Root", "RootView", "Kind" })
        {
            CollectionAssert.Contains(names, present);
        }

        foreach (string absent in new[] { "TryParse", "TryParseRoot", "Definition", "RootName", "Serialize", "Layout", "Update", "ParseAsync", "RecordsRoot" })
        {
            CollectionAssert.DoesNotContain(names, absent);
        }

        // One input form: the span. A byte[] converts to it at the call site.
        string[] inputs = [.. surfaced.GetMethods().Where(method => method.Name is "Parse" or "ParseRoot" or "ParseHdr").Select(method => method.GetParameters()[0].ParameterType.Name).Distinct()];
        CollectionAssert.AreEqual(new[] { "ReadOnlySpan`1" }, inputs);
        var runtime = new CStruct(Definition.Replace("\\n", "\n", StringComparison.Ordinal));
        object parsed = assembly.GetType("Demo.ParserProbe")!.GetMethod("Parse")!.Invoke(null, [Bytes])!;
        ParityComparer.AssertSame(runtime.Parse(Bytes, "root"), parsed, "root", strict: true);
    }

    /// <summary>The view enumerator is always on the full surface and opt-in with <c>Enumerators = true</c> on the smaller ones, where it walks consecutive records without allocating.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="option">The attribute argument that turns the enumerator on, or nothing for the full surface.</param>
    [TestMethod]
    [DataRow("Full", "")]
    [DataRow("Reader", ", Enumerators = true")]
    [DataRow("Parser", ", Enumerators = true")]
    [DataRow("Views", ", Enumerators = true")]
    public void Enumerators_AreOptInOutsideTheFullSurface(string surface, string option)
    {
        const string SumProbe = "public static class SumProbe { public static int SumMagic(byte[] bytes) { int sum = 0; foreach (var record in Demo.Surfaced.RootView.Enumerate(bytes)) { sum += record.Magic; } return sum; } }\n";
        GeneratorResult result = Generate(", Surface = GeneratedSurface." + surface + option, SumProbe).AssertClean();
        Assembly assembly = result.Load();
        Assert.AreEqual(14, assembly.GetType("Demo.SumProbe")!.GetMethod("SumMagic")!.Invoke(null, new object[] { Bytes.Concat(Bytes).ToArray() }));
        string without = Generate(", Surface = GeneratedSurface." + surface).Load().GetType("Demo.Surfaced")!.GetNestedType("RootView")!.GetMethod("Enumerate") is null ? "absent" : "present";
        Assert.AreEqual(surface == "Full" ? "present" : "absent", without);
    }

    /// <summary>The full surface is the default and keeps every member the smaller surfaces drop.</summary>
    [TestMethod]
    public void FullSurface_IsTheDefault_AndKeepsEverything()
    {
        string defaulted = Generate(string.Empty).AssertClean().Source;
        string explicitFull = Generate(", Surface = GeneratedSurface.Full").AssertClean().Source;
        Assert.AreEqual(defaulted, explicitFull);
        string[] names = Names(GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"" + Definition + "\", Root = \"root\")]\npublic static partial class Surfaced { }\n").AssertClean().Load().GetType("Demo.Surfaced")!);
        foreach (string present in new[] { "Parse", "Serialize", "ParseAsync", "RecordsRoot", "Layout", "Update", "UpdatePath", "ResolveAddress" })
        {
            CollectionAssert.Contains(names, present);
        }
    }

    /// <summary>A smaller surface generates much less source; the sizes are the point of the option.</summary>
    [TestMethod]
    public void SmallerSurfaces_GenerateMuchLessCode()
    {
        int full = Generate(string.Empty).Source.Length;
        int reader = Generate(", Surface = GeneratedSurface.Reader").Source.Length;
        int views = Generate(", Surface = GeneratedSurface.Views").Source.Length;
        int parser = Generate(", Surface = GeneratedSurface.Parser").Source.Length;
        Assert.IsLessThan(reader, parser, $"parser {parser} of reader {reader}");
        Assert.IsLessThan(full / 2, reader, $"reader {reader} of full {full}");
        Assert.IsLessThan(full / 6, views, $"views {views} of full {full}");
    }

    /// <summary>The views surface with views switched off would generate nothing, which CSG011 rejects.</summary>
    [TestMethod]
    public void ViewsSurface_WithoutViews_ReportsCsg011()
    {
        GeneratorResult result = Generate(", Surface = GeneratedSurface.Views, Views = false");
        Assert.HasCount(1, result.DiagnosticsWithId("CSG011"));
        Assert.IsEmpty(result.GeneratedSources);
    }
}
