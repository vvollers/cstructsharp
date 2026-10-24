namespace CStructSharp.Generators.Tests;

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Value-type layouts with fixed arrays of structs and of enums, and with a larger size cap: each array is an inline array
///     filled in place (by the fixed reader, or by the member-by-member reader when a nested struct has a condition), every
///     element equals what the runtime layout reads, and a layout over the cap is refused with the cap in the message.
/// </summary>
[TestClass]
public class ValueTypeArraysTests
{
    private const string Layout = "enum kind : uint16 { A = 1, B = 2, C = 3 }; struct inner { uint16 a; uint8 b; }; struct rec { uint32 id; inner items[3]; kind kinds[2]; uint8 tail; };";

    private const string Checked = "struct leaf { uint8 id @expect(id != 0, \\\"leaf.id\\\"); uint16 w; }; struct holder { uint8 n; leaf leaves[2]; };";

    private const string Consumer = """"
        using System;
        using CStructSharp;

        namespace Demo;

        [CStructLayout("@LAYOUT@", Root = "rec", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader)]
        public static partial class ReaderRec { }

        [CStructLayout("@LAYOUT@", Root = "rec", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser, RuntimeByteOrder = true)]
        public static partial class OrderRec { }

        [CStructLayout("@LAYOUT@", Root = "rec", Shape = GeneratedShape.Auto, Surface = GeneratedSurface.Parser, Aligned = true)]
        public static partial class AlignedRec { }

        [CStructLayout("@CHECKED@", Root = "holder", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader)]
        public static partial class CheckedHolder { }

        [CStructLayout("struct big { uint8 data[3000]; uint32 tail; };", Root = "big", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser, MaxValueBytes = 4096)]
        public static partial class BigRec { }

        [CStructLayout("struct signed { int8 v[3]; uint8 t; };", Root = "signed", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]
        public static partial class SignedRec { }

        public static class Probe
        {
            public static string Signed(byte[] bytes)
            {
                var s = SignedRec.Parse(bytes);
                return s.V[0] + "," + s.V[1] + "," + s.V[2] + "," + s.T + "," + s.V.Length;
            }

            public static string Describe(ReaderRec.Rec r)
                => r.Id + "|" + string.Join(";", Enumerable(r.Items.Length, i => r.Items[i].A + "," + r.Items[i].B)) + "|" + string.Join(",", Enumerable(r.Kinds.Length, i => ((int)r.Kinds[i]).ToString())) + "|" + r.Tail;

            public static string DescribeOrder(OrderRec.Rec r)
                => r.Id + "|" + string.Join(";", Enumerable(r.Items.Length, i => r.Items[i].A + "," + r.Items[i].B)) + "|" + string.Join(",", Enumerable(r.Kinds.Length, i => ((int)r.Kinds[i]).ToString())) + "|" + r.Tail;

            public static string DescribeAligned(AlignedRec.Rec r)
                => r.Id + "|" + string.Join(";", Enumerable(r.Items.Length, i => r.Items[i].A + "," + r.Items[i].B)) + "|" + string.Join(",", Enumerable(r.Kinds.Length, i => ((int)r.Kinds[i]).ToString())) + "|" + r.Tail;

            private static string[] Enumerable(int count, Func<int, string> item)
            {
                var result = new string[count];
                for (int i = 0; i < count; i++) { result[i] = item(i); }
                return result;
            }

            public static string Read(byte[] bytes) => Describe(ReaderRec.Parse(bytes));

            public static string ReadOrder(byte[] bytes, bool little) => DescribeOrder(OrderRec.Parse(bytes, little));

            public static string ReadAligned(byte[] bytes) => DescribeAligned(AlignedRec.Parse(bytes));

            public static bool AlignedIsValueType() => typeof(AlignedRec.Rec).IsValueType;

            public static string Holder(byte[] bytes)
            {
                var h = CheckedHolder.Parse(bytes);
                return h.N + "|" + h.Leaves[0].Id + "," + h.Leaves[0].W + ";" + h.Leaves[1].Id + "," + h.Leaves[1].W;
            }

            public static int BigTail(byte[] bytes) => (int)BigRec.Parse(bytes).Tail;

            public static long Allocated(byte[] bytes, int rounds)
            {
                for (int i = 0; i < 200; i++) { _ = ReaderRec.Parse(bytes); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                uint sum = 0;
                for (int i = 0; i < rounds; i++)
                {
                    var r = ReaderRec.Parse(bytes);
                    sum += r.Id + r.Items[2].A + (uint)r.Kinds[1];
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
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("@LAYOUT@", Layout, StringComparison.Ordinal).Replace("@CHECKED@", Checked, StringComparison.Ordinal)).AssertClean();
        assembly = result.Load();
    }

    /// <summary>Calls a probe method and unwraps a failure it throws.</summary>
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

    /// <summary>What the runtime layout reads, formatted like the probe.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="littleEndian">The byte order.</param>
    /// <returns>The description.</returns>
    private static string Expected(byte[] bytes, bool littleEndian = true)
    {
        StructValue value = new CStruct(Layout, isLittleEndian: littleEndian).Parse(bytes, "rec");
        IList items = (IList)value["items"]!;
        IList kinds = (IList)value["kinds"]!;
        return Convert.ToUInt32(value["id"]) + "|" + string.Join(";", items.Cast<StructValue>().Select(item => Convert.ToUInt32(item["a"]) + "," + Convert.ToUInt32(item["b"]))) + "|" + string.Join(",", kinds.Cast<EnumValueResult>().Select(kind => (int)kind.Value)) + "|" + Convert.ToUInt32(value["tail"]);
    }

    /// <summary>The fields of the value are inline arrays of the element types.</summary>
    [TestMethod]
    public void Arrays_AreInlineArraysOfStructsAndEnums()
    {
        Type rec = assembly.GetType("Demo.ReaderRec+Rec")!;
        Assert.IsTrue(rec.IsValueType);
        Assert.AreEqual("InnerArray3", rec.GetField("Items")!.FieldType.Name);
        Assert.AreEqual("KindArray2", rec.GetField("Kinds")!.FieldType.Name);
        Assert.IsTrue(rec.GetField("Items")!.FieldType.IsValueType);
        Assert.IsTrue((bool)Call("AlignedIsValueType"));
    }

    /// <summary>Every element of every array equals what the runtime layout reads, for fixed inputs and for random ones.</summary>
    [TestMethod]
    public void Elements_ReadLikeTheRuntime()
    {
        byte[] bytes = [1, 0, 0, 0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 1, 0, 3, 0, 0xEE];
        Assert.AreEqual(Expected(bytes), (string)Call("Read", bytes));

        var random = new Random(7);
        for (int round = 0; round < 50; round++)
        {
            byte[] noise = new byte[bytes.Length];
            random.NextBytes(noise);
            Assert.AreEqual(Expected(noise), (string)Call("Read", noise), "round " + round);
        }
    }

    /// <summary>With a run-time byte order the elements of the arrays decode in the order given.</summary>
    [TestMethod]
    public void Elements_HonourARuntimeByteOrder()
    {
        byte[] bytes = [1, 2, 3, 4, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0, 1, 0, 3, 0xEE];
        Assert.AreEqual(Expected(bytes, true), (string)Call("ReadOrder", bytes, true));
        Assert.AreEqual(Expected(bytes, false), (string)Call("ReadOrder", bytes, false));
    }

    /// <summary>An array of signed bytes is an inline array too, decoded with its sign.</summary>
    [TestMethod]
    public void SignedByteArrays_AreInlineArrays()
    {
        Assert.AreEqual("-1,2,-128,9,3", (string)Call("Signed", new byte[] { 0xFF, 2, 0x80, 9 }));
    }

    /// <summary>A short source fails with the runtime's failure, a read allocates nothing, and an aligned Auto layout is still a value type.</summary>
    [TestMethod]
    public void ShortSources_Fail_AndReadsAllocateNothing()
    {
        var failure = Assert.Throws<CStructReadException>(() => Call("Read", new byte[] { 1, 0, 0, 0, 1, 2 }));
        StringAssert.Contains(failure.Message, "Not enough bytes");
        Assert.AreEqual(0L, (long)Call("Allocated", new byte[] { 1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 1, 0, 2, 0, 3 }, 5000));
        string aligned = (string)Call("ReadAligned", new byte[24]);
        Assert.IsTrue(aligned.StartsWith("0|", StringComparison.Ordinal));
    }

    /// <summary>An array of structs with conditions is read member by member into the inline array, and a failing element reports the reader's failure.</summary>
    [TestMethod]
    public void ArraysOfConstrainedStructs_AreReadElementByElement()
    {
        Assert.AreEqual("2|5,258;9,1", (string)Call("Holder", new byte[] { 2, 5, 2, 1, 9, 1, 0 }));
        var failure = Assert.Throws<CStructConstraintException>(() => Call("Holder", new byte[] { 2, 5, 2, 1, 0, 1, 0 }));
        Assert.AreEqual("leaf.id", failure.Label);
        Assert.AreEqual("id", failure.Member);
    }

    /// <summary>A layout over the default cap is generated when the attribute raises the cap, and is refused with the cap in the message when it does not.</summary>
    [TestMethod]
    public void MaxValueBytes_RaisesTheCap()
    {
        byte[] big = new byte[3004];
        big[3000] = 9;
        Assert.AreEqual(9, (int)Call("BigTail", big));

        const string Source = "using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct big { uint8 data[3000]; };\", Root = \"big\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser@OPTION@)]\npublic static partial class Sample { }";
        GeneratorResult refused = GeneratorRunner.Run(Source.Replace("@OPTION@", string.Empty, StringComparison.Ordinal));
        Assert.AreEqual(1, refused.DiagnosticsWithId("CSG014").Count);
        StringAssert.Contains(refused.DiagnosticsWithId("CSG014")[0].GetMessage(), "at most 1024 bytes");
        GeneratorResult capped = GeneratorRunner.Run(Source.Replace("@OPTION@", ", MaxValueBytes = 2000", StringComparison.Ordinal));
        StringAssert.Contains(capped.DiagnosticsWithId("CSG014").Single().GetMessage(), "at most 2000 bytes");
        Assert.AreEqual(0, GeneratorRunner.Run(Source.Replace("@OPTION@", ", MaxValueBytes = 3000", StringComparison.Ordinal)).DiagnosticsWithId("CSG014").Count);
        Assert.AreEqual(1, GeneratorRunner.Run(Source.Replace("@OPTION@", ", MaxValueBytes = 0", StringComparison.Ordinal)).DiagnosticsWithId("CSG014").Count);
    }

    /// <summary>The project default <c>CStructSharpMaxValueBytes</c> sets the cap for every layout that does not name one.</summary>
    [TestMethod]
    public void ProjectDefault_MaxValueBytes_AppliesToLayoutsThatNameNone()
    {
        const string Source = "using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"struct big { uint8 data[3000]; };\", Root = \"big\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]\npublic static partial class Sample { }";
        var options = new System.Collections.Generic.Dictionary<string, string> { ["build_property.CStructSharpMaxValueBytes"] = "4096" };
        GeneratorResult result = GeneratorRunner.Run(Source, globalOptions: options);
        Assert.AreEqual(0, result.DiagnosticsWithId("CSG014").Count);
        var invalid = new System.Collections.Generic.Dictionary<string, string> { ["build_property.CStructSharpMaxValueBytes"] = "0" };
        Assert.AreEqual(1, GeneratorRunner.Run(Source, globalOptions: invalid).DiagnosticsWithId("CSG012").Count);
    }
}
