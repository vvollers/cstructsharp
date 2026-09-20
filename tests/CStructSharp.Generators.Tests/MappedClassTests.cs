namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Values;

/// <summary>
///     The <c>[CStructMapped]</c> generator: a partial class gains <c>ReadFrom</c>/<c>WriteTo</c> that map every
///     public settable property by name with the conversions of <c>Get&lt;T&gt;</c> - scalars, strings, enums,
///     nested mapped classes, arrays, lists, read-only collections, nullable members for conditional arms, typed
///     pointers, and reader values - plus the module initializer that registers it; the diagnostics for a type the
///     generator cannot map; and the exact names it emits when the attribute's <c>Layout</c> resolves.
/// </summary>
[TestClass]
public class MappedClassTests
{
    private const string Layout = """
        enum color : uint8 { Red = 1, Green = 2 };
        struct point { int16 x; int16 y; };
        struct root {
            uint8 tag; uint32 bit_depth; color colour; char name[4]; point origin; point corners[2]; uint16 values[3];
            if (tag == 1) { uint8 extra; }
            uint16 *link; uint8 n; uint8 data[n];
        };
        """;

    private const string Consumer = """"
        using System.Collections.Generic;
        using CStructSharp;
        using CStructSharp.Generated;
        using CStructSharp.Values;

        namespace Demo;

        [CStructLayout({0}, Root = "root", PointerSize = 1, Aligned = false)]
        public static partial class Packet { }

        public enum Colour : byte { Red = 1, Green = 2 }

        [CStructMapped]
        public sealed partial class Point
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        [CStructMapped(Layout = "root")]
        public sealed partial class Record
        {
            public byte Tag { get; set; }
            public uint BitDepth { get; set; }
            public Colour Colour { get; set; }
            [CStructMember("name")]
            public string Label { get; set; } = string.Empty;
            public Point Origin { get; set; } = new();
            public List<Point> Corners { get; set; } = [];
            public IReadOnlyList<ushort> Values { get; set; } = [];
            public byte? Extra { get; set; }
            public Pointer<ushort> Link { get; set; }
            public byte N { get; set; }
            public byte[] Data { get; set; } = [];
        }

        [CStructMapped]
        public sealed partial class Loose
        {
            public byte Tag { get; set; }
            public uint BitDepth { get; set; }
            public StructValue Origin { get; set; } = new();
            public Pointer? Link { get; set; }
        }
        """";

    [TestMethod]
    public void MappedClasses_ReadAndWriteThroughTheRuntime()
    {
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("{0}", ReaderParityTests.Literal(Layout), StringComparison.Ordinal)).AssertClean();
        Snapshot.Match("Mapped.Record", result.GeneratedSources.Single(source => source.HintName.Contains("Record", StringComparison.Ordinal)).Source);
        Assembly assembly = result.Load();
        var runtime = (CStruct)assembly.GetType("Demo.Packet")!.GetProperty("Layout")!.GetValue(null)!;
        Type record = assembly.GetType("Demo.Record")!;
        Type loose = assembly.GetType("Demo.Loose")!;
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Assert.IsTrue(MappedTypes.IsMapped(record));
        Assert.IsTrue(MappedTypes.IsMapped(assembly.GetType("Demo.Point")!));

