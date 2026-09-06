namespace CStructSharp.Tests;

using System.Buffers;
using System.Dynamic;

/// <summary>Defines the zero-copy synchronous memory-input and caller-owned output contract.</summary>
[TestClass]
public class MemoryIoTests
{
    /// <summary>Matches the exact test-layout enum payload.</summary>
    public enum MemoryStatus : ushort
    {
        /// <summary>The declared ready state.</summary>
        Ready = 0x1234,
    }

    /// <summary>
    ///     The record combines a count-controlled child array, terminated text, and a uint16 enum.
    /// </summary>
    /// <remarks>
    ///     Byte arrays, spans, and memory must decode the same values as stream APIs, including OK and the selected
    ///     0x0304. Typed reads map the result into C# models; incompatible TryReadValue conversions return false.
    /// </remarks>
    [TestMethod]
    public void MemoryInput_ParsesNaturalAndTypedValuesWithoutStreamConstruction()
    {
        const string definition = """
                                  enum status : uint16 { ready = 0x1234 };
                                  struct child { uint16> value; };
                                  struct root
                                  {
                                      byte count;
                                      child items[count];
                                      cstring name;
                                      status state;
                                  };
                                  """;
        byte[] bytes = [2, 0x01, 0x02, 0x03, 0x04, (byte)'O', (byte)'K', 0, 0x12, 0x34,];
        var cstruct = new CStruct(definition, pointerSize: 1, isLittleEndian: false);

        ExpandoObject parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual((byte)2, (byte)((dynamic)parsed).count);
        Assert.AreEqual("OK", (string)((dynamic)parsed).name);

        ReadOnlySpan<byte> span = bytes;
        Assert.AreEqual((ushort)0x0304, cstruct.ReadValue<ushort>(span, "root.items[1].value"));

        ReadOnlyMemory<byte> memory = bytes;
        MemoryRoot typed = cstruct.ReadValue<MemoryRoot>(memory, "root");
        Assert.AreEqual(2, typed.Items.Count);
        Assert.AreEqual((ushort)0x0102, typed.Items[0].Value);
        Assert.AreEqual(MemoryStatus.Ready, typed.State);
        Assert.AreEqual("OK", typed.Name);

        Assert.IsFalse(cstruct.TryReadValue<DateTime>(memory, out _, "root.items[0].value"));
        Assert.IsTrue(cstruct.TryReadValue(memory, out ushort selected, "root.items[0].value"));
        Assert.AreEqual((ushort)0x0102, selected);
    }

    /// <summary>
    ///     The input is a slice of a larger array.
    /// </summary>
    /// <remarks>
    ///     Pointer offset 3 is measured from the start of that slice and must reach 0x7E, not a position in the
    ///     original array. Out-of-region targets fail when followed, while address-only reads can retain an unresolved
    ///     address.
    /// </remarks>
    [TestMethod]
    public void MemoryInput_PointersStayInsideTheSuppliedRegion()
    {
        var cstruct = new CStruct("struct root { byte *target; byte marker; };", pointerSize: 1);
        byte[] storage = [0xEE, 0xEE, 3, 0xA5, 0, 0x7E, 0xDD,];
        ReadOnlyMemory<byte> region = storage.AsMemory(2, 4);

        Assert.AreEqual((byte)0x7E, cstruct.ReadValue<byte>(region, "root.target.value"));
        byte[] relativeBytes = [2, 0xA5, 0, 0x7E,];
        Assert.AreEqual(
            (byte)0x7E,
            cstruct.ReadValue<byte>(
                (ReadOnlyMemory<byte>)relativeBytes,
                "root.target.value",
                options: new ReadOptions
                {
                    AddressingMode = PointerAddressingMode.Relative,
                    Origin = 1,
                }));

        byte[] outside = [9, 0xA5, 0, 0x7E,];
        Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue<byte>((ReadOnlyMemory<byte>)outside, "root.target.value"));

