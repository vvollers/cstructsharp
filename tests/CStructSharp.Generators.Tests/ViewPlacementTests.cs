namespace CStructSharp.Generators.Tests;

using System;
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

        [CStructLayout("struct paint { uint8 head; rgb colour; uint8 tail; };", Root = "paint", Codecs = new[] { "rgb:3:1" })]
        public static partial class PaintLayout
        {
            private static partial IReadOnlyList<ICustomCodec> CreateCodecs() => new ICustomCodec[] { new Rgb() };
        }

        [CStructLayout("struct late { uint8 n; uint8 items[n]; uint8 bytes[2]; uint8 to_object; };", Root = "late")]
        public static partial class LateLayout { }

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

    /// <summary>A member whose count needs a caller variable, and everything after it, is not exposed: it would always fail.</summary>
    [TestMethod]
    public void CountNeedingACallerVariable_ExposesNothingAfterIt()
    {
        Type view = assembly.GetType("Demo.FreeLayout")!.GetNestedType("FreeView")!;
        Assert.IsNotNull(view.GetProperty("Head"));
        Assert.IsNull(view.GetProperty("Behind"));
        Assert.IsNull(view.GetMethod("CountOfData"));
        Assert.IsNull(view.GetMethod("Data"));
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
}
