namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Verifies that only explicitly capable scalar integral codecs can back portable bitfields.</summary>
[TestClass]
public class BitfieldStorageCapabilityTests
{
    /// <summary>
    ///     Each data row puts a terminated-string type before flags:1.
    /// </summary>
    /// <remarks>
    ///     A text reader may know how to decode bytes, but it does not provide a fixed integer storage unit for a
    ///     bitfield. Every spelling must fail during CStruct construction, before a stream is opened.
    /// </remarks>
    /// <param name="typeName">The non-integral primitive handler spelling placed before a bitfield.</param>
    [TestMethod]
    [DataRow("ascii_string_zero")]
    [DataRow("ascii_string_newline")]
    [DataRow("utf8_string_zero")]
    [DataRow("utf8_string_newline")]
    [DataRow("unicode_string_zero")]
    [DataRow("unicode_string_zero>")]
    [DataRow("unicode_string_zero<")]
    [DataRow("unicode_string_newline")]
    [DataRow("unicode_string_newline>")]
    [DataRow("unicode_string_newline<")]
    [DataRow("cstring")]
    [DataRow("string")]
    [DataRow("string>")]
    [DataRow("string<")]
    public void NonIntegralPrimitiveCodecBitfields_AreRejectedDuringCompilation(string typeName)
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct($"struct root {{ {typeName} flags:1; }};", pointerSize: 1));
    }

    /// <summary>
    ///     The cases try pointers, arrays, structs, enums, typedefs, and floating-point types as bitfield storage.
    /// </summary>
    /// <remarks>
    ///     These forms are outside the library's supported direct integral bitfields. Each must produce a layout error,
    ///     preventing different operations from inventing inconsistent storage rules.
    /// </remarks>
    /// <param name="layout">A complete layout containing one unsupported bitfield declaration.</param>
    [TestMethod]
    [DataRow("struct root { uint8 *flags:4; };")]
    [DataRow("struct root { uint8 flags[2]:4; };")]
    [DataRow("struct bits { byte value; }; struct root { bits flags:4; };")]
    [DataRow("enum bits : uint8 { none = 0, one = 1 }; struct root { bits flags:4; };")]
    [DataRow("typedef uint8 bits; struct root { bits flags:4; };")]
    [DataRow("struct root { float flags:4; };")]
    [DataRow("struct root { double flags:4; };")]
    public void IndirectOrCollectionBitfieldStorage_IsRejectedDuringCompilation(string layout)
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout, pointerSize: 1));
    }

    /// <summary>
    ///     The two declarations use a zero-width named field and an unnamed four-bit field.
    /// </summary>
    /// <remarks>
    ///     Some C compilers use such declarations to control packing, but this library does not support them.
    ///     Construction must reject both rather than silently treating them as ordinary fields.
    /// </remarks>
    /// <param name="layout">A complete layout using one unsupported declaration form.</param>
    [TestMethod]
    [DataRow("struct root { uint8 flags:0; };")]
    [DataRow("struct root { uint8 :4; };")]
    public void UnsupportedPortableBitfieldForms_AreRejectedDuringCompilation(string layout)
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout, pointerSize: 1));
    }

    /// <summary>
    ///     Every listed built-in integer or character spelling is used for a one-bit flags field.
    /// </summary>
    /// <remarks>
    ///     All must compile, including explicit byte-order variants and built-in numeric aliases. This checks which
    ///     storage types are allowed; it does not read or write a binary value.
    /// </remarks>
    [TestMethod]
    public void IntegralPrimitiveCodecBitfields_AreAcceptedDuringCompilation()
    {
        string[] integralTypeNames =
        [
            "byte",
            "int8",
            "uint8",
            "char",
            "wchar",
            "wchar>",
            "wchar<",
            "int16",
            "int16>",
            "int16<",
            "uint16",
            "uint16>",
            "uint16<",
            "int32",
            "int32>",
            "int32<",
            "uint32",
            "uint32>",
            "uint32<",
            "int64",
            "int64>",
            "int64<",
            "uint64",
            "uint64>",
            "uint64<",
            "short",
            "ushort",
            "int",
            "uint",
            "long",
            "ulong",
        ];

        foreach (string typeName in integralTypeNames)
        {
            _ = new CStruct($"struct root {{ {typeName} flags:1; }};", pointerSize: 1);
        }
    }

    /// <summary>
    ///     The field suffix deliberately requests the opposite byte order from the layout default. low must contain the
    ///     bottom four bits and high the remaining bits of the same integer.
    /// </summary>
    /// <remarks>
    ///     All reading and writing APIs must honor the suffix, and changing low to 3 must preserve every high bit.
    /// </remarks>
    [TestMethod]
    public void ExplicitEndianIntegralBitfields_UseTheirCodecByteOrderAcrossOperations()
    {
        (string BaseName, int Size)[] codecFamilies =
        [
            ("wchar", 2),
            ("int16", 2),
            ("uint16", 2),
            ("int32", 4),
            ("uint32", 4),
            ("int64", 8),
            ("uint64", 8),
        ];

        foreach ((string baseName, int size) in codecFamilies)
        {
            foreach (bool layoutIsLittleEndian in new[] { true, false, })
            {
                string typeName = baseName + (layoutIsLittleEndian ? ">" : "<");
                bool storageIsLittleEndian = !layoutIsLittleEndian;
                int capacity = size * 8;
                ulong rawValue = 0x8123456789ABCDEFUL & GetMask(capacity);
                byte[] bytes = WriteUnsigned(rawValue, size, storageIsLittleEndian);
                var cstruct = new CStruct(
                    $"struct root {{ {typeName} low:4; {typeName} high:{capacity - 4}; }};",
                    pointerSize: 1,
                    isLittleEndian: layoutIsLittleEndian);
                using var stream = new MemoryStream((byte[])bytes.Clone());

                dynamic parsed = cstruct.ParseStream(stream, "root");
                Assert.AreEqual(rawValue & 0xFUL, Convert.ToUInt64(parsed.low), typeName);
                Assert.AreEqual(rawValue >> 4, Convert.ToUInt64(parsed.high), typeName);

                stream.Position = 0;
                (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");
                foreach (string fieldName in new[] { "low", "high", })
                {
                    DebugData item = debug.Single(entry => entry.DebugStackString == "root." + fieldName);
                    Assert.AreEqual(0L, item.CurPos, typeName + "." + fieldName);
                    Assert.AreEqual(size, item.EndPos, typeName + "." + fieldName);
                    stream.Position = 0;
                    Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root." + fieldName));
                }

                CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed), typeName + " serialize");

                using (var writeStream = new MemoryStream())
                {
                    cstruct.WriteStream(writeStream, "root", parsed);
                    CollectionAssert.AreEqual(bytes, writeStream.ToArray(), typeName + " write");
                }

                stream.Position = 0;
                cstruct.UpdateStream(stream, "root.low", 3);
                ulong updatedRawValue = (rawValue & ~0xFUL) | 3UL;
                CollectionAssert.AreEqual(
                    WriteUnsigned(updatedRawValue, size, storageIsLittleEndian),
                    stream.ToArray(),
                    typeName + " update");
                Assert.AreEqual(0L, stream.Position, typeName + " update position");
            }
        }
    }

    /// <summary>
    ///     The pointer leads to AB CD at offset 3. uint16&gt; makes that storage big endian despite the little-endian
    ///     default, so low is 0xD and high is 0xABC.
    /// </summary>
    /// <remarks>
    ///     Updating high to 0x123 must leave bytes 12 3D and preserve both the pointer and the intervening bytes.
    /// </remarks>
    [TestMethod]
    public void PointerTargetBitfields_UseTheValidatedStorageCodec()
    {
        const string layout = """
                              struct child { uint16> low:4; uint16> high:12; };
                              struct root { child *selected; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, isLittleEndian: true);
        using var stream = new MemoryStream([0x03, 0xEE, 0xEE, 0xAB, 0xCD,]);

        dynamic root = cstruct.ParseStream(stream, "root");
        var pointer = (Pointer)root.selected;
        dynamic target = pointer.Value!;
        Assert.AreEqual(0xD, (int)target.low);
        Assert.AreEqual(0xABC, (int)target.high);

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.selected.value");
        Assert.AreEqual(0xD, (int)selected.low);
        Assert.AreEqual(0xABC, (int)selected.high);

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root.selected.value");
        Assert.IsTrue(debug.Any(item => item.CurPos == 3 && item.DebugStackString == "root.selected.low"));
        Assert.IsTrue(debug.Any(item => item.CurPos == 3 && item.DebugStackString == "root.selected.high"));

        stream.Position = 0;
        Assert.AreEqual(3L, cstruct.ResolveAddress(stream, "root.selected.value.high"));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.selected.value.high", 0x123);
        CollectionAssert.AreEqual(new byte[] { 0x03, 0xEE, 0xEE, 0x12, 0x3D, }, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Builds an inclusive low-bit mask for the independent storage oracle.</summary>
    private static ulong GetMask(int width)
    {
        return width == 64 ? ulong.MaxValue : (1UL << width) - 1UL;
    }

    /// <summary>Encodes an unsigned storage value without using a production primitive writer.</summary>
    private static byte[] WriteUnsigned(ulong value, int size, bool isLittleEndian)
    {
        var bytes = new byte[size];
        for (int index = 0; index < size; index++)
        {
            int destination = isLittleEndian ? index : size - index - 1;
            bytes[destination] = (byte)(value >> (index * 8));
        }

        return bytes;
    }
}
