namespace CStructSharp.Generators.Tests;

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using System.Text;
using CStructSharp;
using CStructSharp.Values;

/// <summary>
///     <c>RuntimeByteOrder</c>: one generated layout reads both byte orders. For every order the generated readers, the
///     fixed readers, the views and the pointer readers return what a runtime layout compiled with that order returns,
///     an explicit <c>&lt;</c> or <c>&gt;</c> keeps its own order, and a surface that writes is refused.
/// </summary>
[TestClass]
public class RuntimeByteOrderTests
{
    private const string Fixed = "enum kind : uint16 { A = 1, B = 0x0102 }; struct inner { uint16 a; uint8 b; }; struct fx { uint16 u16; int32 i32; uint64 u64; uint32< le32; uint32> be32; float32 f; int24 i24; uint48 u48; kind k; inner nested; uint16 arr[3]; int32 matrix[2][2]; fixed16_16 fpx; uint8 flags:3; uint8 level:5; uint8 sep; uint16 wide:12; uint16 rest:4; uint32 tail; };";

    private const string Dynamic = "struct dy { uint16 count; uint32 vals[count]; wchar name[2]; string text; uint16 tail; };";

    private const string Pointers = "struct leaf { uint8 id; }; struct holder { leaf *target; };";

    private const string Consumer = """"
        using System;
        using CStructSharp;

        namespace Demo;

        [CStructLayout("@FIXED@", Root = "fx", RuntimeByteOrder = true, Surface = GeneratedSurface.Reader, Enumerators = true)]
        public static partial class ReaderFx { }

        [CStructLayout("@FIXED@", Root = "fx", RuntimeByteOrder = true, Surface = GeneratedSurface.Parser)]
        public static partial class ParserFx { }

        [CStructLayout("@FIXED@", Root = "fx", RuntimeByteOrder = true, Surface = GeneratedSurface.Views, Enumerators = true)]
        public static partial class ViewsFx { }

        [CStructLayout("struct dy { uint16 count; uint32 vals[count]; wchar name[2]; string text; uint16 tail; };", Root = "dy", RuntimeByteOrder = true, Surface = GeneratedSurface.Reader)]
        public static partial class ReaderDy { }

        [CStructLayout("struct leaf { uint8 id; }; struct holder { leaf *target; };", Root = "holder", RuntimeByteOrder = true, Surface = GeneratedSurface.Reader, PointerSize = 2)]
        public static partial class ReaderPointers { }

        public static class Probe
        {
            public static object ParseFx(byte[] bytes, bool little) => Demo.ReaderFx.Parse(bytes, little);

            public static object ParseFxParser(byte[] bytes, bool little) => Demo.ParserFx.Parse(bytes, little);

            public static object ParseFxLimits(byte[] bytes, bool little) => Demo.ParserFx.Parse(bytes, little, new ReadLimits { MaxArrayElements = 100 });

            public static object ParseDy(byte[] bytes, bool little) => Demo.ReaderDy.Parse(bytes, little);

            public static object ParsePointers(byte[] bytes, bool little) => Demo.ReaderPointers.Parse(bytes, little);

            public static object[] TryFx(byte[] bytes, bool little)
            {
                bool ok = Demo.ReaderFx.TryParse(bytes, little, out var value, out var failure);
                return new object[] { ok, value!, failure! };
            }

            public static object[] ViewFx(byte[] bytes, bool little)
            {
                var view = new Demo.ViewsFx.FxView(bytes, little);
                return new object[]
                {
                    view.U16, view.I32, view.U64, view.Le32, view.Be32, view.F, view.I24, (int)view.K, view.Nested.A, view.Nested.B,
                    view.Arr(2), view.Fpx, view.Flags, view.Level, view.Wide, view.Rest, view.Tail,
                };
            }

            public static object ViewToObjectFx(byte[] bytes, bool little) => new Demo.ReaderFx.FxView(bytes, little).ToObject();

            public static int[] EnumerateFx(byte[] bytes, bool little)
            {
                var results = new System.Collections.Generic.List<int>();
                foreach (var record in Demo.ViewsFx.FxView.Enumerate(bytes, little))
                {
                    results.Add(record.U16);
                }

                foreach (var record in Demo.ReaderFx.FxView.Enumerate(bytes, little))
                {
                    results.Add(record.U16);
                }

                return results.ToArray();
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
        assembly = GeneratorRunner.Run(Consumer.Replace("@FIXED@", Fixed, StringComparison.Ordinal)).AssertClean().Load();
    }

    /// <summary>Calls a probe method and unwraps a failure it throws.</summary>
    /// <param name="method">The probe method.</param>
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

    /// <summary>An integer the runtime or a view returned, as one comparable number (a runtime value may be a <see cref="System.Numerics.BigInteger"/>).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    private static System.Numerics.BigInteger Whole(object? value) => value is System.Numerics.BigInteger big ? big : new System.Numerics.BigInteger(Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A deterministic input for the fixed layout: any bytes are a valid <c>fx</c>.</summary>
    /// <returns>The bytes of one <c>fx</c>.</returns>
    private static byte[] FixedBytes()
    {
        var random = new Random(1234);
        var bytes = new byte[74];
        random.NextBytes(bytes);
        return bytes;
    }

    /// <summary>The runtime layout of a definition compiled with a byte order.</summary>
    /// <param name="definition">The layout text.</param>
    /// <param name="little">The byte order.</param>
    /// <param name="pointerSize">The pointer width in bytes.</param>
    /// <returns>The runtime layout.</returns>
    private static CStruct Runtime(string definition, bool little, int pointerSize = 8) => new(definition, pointerSize: (byte)pointerSize, isLittleEndian: little);

    /// <summary>Every reader of the fixed layout - member by member, fixed, parser, limits, TryParse and a view's ToObject - equals the runtime for both orders.</summary>
    /// <param name="little">The byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FixedLayout_ReadsBothOrdersLikeTheRuntime(bool little)
    {
        byte[] bytes = FixedBytes();
        StructValue expected = Runtime(Fixed, little).Parse(bytes, "fx");
        foreach (string method in new[] { "ParseFx", "ParseFxParser", "ParseFxLimits", "ViewToObjectFx" })
        {
            ParityComparer.AssertSame(expected, Call(method, bytes, little), "fx:" + method, strict: false);
        }

        object[] tried = (object[])Call("TryFx", bytes, little);
        Assert.IsTrue((bool)tried[0]);
        ParityComparer.AssertSame(expected, tried[1], "fx:TryParse", strict: false);
    }

    /// <summary>The two orders really differ: the same bytes give different neutral values, and an explicit suffix keeps its order.</summary>
    [TestMethod]
    public void ExplicitSuffixes_KeepTheirOrder_AndNeutralValuesFollowTheChosenOne()
    {
        byte[] bytes = FixedBytes();
        object little = Call("ParseFx", bytes, true);
        object big = Call("ParseFx", bytes, false);
        Assert.AreNotEqual(little.GetType().GetProperty("U16")!.GetValue(little), big.GetType().GetProperty("U16")!.GetValue(big));
        Assert.AreEqual(little.GetType().GetProperty("Le32")!.GetValue(little), big.GetType().GetProperty("Le32")!.GetValue(big), "uint32< stays little-endian");
        Assert.AreEqual(little.GetType().GetProperty("Be32")!.GetValue(little), big.GetType().GetProperty("Be32")!.GetValue(big), "uint32> stays big-endian");
    }

    /// <summary>A view reads every member kind in both orders as the runtime does.</summary>
    /// <param name="little">The byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void View_ReadsBothOrdersLikeTheRuntime(bool little)
    {
        byte[] bytes = FixedBytes();
        StructValue expected = Runtime(Fixed, little).Parse(bytes, "fx");
        object[] readings = (object[])Call("ViewFx", bytes, little);
        Assert.AreEqual(Whole(expected["u16"]), Whole(readings[0]));
        Assert.AreEqual(Whole(expected["i32"]), Whole(readings[1]));
        Assert.AreEqual(Whole(expected["u64"]), Whole(readings[2]));
        Assert.AreEqual(Whole(expected["le32"]), Whole(readings[3]));
        Assert.AreEqual(Whole(expected["be32"]), Whole(readings[4]));
        Assert.AreEqual(Convert.ToSingle(expected["f"]).ToString("R"), Convert.ToSingle(readings[5]).ToString("R"));
        Assert.AreEqual(Whole(expected["i24"]), Whole(readings[6]));
        Assert.AreEqual(Whole(((EnumValueResult)expected["k"]!).Value), Whole(readings[7]));
        var nested = (StructValue)expected["nested"]!;
        Assert.AreEqual(Whole(nested["a"]), Whole(readings[8]));
        Assert.AreEqual(Whole(nested["b"]), Whole(readings[9]));
        Assert.AreEqual(Whole(((System.Collections.IList)expected["arr"]!)[2]), Whole(readings[10]));
        Assert.AreEqual(Convert.ToDouble(expected["fpx"]), Convert.ToDouble(readings[11]));
        Assert.AreEqual(Whole(expected["flags"]), Whole(readings[12]));
        Assert.AreEqual(Whole(expected["level"]), Whole(readings[13]));
        Assert.AreEqual(Whole(expected["wide"]), Whole(readings[14]));
        Assert.AreEqual(Whole(expected["rest"]), Whole(readings[15]));
        Assert.AreEqual(Whole(expected["tail"]), Whole(readings[16]));
    }

    /// <summary>The view enumerators walk consecutive records in the chosen order.</summary>
    /// <param name="little">The byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Enumerators_WalkRecordsInTheChosenOrder(bool little)
    {
        byte[] one = FixedBytes();
        byte[] two = [.. one, .. one];
        int first = little ? BinaryPrimitives.ReadUInt16LittleEndian(one) : BinaryPrimitives.ReadUInt16BigEndian(one);
        int[] results = (int[])Call("EnumerateFx", two, little);
        CollectionAssert.AreEqual(new[] { first, first, first, first }, results);
    }

    /// <summary>A runtime-sized array, wide characters, a terminated string and the members after them read in both orders.</summary>
    /// <param name="little">The byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void DynamicLayout_ReadsBothOrdersLikeTheRuntime(bool little)
    {
        Encoding utf16 = little ? Encoding.Unicode : Encoding.BigEndianUnicode;
        byte[] Number16(ushort value) => little ? [(byte)value, (byte)(value >> 8)] : [(byte)(value >> 8), (byte)value];
        byte[] Number32(uint value) => little ? [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)] : [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        byte[] bytes = [.. Number16(2), .. Number32(0x01020304), .. Number32(7), .. utf16.GetBytes("Hi"), .. utf16.GetBytes("OK"), 0, 0, .. Number16(0xABCD)];
        StructValue expected = Runtime(Dynamic, little).Parse(bytes, "dy");
        ParityComparer.AssertSame(expected, Call("ParseDy", bytes, little), "dy", strict: false);
    }

    /// <summary>A pointer's stored address is read in the chosen order, and the target is followed from it.</summary>
    /// <param name="little">The byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void PointerAddress_ReadsInTheChosenOrder(bool little)
    {
        byte[] bytes = little ? [2, 0, 0x5A] : [0, 2, 0x5A];
        StructValue expected = Runtime(Pointers, little, pointerSize: 2).Parse(bytes, "holder");
        ParityComparer.AssertSame(expected, Call("ParsePointers", bytes, little), "holder", strict: false);
    }

    /// <summary>The full surface writes one order, so a layout with run-time byte order on it is refused with CSG013.</summary>
    [TestMethod]
    public void FullSurface_IsRefused()
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct a { uint16 x; };\", RuntimeByteOrder = true)]\npublic static partial class Refused { }\n");
        Assert.HasCount(1, result.DiagnosticsWithId("CSG013"));
        StringAssert.Contains(result.DiagnosticsWithId("CSG013")[0].GetMessage(), "only reads");
        Assert.IsEmpty(result.GeneratedSources);
    }

    /// <summary>A bitfield run whose storage unit sits elsewhere in the other byte order cannot share one placement, so it is refused with CSG013 and the reason.</summary>
    [TestMethod]
    public void MovingBitfield_IsRefused()
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct a { uint8 x:3; uint8 y:5; uint16 wide:12; uint16 rest:4; };\", RuntimeByteOrder = true, Surface = GeneratedSurface.Reader)]\npublic static partial class Refused { }\n");
        Assert.HasCount(1, result.DiagnosticsWithId("CSG013"));
        StringAssert.Contains(result.DiagnosticsWithId("CSG013")[0].GetMessage(), "a.wide");
        Assert.IsEmpty(result.GeneratedSources);
    }

    /// <summary>A layout without a neutral value and a layout that a project turns on with the build property both generate, and the property default applies.</summary>
    [TestMethod]
    public void ProjectDefault_TurnsItOn()
    {
        const string Source = "using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct a { uint16 x; };\", Surface = GeneratedSurface.Parser)]\npublic static partial class Switched { }\n";
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: new System.Collections.Generic.Dictionary<string, string> { ["build_property.CStructSharpRuntimeByteOrder"] = "true" }).AssertClean();
        StringAssert.Contains(result.Source, "bool littleEndian");
        Assert.DoesNotContain("bool littleEndian", GeneratorRunner.Run(Source).AssertClean().Source);
    }
}
