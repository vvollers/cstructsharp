namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Checks arrays of structs: debug paths identify the exact element behind each byte range, records sit at their
///     absolute aligned offsets (root arrays and field alignment overrides included), a large extent does not wrap its
///     span length, and a nested limit keeps the general reader's failure position.
/// </summary>
[TestClass]
public class CompositeArrayTests
{
    /// <summary>Runtime outer counts and fixed inner dimensions retain all coordinates.</summary>
    [TestMethod]
    public void NestedArrays_HaveDistinctPathsAndRanges()
    {
        const string layout = """
                              struct cell { uint16 val; };
                              struct row { cell cells[2][2]; };
                              struct root { uint8 count; row data[count]; };
                              """;
        byte[] bytes = new byte[17];
        bytes[0] = 2;
        var parser = new CStruct(layout, aligned: false);
        using var stream = new MemoryStream(bytes);
        (_, IReadOnlyList<DebugData> debug) = parser.ParseWithDebug(stream, "root");
        for (int i = 0; i < 8; i++)
        {
            string path = $"root.data[{i / 4}].cells[{(i % 4) / 2}][{i % 2}].val";
            DebugData entry = debug.Single(item => item.Path == path);
            Assert.AreEqual(1L + (i * 2), entry.Start);
            Assert.AreEqual(3L + (i * 2), entry.End);
        }
    }

    /// <summary>Lazy paths remain valid after later reads and concurrent formatting.</summary>
    [TestMethod]
    public void RetainedDebugPaths_AreIndependentOfLaterOperations()
    {
        var parser = new CStruct("struct cell { uint8 value; }; struct root { cell cells[2][2]; };");
        (_, IReadOnlyList<DebugData> retained) = parser.ParseWithDebug(new MemoryStream(new byte[4]), "root");
        Parallel.For(0, 32, _ =>
        {
            parser.ParseWithDebug(new MemoryStream(new byte[4]), "root");
            string[] paths = retained.Select(item => item.Path).ToArray();
            CollectionAssert.AreEqual(
                new[] { "root.cells[0][0].value", "root.cells[0][1].value", "root.cells[1][0].value", "root.cells[1][1].value" },
                paths);
        });
    }

