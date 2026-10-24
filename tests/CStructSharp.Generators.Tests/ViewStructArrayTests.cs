namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Views over arrays of fixed-size structs: <c>Entries(i)</c> returns a nested view of one element at a stride of the
///     element's size, for a fixed array and for a runtime-sized one, with bitfield elements, in aligned layouts and for
///     union elements, and every member read equals what the runtime layout and the generated reader return.
/// </summary>
[TestClass]
public class ViewStructArrayTests
{
    private const string Consumer = """"
        using System;
        using CStructSharp;

        namespace Demo;

        [CStructLayout("struct chunk { uint32 offset:31; uint32 compressed:1; }; struct table { uint32 count; chunk entries[count]; uint32 checksum; };", Root = "table")]
        public static partial class TableLayout { }

        [CStructLayout("struct item { uint16 tag; uint8 flags:3; uint8 level:5; }; struct holder { uint8 n; item items[3]; uint32 tail; };", Root = "holder")]
        public static partial class HolderLayout { }

        [CStructLayout("struct pair { uint8 a; uint32 b; }; struct padded { uint8 n; pair pairs[n]; uint16 end; };", Root = "padded", Aligned = true)]
        public static partial class PaddedLayout { }

        [CStructLayout("union word { uint8 low; uint16 whole; }; struct words { word items[2]; uint8 tail; };", Root = "words")]
        public static partial class WordsLayout { }

        [CStructLayout("struct inner { uint8 x; }; struct outer { inner items[2]; uint8 items_bytes; };", Root = "outer")]
        public static partial class ClashLayout { }

        public static class Probe
        {
            public static object[] Table(byte[] bytes)
            {
                var view = new Demo.TableLayout.TableView(bytes);
                int count = view.CountOfEntries();
                var offsets = new uint[count];
                var flags = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    offsets[i] = view.Entries(i).Offset;
                    flags[i] = view.Entries(i).Compressed != 0;
                }

                return new object[] { count, view.EntriesBytes.Length, offsets, flags, view.Checksum };
            }

            public static object[] Holder(byte[] bytes)
            {
                var view = new Demo.HolderLayout.HolderView(bytes);
                return new object[] { view.Items(0).Tag, view.Items(1).Flags, view.Items(2).Level, view.ItemsBytes.Length, view.Tail };
            }

            public static object[] Padded(byte[] bytes)
            {
                var view = new Demo.PaddedLayout.PaddedView(bytes);
                int count = view.CountOfPairs();
                var b = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    b[i] = view.Pairs(i).B;
                }

                return new object[] { b, view.End };
            }

            public static object[] Words(byte[] bytes)
            {
                var view = new Demo.WordsLayout.WordsView(bytes);
                return new object[] { view.Items(0).Low, view.Items(1).Whole, view.Tail };
            }

            public static object[] OutOfRange(byte[] bytes)
            {
                var view = new Demo.HolderLayout.HolderView(bytes);
                return new object[] { view.Items(3).Tag };
            }

            public static object[] Clash(byte[] bytes)
            {
                var view = new Demo.ClashLayout.OuterView(bytes);
                return new object[] { view.Items(1).X, view.ItemsBytes, view.ItemsBytes_.Length };
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
        GeneratorResult result = GeneratorRunner.Run(Consumer);
        assembly = result.AssertClean().Load();
    }

    /// <summary>Calls a probe method and unwraps a failure it throws.</summary>
    /// <param name="method">The probe method.</param>
    /// <param name="bytes">The bytes the view reads.</param>
    /// <returns>The readings.</returns>
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

    /// <summary>A runtime-sized array of bitfield structs reads each element's fields through the nested view, and the member after the array finds its offset.</summary>
    [TestMethod]
    public void RuntimeArrayOfBitfieldStructs_ReadsLikeTheReader()
    {
        // count 3, entries 5 (compressed), 0x7FFFFFFF, 0 (uncompressed), checksum 0xBEEF.
        byte[] bytes = [3, 0, 0, 0, 5, 0, 0, 0x80, 0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0, 0, 0xEF, 0xBE, 0, 0];
        object[] readings = Probe("Table", bytes);
        Assert.AreEqual(3, readings[0]);
        Assert.AreEqual(12, readings[1]);
        CollectionAssert.AreEqual(new uint[] { 5, 0x7FFFFFFF, 0 }, (uint[])readings[2]);
        CollectionAssert.AreEqual(new[] { true, false, false }, (bool[])readings[3]);
        Assert.AreEqual(0xBEEFu, readings[4]);
        StructValue parsed = Runtime("Table").Parse(bytes, "table");
        var entries = (System.Collections.IList)parsed["entries"]!;
        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(5u, Convert.ToUInt32(((StructValue)entries[0]!)["offset"]));
        Assert.AreEqual(1u, Convert.ToUInt32(((StructValue)entries[0]!)["compressed"]));
    }

    /// <summary>A fixed array of structs gives its elements at the element size, and its bytes are every element in a row.</summary>
    [TestMethod]
    public void FixedArrayOfStructs_ReadsLikeTheReader()
    {
        // n, three items (tag, flags:3 level:5), tail.
        byte[] bytes = [9, 0x01, 0x00, 0b10101_011, 0x02, 0x00, 0b00000_111, 0x03, 0x00, 0b11111_000, 0x78, 0x56, 0x34, 0x12];
        object[] readings = Probe("Holder", bytes);
        Assert.AreEqual((ushort)1, readings[0]);
        Assert.AreEqual((byte)7, readings[1]);
        Assert.AreEqual((byte)31, readings[2]);
        Assert.AreEqual(9, readings[3]);
        Assert.AreEqual(0x12345678u, readings[4]);
        ParityComparer.AssertSame(Runtime("Holder").Parse(bytes, "holder"), ParseHolder(bytes), "holder", strict: true);
    }

    /// <summary>The generated reader's value for the holder, for the parity check.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The value.</returns>
    private static object ParseHolder(byte[] bytes)
    {
        MethodInfo parse = assembly.GetType("Demo.HolderLayout")!.GetMethod("Parse", [typeof(byte[]), typeof(ReadOptions)])!;
        return parse.Invoke(null, [bytes, null])!;
    }

    /// <summary>In an aligned layout the stride of an element is its padded size, and the member after the array is rounded up.</summary>
    [TestMethod]
    public void AlignedStructArray_UsesThePaddedStride()
    {
        // n = 2; pairs start at 4 (aligned to the element's 4), each 8 bytes (a, padding, b); end after.
        byte[] bytes = [2, 0, 0, 0, 1, 0, 0, 0, 0x0A, 0, 0, 0, 2, 0, 0, 0, 0x0B, 0, 0, 0, 0x34, 0x12, 0, 0];
        object[] readings = Probe("Padded", bytes);
        CollectionAssert.AreEqual(new uint[] { 10, 11 }, (uint[])readings[0]);
        Assert.AreEqual((ushort)0x1234, readings[1]);
        StructValue parsed = Runtime("Padded").Parse(bytes, "padded");
        Assert.AreEqual(0x1234, Convert.ToInt32(parsed["end"]));
    }

    /// <summary>An array of unions reads each union's members from the same bytes.</summary>
    [TestMethod]
    public void UnionArray_ReadsEachElementsMembers()
    {
        byte[] bytes = [0x34, 0x12, 0x78, 0x56, 9];
        object[] readings = Probe("Words", bytes);
        Assert.AreEqual((byte)0x34, readings[0]);
        Assert.AreEqual((ushort)0x5678, readings[1]);
        Assert.AreEqual((byte)9, readings[2]);
    }

    /// <summary>An index outside the array is an argument error, and a source that ends inside an element fails with the reader's short-read failure.</summary>
    [TestMethod]
    public void IndexAndSourceBounds_AreChecked()
    {
        byte[] bytes = [9, 1, 0, 0, 2, 0, 0, 3, 0, 0, 1, 2, 3, 4];
        Assert.Throws<ArgumentOutOfRangeException>(() => Probe("OutOfRange", bytes));

        // A runtime-sized array whose count claims more elements than the source holds.
        byte[] shortTable = [3, 0, 0, 0, 5, 0, 0, 0, 1, 0, 0, 0];
        CStructReadException exception = Assert.Throws<CStructReadException>(() => Probe("Table", shortTable));
        StringAssert.Contains(exception.Message, "Not enough bytes");
        StringAssert.Contains(exception.Message, "entries");
    }

    /// <summary>The derived bytes member takes a trailing underscore when a member of the layout has its name.</summary>
    [TestMethod]
    public void BytesMember_AvoidsAClashWithTheLayout()
    {
        byte[] bytes = [7, 8, 9];
        object[] readings = Probe("Clash", bytes);
        Assert.AreEqual((byte)8, readings[0]);
        Assert.AreEqual((byte)9, readings[1], "ItemsBytes is still the layout's own uint8 member");
        Assert.AreEqual(2, readings[2], "the array's bytes take the underscore");
    }
}
