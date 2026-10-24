namespace CStructSharp.Generators.Tests;

using System;
using System.Collections;
using System.Reflection;
using CStructSharp;
using CStructSharp.Values;

/// <summary>
///     <c>Shape = Struct</c>: the layout's structs are value types whose members read as the runtime layout's do, a fixed
///     array of numbers is a generated inline array, a read allocates nothing for the value, and a layout or surface a
///     value type cannot hold is refused with CSG014 and the reason.
/// </summary>
[TestClass]
public class ValueTypeShapeTests
{
    private const string Layout = "enum kind : uint16 { A = 1, B = 2 }; struct inner { uint16 a; uint8 b; }; struct rec { uint32 id; int16 delta; kind k; inner nested; uint8 sig[4]; uint16 vals[3]; char name[4]; uint8 flags:3; uint8 level:5; float32 ratio; };";

    private const string Consumer = """"
        using System;
        using CStructSharp;

        namespace Demo;

        [CStructLayout("@LAYOUT@", Root = "rec", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader)]
        public static partial class ReaderRec { }

        [CStructLayout("@LAYOUT@", Root = "rec", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]
        public static partial class ParserRec { }

        [CStructLayout("struct plain { uint32 a; uint8 sig[4]; uint16 pair[2]; };", Root = "plain", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]
        public static partial class PlainLayout { }

        [CStructLayout("struct leaf { uint16 x; }; struct bits { uint8 lo:4; uint8 hi:4; leaf tail; };", Root = "bits", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader, RuntimeByteOrder = true)]
        public static partial class BitsLayout { }

        [CStructLayout("struct checked { uint8 magic @expect(magic == 7); uint8 sig[4]; uint16 tail @supports(tail < 10); };", Root = "checked", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader)]
        public static partial class CheckedLayout { }

        public static class Probe
        {
            public static object ParseReader(byte[] bytes) => Demo.ReaderRec.Parse(bytes);

            public static object ParseParser(byte[] bytes) => Demo.ParserRec.Parse(bytes);

            public static bool TryReader(byte[] bytes, out object value)
            {
                bool ok = Demo.ReaderRec.TryParse(bytes, out var parsed);
                value = parsed;
                return ok;
            }

            public static object ParseChecked(byte[] bytes) => Demo.CheckedLayout.Parse(bytes);

            public static object ParseBits(byte[] bytes, bool little) => Demo.BitsLayout.Parse(bytes, little);

            public static long Allocated(byte[] bytes, int rounds)
            {
                for (int i = 0; i < 200; i++) { _ = Demo.PlainLayout.Parse(bytes); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                uint sum = 0;
                for (int i = 0; i < rounds; i++)
                {
                    var value = Demo.PlainLayout.Parse(bytes);
                    sum += value.A + value.Sig[1] + value.Pair[0];
                }

                return GC.GetAllocatedBytesForCurrentThread() - before + (sum == 0 ? 1 : 0);
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
        assembly = GeneratorRunner.Run(Consumer.Replace("@LAYOUT@", Layout, StringComparison.Ordinal)).AssertClean().Load();
    }

    /// <summary>Calls a probe method.</summary>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result.</returns>
    private static object Call(string method, params object[] arguments)
    {
        try
        {
            return assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>An integer as one comparable number (a runtime value may be a <see cref="System.Numerics.BigInteger"/>).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    private static System.Numerics.BigInteger Whole(object? value) => value is System.Numerics.BigInteger big ? big : new System.Numerics.BigInteger(Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The value of a property, with an inline array turned into a plain array.</summary>
    /// <param name="value">The struct.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value.</returns>
    private static object Property(object value, string name)
    {
        object? result = value.GetType().GetField(name)!.GetValue(value);
        MethodInfo? toArray = result?.GetType().GetMethod("ToArray", Type.EmptyTypes);
        return toArray is not null ? toArray.Invoke(result, null)! : result!;
    }

    /// <summary>The generated source of a small value-type layout, recorded as a snapshot.</summary>
    [TestMethod]
    public void GeneratedSource_IsRecorded()
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct inner { uint16 a; }; struct rec { uint32 id; inner nested; uint8 sig[4]; char name[2]; uint8 flags:3; uint8 level:5; };\", Root = \"rec\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]\npublic static partial class Shaped { }\n").AssertClean();
        Snapshot.Match("Shape.Struct", result.Source);
    }

    /// <summary>The generated types are structs, and a fixed array of numbers is an inline array of the right length.</summary>
    [TestMethod]
    public void GeneratedTypes_AreStructs_WithInlineArrays()
    {
        Type rec = assembly.GetType("Demo.ReaderRec+Rec")!;
        Assert.IsTrue(rec.IsValueType);
        Assert.IsTrue(assembly.GetType("Demo.ReaderRec+Inner")!.IsValueType);
        Type signature = rec.GetField("Sig")!.FieldType;
        Assert.AreEqual("ByteArray4", signature.Name);
        Assert.AreEqual(4, (int)signature.GetField("Length")!.GetValue(null)!);
        Assert.AreEqual("UInt16Array3", rec.GetField("Vals")!.FieldType.Name);
        Assert.AreEqual(typeof(string), rec.GetField("Name")!.FieldType);
    }

    /// <summary>Every member of the value equals what the runtime layout reads, on the reader and parser surfaces and through TryParse.</summary>
    [TestMethod]
    public void Members_ReadLikeTheRuntime()
    {
        byte[] bytes = [1, 0, 0, 0, 0xFE, 0xFF, 2, 0, 0x34, 0x12, 9, 10, 11, 12, 13, 1, 0, 2, 0, 3, 0, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0b10101_011, 0, 0, 0xC0, 0x3F];
        StructValue expected = new CStruct(Layout).Parse(bytes, "rec");
        var actual = new List<object>
        {
            Call("ParseReader", bytes),
            Call("ParseParser", bytes),
        };
        object[] tryArguments = [bytes, null!];
        Assert.IsTrue((bool)assembly.GetType("Demo.Probe")!.GetMethod("TryReader")!.Invoke(null, tryArguments)!);
        actual.Add(tryArguments[1]);
        foreach (object value in actual)
        {
            Assert.AreEqual(Whole(expected["id"]), Whole(Property(value, "Id")));
            Assert.AreEqual(Whole(expected["delta"]), Whole(Property(value, "Delta")));
            Assert.AreEqual(Whole(((EnumValueResult)expected["k"]!).Value), Whole(Property(value, "K")));
            object nested = Property(value, "Nested");
            var runtimeNested = (StructValue)expected["nested"]!;
            Assert.AreEqual(Whole(runtimeNested["a"]), Whole(Property(nested, "A")));
            Assert.AreEqual(Whole(runtimeNested["b"]), Whole(Property(nested, "B")));
            CollectionAssert.AreEqual(((IEnumerable)expected["sig"]!).Cast<object>().Select(Convert.ToByte).ToArray(), (byte[])Property(value, "Sig"));
            CollectionAssert.AreEqual(((IEnumerable)expected["vals"]!).Cast<object>().Select(Convert.ToUInt16).ToArray(), (ushort[])Property(value, "Vals"));
            Assert.AreEqual(expected["name"], Property(value, "Name"));
            Assert.AreEqual(Whole(expected["flags"]), Whole(Property(value, "Flags")));
            Assert.AreEqual(Whole(expected["level"]), Whole(Property(value, "Level")));
            Assert.AreEqual(Convert.ToSingle(expected["ratio"]), Convert.ToSingle(Property(value, "Ratio")));
        }
    }

    /// <summary>A struct failure and a short source throw the reader's exceptions, and TryParse reports them with a default value.</summary>
    [TestMethod]
    public void ShortSource_FailsLikeTheRuntime()
    {
        byte[] bytes = [1, 2, 3];
        var expected = Assert.Throws<CStructSharp.Diagnostics.CStructReadException>(() => new CStruct(Layout).Parse(bytes, "rec"));
        var actual = Assert.Throws<CStructSharp.Diagnostics.CStructReadException>(() => Call("ParseReader", bytes));
        Assert.AreEqual(expected.Message, actual.Message);
        object[] arguments = [bytes, null!];
        Assert.IsFalse((bool)assembly.GetType("Demo.Probe")!.GetMethod("TryReader")!.Invoke(null, arguments)!);
    }

    /// <summary>Reading a value allocates nothing: no object, no array, no options.</summary>
    [TestMethod]
    public void Read_AllocatesNothing()
    {
        byte[] bytes = [1, 0, 0, 0, 9, 8, 7, 6, 5, 0, 6, 0];
        Assert.AreEqual(0L, (long)Call("Allocated", bytes, 5000));
    }

    /// <summary>A value type works with run-time byte order and bitfields: the order chosen at read time decides the neutral members.</summary>
    [TestMethod]
    public void RuntimeByteOrder_AndBitfields_Work()
    {
        byte[] bytes = [0xA5, 0x12, 0x34];
        object little = Call("ParseBits", bytes, true);
        object big = Call("ParseBits", bytes, false);
        Assert.AreEqual(5, Whole(Property(little, "Lo")));
        Assert.AreEqual(10, Whole(Property(little, "Hi")));
        Assert.AreEqual(0x3412, Whole(Property(Property(little, "Tail"), "X")));
        Assert.AreEqual(0x1234, Whole(Property(Property(big, "Tail"), "X")));
    }

    /// <summary>Layouts and surfaces a value type cannot hold are refused with CSG014 and the reason.</summary>
    /// <param name="attribute">The attribute arguments.</param>
    /// <param name="reason">A word the message must contain.</param>
    [TestMethod]
    [DataRow("\"struct a { uint8 x; };\", Shape = GeneratedShape.Struct", "Reader or Parser surface")]
    [DataRow("\"union u { uint8 a; uint16 b; }; struct s { u v; };\", Root = \"s\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "union")]
    [DataRow("\"struct s { uint8 n; uint8 v[n]; };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "single fixed count")]
    [DataRow("\"struct s { uint8 t; if (t) { uint8 v; } };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "conditional")]
    [DataRow("\"struct s { uint8 *p; };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "pointer")]
    [DataRow("\"struct s { cstring name; };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "fixed size")]
    [DataRow("\"struct e { uint8 a; }; struct s { e items[2]; };\", Root = \"s\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "array of structs")]
    [DataRow("\"struct s { int8 v[2]; };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "only arrays of numbers")]
    [DataRow("\"struct s { uint8 m[2][2]; };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "single fixed count")]
    [DataRow("\"struct s { uint8 buf[1025]; };\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader", "at most 1024 bytes")]
    public void Unsupported_IsRefusedWithCSG014(string attribute, string reason)
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(" + attribute + ")]\npublic static partial class Refused { }\n");
        Assert.HasCount(1, result.DiagnosticsWithId("CSG014"), string.Join("\n", result.GeneratorDiagnostics));
        StringAssert.Contains(result.DiagnosticsWithId("CSG014")[0].GetMessage(), reason);
        Assert.IsEmpty(result.GeneratedSources);
    }

    /// <summary>The project property sets the shape of every layout whose attribute does not name one.</summary>
    [TestMethod]
    public void ProjectDefault_SetsTheShape()
    {
        const string Source = "using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct a { uint16 x; };\", Surface = GeneratedSurface.Parser)]\npublic static partial class Shaped { }\n";
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: new System.Collections.Generic.Dictionary<string, string> { ["build_property.CStructSharpShape"] = "Struct" }).AssertClean();
        Assert.IsTrue(result.Load().GetType("Demo.Shaped+A")!.IsValueType);
        Assert.IsFalse(GeneratorRunner.Run(Source).AssertClean().Load().GetType("Demo.Shaped+A")!.IsValueType);
    }

    /// <summary>A value-type struct with conditions and an inline array reads, checks and fails like the runtime.</summary>
    [TestMethod]
    public void ValueTypeWithConstraintsAndInlineArray_ChecksAfterDecoding()
    {
        var runtime = new CStruct("struct checked { uint8 magic @expect(magic == 7); uint8 sig[4]; uint16 tail @supports(tail < 10); };");
        object ok = Call("ParseChecked", new byte[] { 7, 1, 2, 3, 4, 5, 0 });
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, (byte[])Property(ok, "Sig"));
        Assert.AreEqual(5, Whole(Property(ok, "Tail")));
        foreach (byte[] bad in new[] { new byte[] { 8, 1, 2, 3, 4, 5, 0 }, new byte[] { 7, 1, 2, 3, 4, 50, 0 } })
        {
            var expected = Assert.Throws<CStructSharp.Diagnostics.CStructConstraintException>(() => runtime.Parse(bad, "checked"));
            var actual = Assert.Throws<CStructSharp.Diagnostics.CStructConstraintException>(() => Call("ParseChecked", bad));
            Assert.AreEqual(expected.Message, actual.Message);
            Assert.AreEqual(expected.Kind, actual.Kind);
        }
    }
}