    /// <summary>Array indices survive pointer dereferencing, including address-storage records.</summary>
    [TestMethod]
    public void StructPointerArray_IndexesTargetsAndAddresses()
    {
        const string layout = """
                              struct cell { uint16 val; };
                              struct root { cell *data[2]; };
                              """;
        var parser = new CStruct(layout, pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(new byte[] { 2, 4, 11, 0, 22, 0 });
        (_, IReadOnlyList<DebugData> debug) = parser.ParseWithDebug(stream, "root");
        Assert.AreEqual(2L, debug.Single(item => item.Path == "root.data[0].val").Start);
        Assert.AreEqual(4L, debug.Single(item => item.Path == "root.data[1].val").Start);
        Assert.AreEqual(0L, debug.Single(item => item.Path == "root.data[0]").Start);
        Assert.AreEqual(1L, debug.Single(item => item.Path == "root.data[1]").Start);
    }

    /// <summary>A four-element array with a four-gibibyte extent falls back to the normal short-input diagnostic.</summary>
    [TestMethod]
    public void LargeCompositeArrayExtent_DoesNotWrapItsSpanLength()
    {
        var codec = new LargeFixedCodec();
        var layout = new CStruct("struct element { huge_block _; }; struct root { uint8 count; element values[count]; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        byte[] input = [4,];

        // Only the count byte and four list slots exist; the codec reports truncated borrowed input without renting storage.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.Parse(input.AsSpan(), "root"));
        StringAssert.Contains(failure.Message, "huge_block");
        Assert.AreEqual(1, codec.ReadCalls);
    }

    /// <summary>
    ///     A smaller field alignment moves where an array of structs starts, not how each element is laid out: an
    ///     element at an odd offset keeps its four-byte layout, on the fast and the general path.
    /// </summary>
    [TestMethod]
    public void FieldAlignmentOverride_KeepsTheElementLayout()
    {
        var layout = new CStruct("struct item { uint8 first; uint16 second; }; struct root { uint8 count; uint8 prefix[count]; item values[2] @align(1); uint8 tail; };", aligned: true, isLittleEndian: true);
        byte[] bytes = [0, 0xA1, 0xEE, 0xB2, 0xC3, 0xD4, 0xEE, 0x16, 0x27, 99, 0,];
        foreach (ExecutionPath path in (ExecutionPath[])[ExecutionPath.Fastest, ExecutionPath.GeneralOnly])
        {
            dynamic parsed = layout.Parse(bytes.AsSpan(), "root", null, new ReadOptions { ExecutionPath = path, });
            var values = ((IEnumerable<object?>)parsed.values).Cast<StructValue>().ToArray();
            Assert.AreEqual((byte)0xA1, values[0]["first"]);
            Assert.AreEqual((ushort)0xC3B2, values[0]["second"]);
            Assert.AreEqual((byte)0xD4, values[1]["first"]);
            Assert.AreEqual((ushort)0x2716, values[1]["second"]);
            Assert.AreEqual((byte)99, (byte)parsed.tail);
        }
    }

    /// <summary>An inner array limit fails after the preceding scalar, without consuming the remaining record block.</summary>
    [TestMethod]
    public void NestedArrayLimit_PreservesTheGeneralReadPosition()
    {
        var layout = new CStruct("struct item { uint8 prefix; uint8 values[2]; }; struct root { uint8 count; item items[count]; };");
        byte[] bytes = [1, 0x42, 0xAA, 0xBB,];

        // Expose the buffer so the reader can attempt its whole-array span optimization.
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
        CStructReadLimitException error = Assert.Throws<CStructReadLimitException>(() =>
            layout.Parse(source, "root", options: new ReadOptions { MaxArrayElements = 1, }));
        Assert.AreEqual(2L, source.Position);
        Assert.AreEqual(2L, error.Offset);
    }

    /// <summary>An aligned root array starts at an odd input position, and each record keeps its own layout.</summary>
    [TestMethod]
    public void RootArray_StartsAtAnOddPosition()
    {
        var layout = new CStruct("struct item { uint8 first; uint16 second; }; typedef item pair[2];", aligned: true);
        using var source = new MemoryStream(new byte[] { 0xEE, 0xA1, 0, 0xB2, 0xC3, 0xD4, 0, 0xE5, 0xF6, });
        source.Position = 1;
        StructValue[] values = layout.ReadValue<StructValue[]>(source, "pair");
        Assert.HasCount(2, values);
        Assert.AreEqual((byte)0xA1, values[0]["first"]);
        Assert.AreEqual((ushort)0xC3B2, values[0]["second"]);
        Assert.AreEqual((byte)0xD4, values[1]["first"]);
        Assert.AreEqual((ushort)0xF6E5, values[1]["second"]);
        Assert.AreEqual(9L, source.Position);
    }

    /// <summary>Declares a large fixed storage extent while handling truncated spans without allocation.</summary>
    private sealed class LargeFixedCodec : ICustomCodec
    {
        public string Name => "huge_block";

        public int? FixedSize => 1 << 30;

        public int Alignment => 1;

        /// <summary>Gets the number of attempts to decode the borrowed input.</summary>
        public int ReadCalls { get; private set; }

        /// <summary>Consumes a complete fixed block or reports that the supplied input is too short.</summary>
        /// <param name="source">The borrowed input bytes.</param>
        /// <param name="value">A zero marker on success, otherwise null.</param>
        /// <param name="bytesRead">The declared extent on success, otherwise zero.</param>
        /// <returns>Done only when the whole fixed block is present.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesRead)
        {
            this.ReadCalls++;
            bool complete = source.Length >= this.FixedSize!.Value;
            value = complete ? 0 : null;
            bytesRead = complete ? this.FixedSize.Value : 0;
            return complete ? OperationStatus.Done : OperationStatus.NeedMoreData;
        }

        /// <summary>Encodes the fixed zero block only when the destination has room for its complete extent.</summary>
        /// <param name="destination">The output window.</param>
        /// <param name="value">Unused marker value.</param>
        /// <param name="bytesWritten">The complete extent on success, otherwise zero.</param>
        /// <returns>Done or DestinationTooSmall according to the available storage.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < this.FixedSize!.Value)
            {
                return OperationStatus.DestinationTooSmall;
            }

            destination[..this.FixedSize.Value].Clear();
            bytesWritten = this.FixedSize.Value;
            return OperationStatus.Done;
        }
    }
}