        Pointer unresolved = cstruct.ReadValue<Pointer>(
            (ReadOnlyMemory<byte>)outside,
            "root.target",
            options: new ReadOptions { DereferencePointers = false, });
        Assert.AreEqual(9L, unresolved.Address);
        Assert.IsFalse(unresolved.IsDereferenced);
    }

    /// <summary>
    ///     The four bytes encode uint32 value 0x12345678.
    /// </summary>
    /// <remarks>
    ///     Memory-based reads must recover it with either the default root or an explicit selection. Truncation,
    ///     insufficient byte budget, and invalid options must use the same failure rules as the stream APIs.
    /// </remarks>
    [TestMethod]
    public void MemoryInput_UsesTheSharedFailureAndBudgetPolicy()
    {
        var cstruct = new CStruct("struct root { uint32 value; };", pointerSize: 1);
        byte[] complete = [0x78, 0x56, 0x34, 0x12,];

        ExpandoObject parsed = cstruct.Parse((ReadOnlySpan<byte>)complete);
        Assert.AreEqual(0x12345678U, (uint)((dynamic)parsed).value);
        Assert.IsInstanceOfType<ExpandoObject>(cstruct.ReadValue((ReadOnlyMemory<byte>)complete));
        Assert.AreEqual(
            0x12345678U,
            cstruct.ReadValue<uint>((ReadOnlyMemory<byte>)complete, "root.value"));

        Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue<uint>((ReadOnlyMemory<byte>)complete[..3], "root.value"));
        Assert.IsFalse(
            cstruct.TryReadValue(
                (ReadOnlyMemory<byte>)complete,
                out uint _,
                "root.value",
                options: new ReadOptions { MaxTotalBytesRead = 3, }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cstruct.ReadValue<uint>(
                (ReadOnlyMemory<byte>)complete,
                "root.value",
                options: new ReadOptions { MaxTotalBytesRead = -1, }));
    }

    /// <summary>
    ///     A layout containing bitfields, explicit-endian integers, text, and a union is written into caller-owned
    ///     spans.
    /// </summary>
    /// <remarks>
    ///     An exact or oversized span must match ordinary serialization and report only the initialized prefix. Unused
    ///     suffix bytes must remain unchanged, and insufficient capacity must produce a write error.
    /// </remarks>
    [TestMethod]
    public void SpanSerialization_MatchesOwnedSerializationAndEnforcesCapacity()
    {
        const string definition = """
                                  union choice { uint16> wide; byte narrow; };
                                  struct root
                                  {
                                      byte low : 3;
                                      byte high : 5;
                                      uint16> value;
                                      char text[3];
                                      choice selected;
                                  };
                                  """;
        var cstruct = new CStruct(definition, pointerSize: 1, isLittleEndian: true, aligned: true);
        var value = new
        {
            low = 5,
            high = 17,
            value = 0x1234,
            text = "OK",
            selected = UnionValue.FromMember("choice", "wide", 0xABCD),
        };
        byte[] expected = cstruct.Serialize("root", value);

        Span<byte> exact = stackalloc byte[expected.Length];
        int exactCount = cstruct.Serialize(exact, "root", value);
        Assert.AreEqual(expected.Length, exactCount);
        CollectionAssert.AreEqual(expected, exact.ToArray());

        byte[] oversized = Enumerable.Repeat((byte)0xA5, expected.Length + 4).ToArray();
        int oversizedCount = cstruct.Serialize(oversized.AsSpan(), "root", value);
        Assert.AreEqual(expected.Length, oversizedCount);
        CollectionAssert.AreEqual(expected, oversized[..expected.Length]);
        CollectionAssert.AreEqual(
            Enumerable.Repeat((byte)0xA5, 4).ToArray(),
            oversized[expected.Length..]);

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(new byte[expected.Length - 1].AsSpan(), "root", value));
    }

    /// <summary>
    ///     The destination has room for first but not second.
    /// </summary>
    /// <remarks>
    ///     Serialization must throw when capacity runs out, yet its first byte has already changed from 0xA5 to 1.
    ///     Direct span output does not promise rollback, so callers must not assume failure leaves their buffer
    ///     untouched.
    /// </remarks>
    [TestMethod]
    public void SpanSerialization_CapacityFailureDoesNotPromiseRollback()
    {
        var cstruct = new CStruct("struct root { byte first; byte second; };", pointerSize: 1);
        byte[] destination = [0xA5,];

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(destination.AsSpan(), "root", new { first = 1, second = 2, }));
        CollectionAssert.AreEqual(new byte[] { 1, }, destination);
    }

    /// <summary>
    ///     A 5000-byte array is followed by two bitfields sharing a byte and a big-endian tail.
    /// </summary>
    /// <remarks>
    ///     Writing through IBufferWriter must match ordinary serialization across internal chunk boundaries. The
    ///     runtime count and the bitfield rewrite must still work without allocating one complete temporary output
    ///     array.
    /// </remarks>
    [TestMethod]
    public void BufferWriterSerialization_MatchesSharedWriterAcrossChunksAndVariables()
    {
        const int count = 5000;
        var cstruct = new CStruct(
            "struct root { byte prefix[count]; byte low : 4; byte high : 4; uint16> tail; };",
            pointerSize: 1);
        var variables = new Dictionary<string, int> { ["count"] = count, };
        var value = new
        {
            prefix = Enumerable.Range(0, count).Select(index => (byte)index).ToArray(),
            low = 0xA,
            high = 0xB,
            tail = 0x1234,
        };
        byte[] expected = cstruct.Serialize("root", value, variables);
        var writer = new ArrayBufferWriter<byte>(128);

        long written = cstruct.Serialize(writer, "root", value, variables);

        Assert.AreEqual(expected.Length, written);
        CollectionAssert.AreEqual(expected, writer.WrittenSpan.ToArray());
    }

    /// <summary>
    ///     The aligned uint64 after 4095 bytes crosses an output-window boundary.
    /// </summary>
    /// <remarks>
    ///     Appending this record must preserve the writer's existing two-byte prefix and use coordinates relative to
    ///     the new record. A separate bitfield case verifies unused new bits start at zero rather than inheriting old
    ///     destination contents.
    /// </remarks>
    [TestMethod]
    public void MemorySerialization_UsesRegionRelativeAppendAndZeroInitialization()
    {
        const int count = 4095;
        var aligned = new CStruct(
            "struct root { byte prefix[count]; uint64 value; };",
            pointerSize: 1,
            aligned: true);
        var variables = new Dictionary<string, int> { ["count"] = count, };
        var value = new
        {
            prefix = new byte[count],
            value = 0x0102030405060708UL,
        };
        byte[] expected = aligned.Serialize("root", value, variables);
        var writer = new ArrayBufferWriter<byte>(32);
        writer.Write(new byte[] { 0xEE, 0xDD, });

        long written = aligned.Serialize(writer, "root", value, variables);

        Assert.AreEqual(expected.Length, written);
        CollectionAssert.AreEqual(new byte[] { 0xEE, 0xDD, }, writer.WrittenSpan[..2].ToArray());
        CollectionAssert.AreEqual(expected, writer.WrittenSpan[2..].ToArray());

        var bitfield = new CStruct("struct root { byte low : 3; };", pointerSize: 1);
        byte[] destination = [0xFF, 0xA5,];
        int bitfieldBytes = bitfield.Serialize(destination.AsSpan(), "root", new { low = 5, });
        Assert.AreEqual(1, bitfieldBytes);
        Assert.AreEqual((byte)5, destination[0]);
        Assert.AreEqual((byte)0xA5, destination[1]);
    }

    /// <summary>Receives a complete typed root from the memory reader.</summary>
    public sealed class MemoryRoot
    {
        /// <summary>Gets or sets the runtime child count.</summary>
        public byte Count { get; set; }

        /// <summary>Gets or sets the converted child list.</summary>
        public List<MemoryChild> Items { get; set; } = [];

        /// <summary>Gets or sets the terminated string.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the exact enum projection.</summary>
        public MemoryStatus State { get; set; }
    }

    /// <summary>Receives one typed nested child.</summary>
    public sealed class MemoryChild
    {
        /// <summary>Gets or sets the big-endian child value.</summary>
        public ushort Value { get; set; }
    }
}