        // tag=1 (extra present), bit_depth, colour, name, origin, corners, values, extra, link → offset 33, n, data, target.
        byte[] bytes = [1, 8, 0, 0, 0, 2, (byte)'a', (byte)'b', 0, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 1, 0, 2, 0, 3, 0, 9, 33, 2, 0x11, 0x22, 0x2A, 0];
        object mapped = ReadValue(record, runtime, bytes);
        Assert.AreEqual((byte)1, record.GetProperty("Tag")!.GetValue(mapped));
        Assert.AreEqual(8u, record.GetProperty("BitDepth")!.GetValue(mapped), "bit_depth maps to BitDepth");
        Assert.AreEqual(2, Convert.ToInt32(record.GetProperty("Colour")!.GetValue(mapped)));
        Assert.AreEqual("ab\0\0", record.GetProperty("Label")!.GetValue(mapped), "[CStructMember] names the layout member");
        object origin = record.GetProperty("Origin")!.GetValue(mapped)!;
        Assert.AreEqual(3, origin.GetType().GetProperty("X")!.GetValue(origin));
        var corners = (System.Collections.IList)record.GetProperty("Corners")!.GetValue(mapped)!;
        Assert.AreEqual(2, corners.Count);
        Assert.AreEqual(8, corners[1]!.GetType().GetProperty("Y")!.GetValue(corners[1]));
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 3 }, ((IEnumerable<ushort>)record.GetProperty("Values")!.GetValue(mapped)!).ToArray());
        Assert.AreEqual((byte)9, record.GetProperty("Extra")!.GetValue(mapped));
        object link = record.GetProperty("Link")!.GetValue(mapped)!;
        Assert.AreEqual(33L, link.GetType().GetProperty("Address")!.GetValue(link));
        Assert.AreEqual((ushort)42, link.GetType().GetProperty("Value")!.GetValue(link));
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22 }, (byte[])record.GetProperty("Data")!.GetValue(mapped)!);

        // The write goes back through WriteTo: the same bytes.
        CollectionAssert.AreEqual(runtime.Serialize("root", runtime.Parse(bytes, "root")), runtime.Serialize("root", mapped));

        // An unselected conditional arm leaves the nullable member null, and the write omits it.
        byte[] other = [0, 8, 0, 0, 0, 2, (byte)'a', (byte)'b', 0, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 1, 0, 2, 0, 3, 0, 31, 2, 0x11, 0x22, 0x2A, 0];
        object mappedOther = ReadValue(record, runtime, other);
        Assert.IsNull(record.GetProperty("Extra")!.GetValue(mappedOther));
        CollectionAssert.AreEqual(runtime.Serialize("root", runtime.Parse(other, "root")), runtime.Serialize("root", mappedOther));

        // The bridge: a generated value → StructValue → mapped class, a mapped instance serialized through the layout,
        // and the runtime's StructValue.ToMapped<T>().
        Type packet = assembly.GetType("Demo.Packet")!;
        object generatedValue = packet.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(byte[])).Invoke(null, [bytes, null, null])!;
        var structValue = (StructValue)packet.GetMethod("ToStructValue")!.Invoke(null, [generatedValue, null, null])!;
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), generatedValue, "root");
        Assert.AreEqual((byte)9, structValue["extra"]);
        object bridged = packet.GetMethod("ToMapped")!.MakeGenericMethod(record).Invoke(null, [generatedValue, null])!;
        Assert.AreEqual(8u, record.GetProperty("BitDepth")!.GetValue(bridged));
        object bridgedLink = record.GetProperty("Link")!.GetValue(bridged)!;
        Assert.AreEqual(33L, bridgedLink.GetType().GetProperty("Address")!.GetValue(bridgedLink));
        Assert.IsFalse((bool)bridgedLink.GetType().GetProperty("IsDereferenced")!.GetValue(bridgedLink)!, "the value's bytes hold no pointer targets");
        CollectionAssert.AreEqual(bytes[..^2], (byte[])packet.GetMethod("SerializeMapped")!.MakeGenericMethod(record).Invoke(null, [bridged, null, null])!);
        object viaRuntime = typeof(StructValue).GetMethod("ToMapped")!.MakeGenericMethod(record).Invoke(structValue, null)!;
        Assert.AreEqual((byte)9, record.GetProperty("Extra")!.GetValue(viaRuntime));

        // Without a resolvable layout the names are matched at run time (exact, case-insensitive, underscores ignored); reader values pass through.
        object looseValue = ReadValue(loose, runtime, bytes);
        Assert.AreEqual(8u, loose.GetProperty("BitDepth")!.GetValue(looseValue));
        Assert.AreEqual((short)3, ((StructValue)loose.GetProperty("Origin")!.GetValue(looseValue)!)["x"]);
        Assert.AreEqual(33L, ((CStructSharp.Values.Pointer)loose.GetProperty("Link")!.GetValue(looseValue)!).Address);
    }

    [TestMethod]
    public void Diagnostics_CoverPartialConstructorMemberTypesAndMissingMembers()
    {
        const string Header = "using CStructSharp;\n\nnamespace Demo;\n\n";
        GeneratorResult notPartial = GeneratorRunner.Run(Header + "[CStructMapped] public sealed class Sealed { public byte Tag { get; set; } }");
        StringAssert.Contains(notPartial.DiagnosticsWithId("CSG100").Single().GetMessage(), "partial");

        GeneratorResult noConstructor = GeneratorRunner.Run(Header + "[CStructMapped] public sealed partial class Ctor { public Ctor(int x) { } public byte Tag { get; set; } }");
        StringAssert.Contains(noConstructor.DiagnosticsWithId("CSG100").Single().GetMessage(), "parameterless constructor");

        GeneratorResult unmapped = GeneratorRunner.Run(Header + "public sealed class Plain { } [CStructMapped] public sealed partial class Holder { public Plain Child { get; set; } = new(); public System.Collections.Generic.List<Plain> Children { get; set; } = []; }");
        Assert.AreEqual(2, unmapped.DiagnosticsWithId("CSG101").Count);
        StringAssert.Contains(unmapped.DiagnosticsWithId("CSG101")[0].GetMessage(), "Demo.Plain");

        GeneratorResult missing = GeneratorRunner.Run(Header + """"
            [CStructLayout("struct hdr { uint8 kind; uint16 total_len; };")]
            public static partial class Layout1 { }

            [CStructMapped(Layout = "hdr")]
            public sealed partial class Mapped { public byte Kind { get; set; } public ushort TotalLen { get; set; } public byte Missing { get; set; } }
            """");
        Assert.AreEqual(1, missing.DiagnosticsWithId("CSG102").Count);
        StringAssert.Contains(missing.DiagnosticsWithId("CSG102").Single().GetMessage(), "'Missing'");
        string source = missing.GeneratedSources.Single(item => item.HintName.Contains("Mapped", StringComparison.Ordinal)).Source;
        StringAssert.Contains(source, "source.Get<ushort>(\"total_len\")", "a resolved layout gives exact names");
        StringAssert.Contains(source, "MemberName(source, \"Missing\")", "an unresolved member falls back to run-time matching");
    }

    private static object ReadValue(Type mapped, CStruct runtime, byte[] bytes)
    {
        // ReadValue<T> over a span cannot be called through reflection with a span: a generic helper closed over T does it.
        MethodInfo helper = typeof(MappedClassTests).GetMethod(nameof(Call), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(mapped);
        try
        {
            return helper.Invoke(null, [runtime, bytes])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static T Call<T>(CStruct runtime, byte[] bytes) => runtime.ReadValue<T>(bytes, "root");
}
