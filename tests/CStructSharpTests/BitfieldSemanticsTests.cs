namespace CStructSharp.Tests;

using System.Dynamic;
using System.Globalization;

/// <summary>Verifies the portable unsigned-value contract for bitfield slices and in-place updates.</summary>
[TestClass]
public class BitfieldSemanticsTests
{
    /// <summary>
    ///     A uint16 storage unit holds three fields of 3, 5, and 8 bits.
    /// </summary>
    /// <remarks>
    ///     The number 0xA5D5 must split into 5, 26, and 165, regardless of byte order. All three fields share a byte
    ///     address and debug range. Updating one slice must preserve the other bits, the prefix, and the tail, with or
    ///     without alignment padding.
    /// </remarks>
    /// <param name="aligned">Whether fields use their portable alignment boundaries.</param>
    /// <param name="isLittleEndian">Whether the least-significant storage byte is written first.</param>
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(true, false)]
    public void BitfieldOperations_FirstMiddleAndLastSlices_Agree(bool aligned, bool isLittleEndian)
    {
        const string layout = """
                              struct root {
                                  uint8 prefix;
                                  uint16 first:3;
                                  uint16 center:5;
                                  uint16 last:8;
                                  uint8 tail;
                              };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: aligned, isLittleEndian: isLittleEndian);
        int unitStart = aligned ? 2 : 1;
        int tailOffset = unitStart + 2;
        byte[] bytes = new byte[aligned ? 6 : 4];
        bytes[0] = 0xEE;
        WriteUnsigned(bytes, unitStart, 2, 0xA5D5, isLittleEndian);
        bytes[tailOffset] = 0x7E;
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        Assert.AreEqual(0x05, (int)parsed.first);
        Assert.AreEqual(0x1A, (int)parsed.center);
        Assert.AreEqual(0xA5, (int)parsed.last);
        Assert.AreEqual((byte)0x7E, (byte)parsed.tail);

        foreach (string name in new[] { "first", "center", "last", })
        {
            stream.Position = 0;
            Assert.AreEqual(unitStart, cstruct.ResolveAddress(stream, "root." + name), name);
        }

        stream.Position = 0;
        (List<DebugData> debug, dynamic debugResult) = cstruct.ParseStreamWithDebug(stream, "root");
        dynamic debugParsed = ((IDictionary<string, object?>)debugResult)["root"]!;
        Assert.AreEqual(0x1A, (int)debugParsed.center);
        foreach (string name in new[] { "first", "center", "last", })
        {
            DebugData item = debug.Single(entry => entry.DebugStackString == "root." + name);
            Assert.AreEqual(unitStart, item.CurPos, name);
            Assert.AreEqual(unitStart + 2, item.EndPos, name);
        }

        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));

        var updates = new[]
        {
            (Name: "first", Offset: 0, Width: 3, Value: 0x2UL),
            (Name: "center", Offset: 3, Width: 5, Value: 0x0AUL),
            (Name: "last", Offset: 8, Width: 8, Value: 0x3CUL),
        };
        foreach ((string name, int offset, int width, ulong value) in updates)
        {
            byte[] updatedBytes = (byte[])bytes.Clone();
            using var updateStream = new MemoryStream(updatedBytes);
            cstruct.UpdateStream(updateStream, "root." + name, value);

            ulong mask = GetMask(width) << offset;
            ulong expectedStorage = (0xA5D5UL & ~mask) | (value << offset);
            byte[] expectedBytes = (byte[])bytes.Clone();
            WriteUnsigned(expectedBytes, unitStart, 2, expectedStorage, isLittleEndian);
            CollectionAssert.AreEqual(expectedBytes, updateStream.ToArray(), name);
            Assert.AreEqual(0, updateStream.Position, name);
        }
    }

    /// <summary>
    ///     a and b share the byte 0xBA and read 10 and 11.
    /// </summary>
    /// <remarks>
    ///     Changing the base type to uint16 starts a new unit, where c and d read 12 and 13. Updating d to 5 must make
    ///     that unit 0x005C. Every operation must agree about the new unit's position and any alignment padding.
    /// </remarks>
    /// <param name="aligned">Whether the wider second unit is aligned to two bytes.</param>
    /// <param name="isLittleEndian">Whether the least-significant storage byte is written first.</param>
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(true, false)]
    public void BitfieldOperations_MixedStorageUnits_Agree(bool aligned, bool isLittleEndian)
    {
        const string layout = """
                              struct root {
                                  uint8 a:4;
                                  uint8 b:4;
                                  uint16 c:4;
                                  uint16 d:4;
                                  uint8 tail;
                              };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: aligned, isLittleEndian: isLittleEndian);
        int wideUnitStart = aligned ? 2 : 1;
        int tailOffset = wideUnitStart + 2;
        byte[] bytes = new byte[aligned ? 6 : 4];
        bytes[0] = 0xBA;
        WriteUnsigned(bytes, wideUnitStart, 2, 0x00DC, isLittleEndian);
        bytes[tailOffset] = 0x7E;
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        Assert.AreEqual(0xA, (int)parsed.a);
        Assert.AreEqual(0xB, (int)parsed.b);
        Assert.AreEqual(0xC, (int)parsed.c);
        Assert.AreEqual(0xD, (int)parsed.d);
        Assert.AreEqual(bytes.Length, cstruct.GetStructSizeInBytes("root"));

        stream.Position = 0;
        Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root.b"));
        stream.Position = 0;
        Assert.AreEqual(wideUnitStart, cstruct.ResolveAddress(stream, "root.d"));
        stream.Position = 0;
        Assert.AreEqual(tailOffset, cstruct.ResolveAddress(stream, "root.tail"));

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");
        DebugData narrow = debug.Single(entry => entry.DebugStackString == "root.b");
        DebugData wide = debug.Single(entry => entry.DebugStackString == "root.d");
        Assert.AreEqual(0L, narrow.CurPos);
        Assert.AreEqual(1L, narrow.EndPos);
        Assert.AreEqual(wideUnitStart, wide.CurPos);
        Assert.AreEqual(wideUnitStart + 2, wide.EndPos);
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.d", 0x5);
        byte[] expected = (byte[])bytes.Clone();
        WriteUnsigned(expected, wideUnitStart, 2, 0x005C, isLittleEndian);
        CollectionAssert.AreEqual(expected, stream.ToArray());
    }

    /// <summary>
    ///     The signed type names int8 through int64 describe storage here, while the bitfields expose unsigned slices
    ///     of that storage.
    /// </summary>
    /// <remarks>
    ///     Each test value has its top bit set and must remain positive and exact. Parsing, serialization, and updating
    ///     must preserve those bits in both byte orders rather than sign-extending or rejecting them.
    /// </remarks>
    [TestMethod]
    public void SignedBackingStorage_RoundTripsUnsignedSlicesAtEveryWidth()
    {
        var cases = new[]
        {
            (Type: "int8", Size: 1, Value: 0xA5UL),
            (Type: "int16", Size: 2, Value: 0x80A5UL),
            (Type: "int32", Size: 4, Value: 0x800000A5UL),
            (Type: "int64", Size: 8, Value: 0x80000000000000A5UL),
        };

        foreach (bool isLittleEndian in new[] { true, false, })
        {
            foreach ((string type, int size, ulong value) in cases)
            {
                int width = size * 8;
                var cstruct = new CStruct(
                    $"struct root {{ {type} flags:{width}; }};",
                    pointerSize: 1,
                    isLittleEndian: isLittleEndian);
                byte[] expected = new byte[size];
                WriteUnsigned(expected, 0, size, value, isLittleEndian);
                using var input = new MemoryStream(expected);

                dynamic parsed = cstruct.ParseStream(input, "root");
                Assert.AreEqual(value, Convert.ToUInt64(parsed.flags), $"{type}, little={isLittleEndian}");

                var data = new Dictionary<string, object> { ["flags"] = value, };
                CollectionAssert.AreEqual(expected, cstruct.Serialize("root", data), type);

                using var update = new MemoryStream(new byte[size]);
                cstruct.UpdateStream(update, "root.flags", value);
                CollectionAssert.AreEqual(expected, update.ToArray(), type);
            }
        }
    }

    /// <summary>
    ///     A field of n bits can hold unsigned values from zero through 2^n - 1.
    /// </summary>
    /// <remarks>
    ///     The test writes that maximum for several widths, including 64 bits, then tries the next larger value. The
    ///     maximum must succeed; overflow must leave both the original bytes and the stream position unchanged.
    /// </remarks>
    [TestMethod]
    public void BitfieldWriteRange_UsesExactInclusiveBoundaries()
    {
        foreach (int width in new[] { 1, 4, 31, 32, 63, 64, })
        {
            var cstruct = new CStruct($"struct root {{ uint64 flags:{width}; }};", pointerSize: 1);
            ulong maximum = GetMask(width);
            using var valid = new MemoryStream(new byte[8]);

            cstruct.UpdateStream(valid, "root.flags", maximum);

            byte[] expected = new byte[8];
            WriteUnsigned(expected, 0, 8, maximum, true);
            CollectionAssert.AreEqual(expected, valid.ToArray(), "maximum width " + width);

            object overflow = width == 64
                                  ? decimal.Parse("18446744073709551616", CultureInfo.InvariantCulture)
                                  : maximum + 1;
            byte[] original = Enumerable.Repeat((byte)0xA5, 8).ToArray();
            using var invalid = new MemoryStream((byte[])original.Clone());
            Assert.Throws<CStructWriteException>(
                () => cstruct.UpdateStream(invalid, "root.flags", overflow),
                "overflow width " + width);
            CollectionAssert.AreEqual(original, invalid.ToArray(), "overflow width " + width);
            Assert.AreEqual(0, invalid.Position, "overflow width " + width);
        }
    }

    /// <summary>
    ///     flags occupies four bits of an existing byte.
    /// </summary>
    /// <remarks>
    ///     Booleans, fractional numbers, and nonnumeric text must cause a write error instead of being rounded or
    ///     converted unexpectedly. The original 0xA5 byte and position zero must survive each failure, protecting the
    ///     other bits in the same storage.
    /// </remarks>
    [TestMethod]
    public void BitfieldWriteRange_RejectsValuesOutsideTheUnsignedIntegerDomain()
    {
        var cstruct = new CStruct("struct root { uint8 flags:4; };", pointerSize: 1);
        foreach (object invalid in new object[] { true, 1.5F, 1.5D, 1.5M, "not-a-number", })
        {
            using var stream = new MemoryStream(new byte[] { 0xA5, });

            Assert.Throws<CStructWriteException>(() => cstruct.UpdateStream(stream, "root.flags", invalid));

            CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray(), invalid.ToString());
            Assert.AreEqual(0, stream.Position, invalid.ToString());
        }
    }

    /// <summary>
    ///     The union's low field and all field describe overlapping bits of 0xBCA5.
    /// </summary>
    /// <remarks>
    ///     Reading low gives 5; updating it to 3 must produce 0xBCA3. Only those four low bits may change, even though
    ///     the union also exposes the complete 16-bit value and is surrounded by other fields.
    /// </remarks>
    [TestMethod]
    public void UnionBitfieldUpdate_PreservesOverlappingStorage()
    {
        const string layout = """
                              union flags { uint16 low:4; uint16 all:16; };
                              struct root { uint8 prefix; flags value; uint8 tail; };
                              """;

        foreach (bool isLittleEndian in new[] { true, false, })
        {
            var cstruct = new CStruct(layout, pointerSize: 1, isLittleEndian: isLittleEndian);
            byte[] bytes = new byte[4];
            bytes[0] = 0xEE;
            WriteUnsigned(bytes, 1, 2, 0xBCA5, isLittleEndian);
            bytes[3] = 0x7E;
            using var stream = new MemoryStream(bytes);

            dynamic parsed = cstruct.ParseStream(stream, "root");
            Assert.AreEqual(0x5, (int)parsed.value.low);
            Assert.AreEqual(0xBCA5UL, Convert.ToUInt64(parsed.value.all));
            stream.Position = 0;
            Assert.AreEqual(1L, cstruct.ResolveAddress(stream, "root.value.low"));

            stream.Position = 0;
            cstruct.UpdateStream(stream, "root.value.low", 0x3);

            byte[] expected = (byte[])bytes.Clone();
            WriteUnsigned(expected, 1, 2, 0xBCA3, isLittleEndian);
            CollectionAssert.AreEqual(expected, stream.ToArray());
        }
    }

    /// <summary>
    ///     flags[2]:4 combines an array with a bitfield.
    /// </summary>
    /// <remarks>
    ///     The declaration parser can recognize this form, but the executable layout does not support it. Constructing
    ///     CStruct must raise a layout error before any data is read, rather than guessing how array elements should
    ///     share bits.
    /// </remarks>
    [TestMethod]
    public void BitfieldArrays_AreRejectedDuringCompilation()
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint16 flags[2]:4; };", pointerSize: 1));
    }

    /// <summary>
    ///     low and high each use four bits in 0xA5.
    /// </summary>
    /// <remarks>
    ///     Writing 0x10 to high is invalid because four bits can hold at most 0xF. The operation must report a write
    ///     error and preserve the entire byte, including the unrelated low field.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_BitfieldOverflow_LeavesStorageUntouched()
    {
        var cstruct = new CStruct("struct root { uint8 low:4; uint8 high:4; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0xA5, });

        Assert.Throws<CStructWriteException>(() => cstruct.UpdateStream(stream, "root.high", (byte)0x10));

        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>
    ///     Although the storage type is int8, the library treats each bit slice as unsigned.
    /// </summary>
    /// <remarks>
    ///     Writing -1 into high must therefore fail. The test checks that rejection happens without replacing the
    ///     shared 0xA5 byte or changing the caller's stream position.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_NegativeBitfieldValue_LeavesStorageUntouched()
    {
        var cstruct = new CStruct("struct root { int8 low:4; int8 high:4; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0xA5, });

        Assert.Throws<CStructWriteException>(() => cstruct.UpdateStream(stream, "root.high", -1));

        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>
    ///     The declaration uses every bit of an int8 storage unit.
    /// </summary>
    /// <remarks>
    ///     Reading 0xFF must return 255 for flags, not -1. This makes the library's unsigned bitfield rule explicit; it
    ///     should not be confused with the signedness rules of a particular C compiler.
    /// </remarks>
    [TestMethod]
    public void ParseStream_SignedBitfieldBacking_UsesUnsignedSlice()
    {
        var cstruct = new CStruct("struct root { int8 flags:8; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0xFF, });

        dynamic result = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(255, (int)result.flags);
    }

    /// <summary>Builds a low-bit mask for expected-value calculations in the independent test oracle.</summary>
    private static ulong GetMask(int width)
    {
        return width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
    }

    /// <summary>Writes an integer into a test buffer without using the production codec implementation.</summary>
    private static void WriteUnsigned(byte[] target, int offset, int size, ulong value, bool isLittleEndian)
    {
        for (int index = 0; index < size; index++)
        {
            int destination = isLittleEndian ? offset + index : offset + size - index - 1;
            target[destination] = (byte)(value >> (index * 8));
        }
    }
}
