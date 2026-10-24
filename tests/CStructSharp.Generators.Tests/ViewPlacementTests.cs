namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Views past a runtime-sized array: the members after it are read at an offset the view computes from the members
///     before it, an array reads through its count, bytes, an element and a bulk copy, and text and custom codec members
///     read through the reader's own decoders. Every value equals what the generated reader and the runtime return, and
///     every failure is the reader's.
/// </summary>
[TestClass]
public class ViewPlacementTests
{
    private const string Consumer = """"
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using CStructSharp;
        using CStructSharp.Codecs;
        using CStructSharp.Diagnostics;

        namespace Demo;

        public sealed class Rgb : ICustomCodec
        {
            public string Name => "rgb";
            public int? FixedSize => 3;
            public int Alignment => 1;

            public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
            {
                value = null;
                bytesConsumed = 0;
                if (source.Length < 3) return OperationStatus.NeedMoreData;
                value = (source[0] << 16) | (source[1] << 8) | source[2];
                bytesConsumed = 3;
                return OperationStatus.Done;
            }

            public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
            {
                bytesWritten = 0;
                return OperationStatus.InvalidData;
            }
        }

        [CStructLayout("enum kind : uint8 { A = 1, B = 2 }; struct table { uint32 count; uint32 skip; uint64 origin; uint32 entries[count]; uint32 checksum; kind tag; uint8 tail[2]; };", Root = "table")]
        public static partial class TableLayout { }

        [CStructLayout("struct chain { uint8 n; uint16 a[n]; uint8 m; uint32 b[m]; uint8 last; };", Root = "chain", LittleEndian = false)]
        public static partial class ChainLayout { }

        [CStructLayout("struct padded { uint8 n; uint8 tags[n]; uint32 word; uint64 wide; uint16 end; };", Root = "padded", Aligned = true)]
        public static partial class PaddedLayout { }

        [CStructLayout("struct text { uint8 length; utf16le name[length]; uint8 after; latin1 code[4]; char legacy[length]; uint8 tail; };", Root = "text")]
        public static partial class TextLayout { }

        [CStructLayout("struct free { uint8 head; uint8 data[N]; uint8 behind; };", Root = "free")]
        public static partial class FreeLayout { }

        [CStructLayout("#define N 2\nstruct sized { uint8 data[N]; uint8 tail; };", Root = "sized")]
        public static partial class SizedLayout { }

        [CStructLayout("struct paint { uint8 head; rgb colour; uint8 tail; };", Root = "paint", Codecs = new[] { "rgb:3:1" })]
        public static partial class PaintLayout
        {
            private static partial IReadOnlyList<ICustomCodec> CreateCodecs() => new ICustomCodec[] { new Rgb() };
        }

        [CStructLayout("struct late { uint8 n; uint8 items[n]; uint8 bytes[2]; uint8 to_object; };", Root = "late")]
        public static partial class LateLayout { }

        [CStructLayout("struct hdr { char n; uint8 pad; }; struct pkt { hdr h; uint8 data[h.n]; uint32 tail; };", Root = "pkt")]
        public static partial class NestedCharCountLayout { }

        [CStructLayout("struct hdr { int48 n; uint8 pad; }; struct pkt { hdr h; uint8 data[h.n]; uint32 tail; };", Root = "pkt")]
        public static partial class NestedWideCountLayout { }

        [CStructLayout("struct hdr { uint8 n; uint8 pad; }; struct pkt { hdr h; uint8 data[h.n]; uint32 tail; };", Root = "pkt")]
        public static partial class NestedCountLayout { }

        [CStructLayout("struct clash { uint8 n; uint8 data[n]; uint32 data_bytes; uint32 vals[2]; uint32 vals_bytes; utf8 name[n]; uint32 name_bytes; };", Root = "clash")]
        public static partial class ClashLayout { }

        [CStructLayout("struct pointers { uint8 n; uint8 data[n]; uint8 *ptr; uint32 ptr_address; };", Root = "pointers", PointerSize = 2)]
        public static partial class PointerClashLayout { }

        [CStructLayout("struct leaf { uint8 id; uint8 *target; }; struct holder { uint8 n; uint8 xs[n]; leaf l; };", Root = "holder", PointerSize = 2)]
        public static partial class HolderLayout { }

        [CStructLayout("struct signed { int8 n; uint8 data[n]; uint8 tail; };", Root = "signed")]
        public static partial class NegativeCountLayout { }

        [CStructLayout("struct divided { uint8 d; uint8 data[100 / d]; uint8 tail; };", Root = "divided")]
        public static partial class DividedCountLayout { }

        [CStructLayout("struct nested { uint8 m; uint8 a[m]; uint8 n; uint8 b[n]; };", Root = "nested")]
        public static partial class CountAfterArrayLayout { }

        public static class Probe
        {
            public static object[] Table(byte[] bytes)
            {
                var view = new Demo.TableLayout.TableView(bytes);
                uint[] copy = new uint[view.CountOfEntries()];
                view.CopyEntriesTo(copy);
                return new object[] { view.CountOfEntries(), view.EntriesBytes.Length, copy.Length > 1 ? view.Entries(1) : 0u, copy, view.Checksum, (int)view.Tag, view.Tail(1) };
            }

            public static object[] Chain(byte[] bytes)
            {
                var view = new Demo.ChainLayout.ChainView(bytes);
                return new object[] { view.N, view.A(view.CountOfA() - 1), view.M, view.CountOfB(), view.B(0), view.Last };
            }

            public static object[] Padded(byte[] bytes)
            {
                var view = new Demo.PaddedLayout.PaddedView(bytes);
                return new object[] { view.N, view.Word, view.Wide, view.End };
            }

            public static object[] Text(byte[] bytes)
            {
                var view = new Demo.TextLayout.TextView(bytes);
                return new object[] { view.Name, view.NameBytes.Length, view.After, view.Code, view.Legacy, view.Tail };
            }

            public static object[] Paint(byte[] bytes)
            {
                var view = new Demo.PaintLayout.PaintView(bytes);
                return new object[] { view.Head, view.Colour, view.ColourBytes.Length, view.Tail };
            }

            public static object[] NestedCount(byte[] bytes)
            {
                var view = new Demo.NestedCountLayout.PktView(bytes);
                return new object[] { view.CountOfData(), view.Data(1), view.Tail };
            }

            public static object[] Clash(byte[] bytes)
            {
                var view = new Demo.ClashLayout.ClashView(bytes);
                return new object[] { view.CountOfData(), view.DataBytes_.Length, view.DataBytes, view.Vals(1), view.ValsBytes_.Length, view.ValsBytes, view.Name, view.NameBytes_.Length, view.NameBytes };
            }

            public static object[] PointerClash(byte[] bytes)
            {
                var view = new Demo.PointerClashLayout.PointersView(bytes);
                return new object[] { view.PtrAddress_, view.PtrAddress };
            }

            public static uint[] CopyTableLonger(byte[] bytes)
            {
                var view = new Demo.TableLayout.TableView(bytes);
                uint[] buffer = new uint[8];
                view.CopyEntriesTo(buffer);
                return buffer;
            }

            public static ushort[] CopyChainLonger(byte[] bytes)
            {
                var view = new Demo.ChainLayout.ChainView(bytes);
                ushort[] buffer = new ushort[6];
                view.CopyATo(buffer);
                return buffer;
            }

            public static uint[] CopyFixedAfterArrayLonger(byte[] bytes)
            {
                var view = new Demo.ClashLayout.ClashView(bytes);
                uint[] buffer = new uint[5];
                view.CopyValsTo(buffer);
                return buffer;
            }

            public static object[] Holder(byte[] bytes)
            {
                var view = new Demo.HolderLayout.HolderView(bytes);
                return new object[] { view.L.ToObject().Id };
            }

            public static object[] NegativeCount(byte[] bytes)
            {
                var view = new Demo.NegativeCountLayout.SignedView(bytes);
                return new object[] { view.CountOfData() };
            }

            public static object[] DividedCount(byte[] bytes)
            {
                var view = new Demo.DividedCountLayout.DividedView(bytes);
                return new object[] { view.CountOfData() };
            }

            public static object[] LimitedCount(byte[] bytes)
            {
                var view = new Demo.TableLayout.TableView(bytes, new ReadOptions { MaxArrayElements = 2 });
                return new object[] { view.CountOfEntries() };
            }

            public static object[] CountAfterArray(byte[] bytes)
            {
                var view = new Demo.CountAfterArrayLayout.NestedView(bytes);
                return new object[] { view.CountOfB() };
            }

            public static object[] Free(byte[] bytes)
            {
                var view = new Demo.FreeLayout.FreeView(bytes, null, new Dictionary<string, int> { ["N"] = 3 });
                return new object[] { view.Head, view.CountOfData(), view.Data(2), view.Behind };
            }

            public static object[] FreeWithoutVariables(byte[] bytes)
            {
                var view = new Demo.FreeLayout.FreeView(bytes);
                return new object[] { view.CountOfData() };
            }

            public static object[] Sized(byte[] bytes)
            {
                var plain = new Demo.SizedLayout.SizedView(bytes);
                var wide = new Demo.SizedLayout.SizedView(bytes, null, new Dictionary<string, int> { ["N"] = 3 });
                return new object[] { plain.CountOfData(), plain.Tail, wide.CountOfData(), wide.Tail, wide.ToObject().Data.Length };
            }

            public static object[] Late(byte[] bytes)
            {
                var view = new Demo.LateLayout.LateView(bytes);
                return new object[] { view.N, view.CountOfItems(), view.ToObject_ };
            }
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates and compiles the consumer once for every test.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("view.ToObject_", "0", StringComparison.Ordinal));
        assembly = result.AssertClean().Load();
    }

    /// <summary>Calls a probe method and unwraps a failure it throws.</summary>
    private static object[] Probe(string method, byte[] bytes)
    {
        try
        {
            return (object[])assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, [bytes])!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>The runtime layout of a generated class.</summary>
    /// <param name="layout">The class name without its <c>Layout</c> suffix.</param>
    /// <returns>The runtime layout.</returns>
    private static CStruct Runtime(string layout) => (CStruct)assembly.GetType("Demo." + layout + "Layout")!.GetProperty("Layout")!.GetValue(null)!;

    /// <summary>The generated reader's value of a layout's root, as a dynamic bag of properties.</summary>
    private static object Parsed(string layout, byte[] bytes)
    {
        Type type = assembly.GetType("Demo." + layout + "Layout")!;
        MethodInfo parse = type.GetMethods().Single(method => method.Name == "Parse" && method.GetParameters() is [{ ParameterType: { } first }, _] && first == typeof(byte[]));
        try
        {
            return parse.Invoke(null, [bytes, null])!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>The generated reader's value of a layout's root with caller variables.</summary>
    private static object Parsed(string layout, byte[] bytes, IReadOnlyDictionary<string, int> variables)
    {
        MethodInfo parse = assembly.GetType("Demo." + layout + "Layout")!.GetMethods().Single(method => method.Name == "Parse" + layout && method.GetParameters() is [{ ParameterType: { } first }, ..] && first == typeof(byte[]));
        try
        {
            return parse.Invoke(null, [bytes, variables, null])!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>Reads a property of a generated object by name.</summary>
    /// <param name="value">The object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The property value.</returns>
    private static object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;

    /// <summary>Members after a runtime-sized array read at the computed offset, and the array reads by count, element, and bulk copy.</summary>
    [TestMethod]
    public void TableMembersAfterARuntimeArray_ReadLikeTheReader()
    {
        // count 3, skip, origin, entries 10 20 30, checksum 0xCAFE, tag B, tail 5 6.
        byte[] bytes = [3, 0, 0, 0, 9, 9, 9, 9, 1, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 20, 0, 0, 0, 30, 0, 0, 0, 0xFE, 0xCA, 0, 0, 2, 5, 6];
        object[] readings = Probe("Table", bytes);
        object parsed = Parsed("Table", bytes);
        Assert.AreEqual(3, readings[0]);
        Assert.AreEqual(12, readings[1]);
        Assert.AreEqual(20u, readings[2]);
        CollectionAssert.AreEqual((uint[])Property(parsed, "Entries"), (uint[])readings[3]);
        Assert.AreEqual(Property(parsed, "Checksum"), readings[4]);
        Assert.AreEqual(Convert.ToInt32(Property(parsed, "Tag")), readings[5]);
        Assert.AreEqual(((byte[])Property(parsed, "Tail"))[1], readings[6]);
        ParityComparer.AssertSame(Runtime("Table").Parse(bytes, "table"), parsed, "table", strict: true);
    }

    /// <summary>A view of a table whose count claims more than the source holds fails with the reader's short-read failure naming the array.</summary>
    [TestMethod]
    public void ArrayPastTheSource_FailsWithAShortRead()
    {
        byte[] bytes = [3, 0, 0, 0, 9, 9, 9, 9, 1, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0];
        CStructReadException exception = Assert.Throws<CStructReadException>(() => Probe("Table", bytes));
        StringAssert.Contains(exception.Message, "Not enough bytes");
        StringAssert.Contains(exception.Message, "entries");
        Assert.Throws<CStructReadException>(() => Parsed("Table", bytes), "the reader fails on the same input");
    }

    /// <summary>A count over the array limit fails with the reader's limit failure, and the options of the view change the limit.</summary>
    [TestMethod]
    public void CountOverTheLimit_FailsLikeTheReader()
    {
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Exception view = Assert.Throws<CStructReadLimitException>(() => Probe("Table", bytes));
        Exception reader = Assert.Throws<CStructReadLimitException>(() => Runtime("Table").Parse(bytes, "table"));
        Assert.AreEqual(reader.GetType(), view.GetType());
        StringAssert.Contains(view.Message, "MaxArrayElements");
    }

    /// <summary>Two runtime-sized arrays in a row: each offset builds on the one before, with big-endian elements.</summary>
    [TestMethod]
    public void ChainedRuntimeArrays_BuildOnEachOther()
    {
        // n 2, a = 0x0102 0x0304, m 3, b = 7 8 9 (uint32 big-endian), last.
        byte[] bytes = [2, 1, 2, 3, 4, 3, 0, 0, 0, 7, 0, 0, 0, 8, 0, 0, 0, 9, 0x5A];
        object[] readings = Probe("Chain", bytes);
        object parsed = Parsed("Chain", bytes);
        Assert.AreEqual((ushort)0x0304, readings[1]);
        Assert.AreEqual(3, readings[3]);
        Assert.AreEqual(7u, readings[4]);
        Assert.AreEqual((byte)0x5A, readings[5]);
        Assert.AreEqual(Property(parsed, "Last"), readings[5]);
        ParityComparer.AssertSame(Runtime("Chain").Parse(bytes, "chain"), parsed, "chain", strict: true);
    }

    /// <summary>In an aligned layout the member after a runtime-sized array is rounded up to its alignment.</summary>
    [TestMethod]
    public void AlignedLayout_RoundsTheComputedOffsetUp()
    {
        foreach (int tags in new[] { 0, 1, 2, 3, 5 })
        {
            // n, tags[n], padding to 4, word, padding to 8, wide, end.
            int wordAt = (1 + tags + 3) & ~3;
            int wideAt = (wordAt + 4 + 7) & ~7;
            byte[] bytes = new byte[(wideAt + 8 + 2 + 7) & ~7];
            bytes[0] = (byte)tags;
            BitConverter.GetBytes(0xA1B2C3D4u).CopyTo(bytes, wordAt);
            BitConverter.GetBytes(0x1122334455667788UL).CopyTo(bytes, wideAt);
            BitConverter.GetBytes((ushort)0xBEEF).CopyTo(bytes, wideAt + 8);
            object[] readings = Probe("Padded", bytes);
            object parsed = Parsed("Padded", bytes);
            Assert.AreEqual(Property(parsed, "Word"), readings[1], $"word with {tags} tags");
            Assert.AreEqual(Property(parsed, "Wide"), readings[2], $"wide with {tags} tags");
            Assert.AreEqual(Property(parsed, "End"), readings[3], $"end with {tags} tags");
            ParityComparer.AssertSame(Runtime("Padded").Parse(bytes, "padded"), parsed, "padded", strict: true);
        }
    }

    /// <summary>Encoded text arrays read through the reader's decoder: UTF-16 sized by a field, a fixed Latin-1 buffer, and a sized character array.</summary>
    [TestMethod]
    public void TextArrays_DecodeLikeTheReader()
    {
        // length 4 → utf16le name takes 4 bytes ("hi"), after, latin1 code "AéZ\0", legacy char[4], tail.
        byte[] bytes = [4, (byte)'h', 0, (byte)'i', 0, 7, (byte)'A', 0xE9, (byte)'Z', 0, (byte)'w', (byte)'x', (byte)'y', (byte)'z', 9];
        object[] readings = Probe("Text", bytes);
        object parsed = Parsed("Text", bytes);
        Assert.AreEqual("hi", readings[0]);
        Assert.AreEqual(4, readings[1]);
        Assert.AreEqual(Property(parsed, "Name"), readings[0]);
        Assert.AreEqual(Property(parsed, "Code"), readings[3]);
        Assert.AreEqual(Property(parsed, "Legacy"), readings[4]);
        Assert.AreEqual((byte)9, readings[5]);
        ParityComparer.AssertSame(Runtime("Text").Parse(bytes, "text"), parsed, "text", strict: true);
    }

    /// <summary>A lone UTF-16 surrogate fails in the view with the reader's own text failure.</summary>
    [TestMethod]
    public void InvalidText_FailsLikeTheReader()
    {
        byte[] bytes = [2, 0x00, 0xD8, 7, (byte)'A', (byte)'B', (byte)'C', (byte)'D', 1, 2, 9];
        Exception reader = Assert.Throws<CStructReadException>(() => Runtime("Text").Parse(bytes, "text"));
        Exception view = Assert.Throws<CStructReadException>(() => Probe("Text", bytes));
        Assert.AreEqual(reader.InnerException?.GetType(), view.InnerException?.GetType());
    }

    /// <summary>A count that needs a caller variable is exposed: the view takes the variables, evaluates the count with them like the reader, and finds the members after the array.</summary>
    [TestMethod]
    public void CountNeedingACallerVariable_UsesTheVariablesTheViewWasGiven()
    {
        byte[] bytes = [1, 10, 20, 30, 99];
        object[] readings = Probe("Free", bytes);
        Assert.AreEqual((byte)1, readings[0]);
        Assert.AreEqual(3, readings[1]);
        Assert.AreEqual((byte)30, readings[2]);
        Assert.AreEqual((byte)99, readings[3]);
        var variables = new Dictionary<string, int> { ["N"] = 3 };
        ParityComparer.AssertSame(Runtime("Free").Parse(bytes, "free", variables), Parsed("Free", bytes, variables), "free", strict: true);

        // Without the variable the count fails as the reader's does: the identifier is undefined.
        Type view = assembly.GetType("Demo.FreeLayout")!.GetNestedType("FreeView")!;
        Assert.IsNotNull(view.GetMethod("CountOfData"));
        CStructException reader = Assert.Throws<CStructReadException>(() => Runtime("Free").Parse(bytes, "free"));
        CStructException missing = Assert.Throws<CStructReadException>(() => Probe("FreeWithoutVariables", bytes));
        Assert.AreEqual(FailureText(reader), FailureText(missing));
    }

    /// <summary>A define's default applies to a view made without variables, a caller's value overrides it, and <c>ToObject</c> reads with the variables the view carries.</summary>
    [TestMethod]
    public void DefineOverride_AppliesToTheViewAndItsToObject()
    {
        byte[] bytes = [1, 2, 3, 4];
        object[] readings = Probe("Sized", bytes);
        Assert.AreEqual(2, readings[0]);
        Assert.AreEqual((byte)3, readings[1]);
        Assert.AreEqual(3, readings[2]);
        Assert.AreEqual((byte)4, readings[3]);
        Assert.AreEqual(3, readings[4], "ToObject reads with the view's variables");
    }

    /// <summary>A fixed-size custom codec member reads through the codec instance and also exposes its stored bytes.</summary>
    [TestMethod]
    public void CustomCodecMember_ReadsThroughTheCodec()
    {
        byte[] bytes = [1, 0x10, 0x20, 0x30, 4];
        object[] readings = Probe("Paint", bytes);
        Assert.AreEqual(0x102030, readings[1]);
        Assert.AreEqual(3, readings[2]);
        Assert.AreEqual((byte)4, readings[3]);
        ParityComparer.AssertSame(Runtime("Paint").Parse(bytes, "paint"), Parsed("Paint", bytes), "paint", strict: true);
    }

    /// <summary>A member after a runtime-sized array whose name the view already owns stays hidden instead of breaking the build.</summary>
    [TestMethod]
    public void LateMemberNamedLikeAViewMember_StaysHidden()
    {
        Type view = assembly.GetType("Demo.LateLayout")!.GetNestedType("LateView")!;
        Assert.IsNull(view.GetProperty("ToObject_"));
        Assert.IsNotNull(view.GetMethod("CountOfItems"));
        Assert.IsNotNull(view.GetMethod("ToObject"));
    }

    /// <summary>A count that names a member of a nested struct the nested view cannot expose hides the array instead of breaking the build.</summary>
    [TestMethod]
    public void CountNamingAnUnexposedNestedMember_HidesTheArray()
    {
        foreach (string layout in new[] { "NestedCharCount", "NestedWideCount" })
        {
            Type view = assembly.GetType("Demo." + layout + "Layout")!.GetNestedType("PktView")!;
            Assert.IsNotNull(view.GetProperty("H"), layout);
            Assert.IsNull(view.GetMethod("CountOfData"), layout);
            Assert.IsNull(view.GetProperty("Tail"), layout);
        }
    }

    /// <summary>A count that names an exposed member of a nested struct is evaluated by the view like the reader's.</summary>
    [TestMethod]
    public void CountNamingAnExposedNestedMember_PlacesTheArray()
    {
        byte[] bytes = [2, 0, 5, 6, 1, 2, 3, 4];
        object[] readings = Probe("NestedCount", bytes);
        object parsed = Parsed("NestedCount", bytes);
        Assert.AreEqual(2, readings[0]);
        Assert.AreEqual((byte)6, readings[1]);
        Assert.AreEqual(Property(parsed, "Tail"), readings[2]);
    }

    /// <summary>The bytes of <c>clash</c>: n = 2, data, data_bytes, vals[2], vals_bytes, name, name_bytes.</summary>
    private static byte[] ClashBytes() =>
    [
        2, 0x0A, 0x0B,
        .. BitConverter.GetBytes(11u),
        .. BitConverter.GetBytes(21u), .. BitConverter.GetBytes(22u),
        .. BitConverter.GetBytes(33u),
        (byte)'h', (byte)'i',
        .. BitConverter.GetBytes(44u),
    ];

    /// <summary>A derived bytes member never takes the name of a member of the layout, whatever the member's placement.</summary>
    [TestMethod]
    public void DerivedBytesMembers_AvoidTheLayoutsOwnNames()
    {
        byte[] bytes = ClashBytes();
        object[] readings = Probe("Clash", bytes);
        object parsed = Parsed("Clash", bytes);
        Assert.AreEqual(2, readings[0]);
        Assert.AreEqual(2, readings[1]);
        Assert.AreEqual(Property(parsed, "DataBytes"), readings[2]);
        Assert.AreEqual(22u, readings[3]);
        Assert.AreEqual(8, readings[4]);
        Assert.AreEqual(Property(parsed, "ValsBytes"), readings[5]);
        Assert.AreEqual("hi", readings[6]);
        Assert.AreEqual(2, readings[7]);
        Assert.AreEqual(Property(parsed, "NameBytes"), readings[8]);
    }

    /// <summary>A stored pointer address never takes the name of a member of the layout.</summary>
    [TestMethod]
    public void PointerAddress_AvoidsTheLayoutsOwnNames()
    {
        byte[] bytes = [1, 7, 0x34, 0x12, 0x78, 0x56, 0x34, 0x12];
        object[] readings = Probe("PointerClash", bytes);
        Assert.AreEqual(0x1234L, readings[0]);
        Assert.AreEqual(0x12345678u, readings[1]);
    }

    /// <summary>A destination longer than the array is filled with the array's elements only; the rest is left alone.</summary>
    [TestMethod]
    public void CopyTo_AcceptsALongerDestination()
    {
        // count 3, skip, origin, entries 10 20 30, checksum, tag, tail.
        byte[] table = [3, 0, 0, 0, 9, 9, 9, 9, 1, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 20, 0, 0, 0, 30, 0, 0, 0, 0xFE, 0xCA, 0, 0, 2, 5, 6];
        CollectionAssert.AreEqual(new uint[] { 10, 20, 30, 0, 0, 0, 0, 0 }, (uint[])Call("CopyTableLonger", table));

        // n 2, a = 0x0102 0x0304 (big-endian), m 0, last.
        byte[] chain = [2, 1, 2, 3, 4, 0, 0x5A];
        CollectionAssert.AreEqual(new ushort[] { 0x0102, 0x0304, 0, 0, 0, 0 }, (ushort[])Call("CopyChainLonger", chain));

        CollectionAssert.AreEqual(new uint[] { 21, 22, 0, 0, 0 }, (uint[])Call("CopyFixedAfterArrayLonger", ClashBytes()));
    }

    /// <summary>A nested view at a computed offset keeps the bytes after it, so its pointer targets read like those of a nested view at a constant offset.</summary>
    [TestMethod]
    public void NestedViewAtAComputedOffset_SeesTheBytesAfterIt()
    {
        // n 1, xs = 9, leaf (id 4, target address 3), then the target byte after the leaf.
        byte[] bytes = [1, 9, 4, 3, 0, 0x55];
        Assert.AreEqual((byte)4, Probe("Holder", bytes)[0]);
    }

    /// <summary>A negative count, a count expression that fails, and a count over a custom array limit each fail in the view with the reader's own message, type, and member.</summary>
    [TestMethod]
    public void CountFailures_MatchTheReader()
    {
        (string Layout, string Probe, string Root, byte[] Bytes)[] cases =
        [
            ("NegativeCount", "NegativeCount", "signed", [0xFF, 1, 2]),
            ("DividedCount", "DividedCount", "divided", [0, 1, 2]),
        ];
        foreach ((string layout, string probe, string root, byte[] bytes) in cases)
        {
            CStructException reader = Assert.Throws<CStructReadException>(() => Runtime(layout).Parse(bytes, root));
            CStructException view = Assert.Throws<CStructReadException>(() => Probe(probe, bytes));
            Assert.AreEqual(FailureText(reader), FailureText(view), layout);
            Assert.AreEqual(reader.Member, view.Member, layout);
        }

        // count 3 over a limit of 2.
        byte[] table = [3, 0, 0, 0, 9, 9, 9, 9, 1, 0, 0, 0, 0, 0, 0, 0, 10, 0, 0, 0, 20, 0, 0, 0, 30, 0, 0, 0, 0xFE, 0xCA, 0, 0, 2, 5, 6];
        var limited = new ReadOptions { MaxArrayElements = 2 };
        CStructException readerLimit = Assert.Throws<CStructReadLimitException>(() => Runtime("Table").Parse(table, "table", options: limited));
        CStructException viewLimit = Assert.Throws<CStructReadLimitException>(() => Probe("LimitedCount", table));
        Assert.AreEqual(FailureText(readerLimit), FailureText(viewLimit));
        Assert.AreEqual(readerLimit.Member, viewLimit.Member);
    }

    /// <summary>A count that is a member at a computed offset, on a source that ends before it, fails with the reader's exception type and text.</summary>
    [TestMethod]
    public void CountMemberPastTheSource_FailsLikeTheReader()
    {
        // m 2, a = 1 2, then the source ends where n should be.
        byte[] bytes = [2, 1, 2];
        CStructException reader = Assert.Throws<CStructReadException>(() => Runtime("CountAfterArray").Parse(bytes, "nested"));
        CStructException view = Assert.Throws<CStructReadException>(() => Probe("CountAfterArray", bytes));
        StringAssert.Contains(reader.Message, "Not enough bytes");
        StringAssert.Contains(view.Message, "Not enough bytes");
        Assert.AreEqual(reader.Member, view.Member);
        Assert.AreEqual(2, Probe("CountAfterArray", [2, 1, 2, 2, 7, 8])[0]);
    }

    /// <summary>A failure's message without the trailing field, path and offset context, which differs between a view and a whole-value read.</summary>
    private static string FailureText(CStructException exception)
    {
        int context = exception.Message.IndexOf(" (field", StringComparison.Ordinal);
        return context < 0 ? exception.Message : exception.Message[..context];
    }

    /// <summary>Calls a probe method that returns something other than an object array.</summary>
    private static object Call(string method, byte[] bytes)
    {
        try
        {
            return assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, [bytes])!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }
}
