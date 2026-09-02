namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Verifies that only explicitly capable scalar integral codecs can back portable bitfields.</summary>
[TestClass]
public class BitfieldStorageCapabilityTests
{
    /// <summary>Rejects every terminated-string handler even though each has a registered read/write delegate.</summary>
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

    /// <summary>Continues rejecting indirect or collection-shaped storage before an operation can reach a stream.</summary>
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

    /// <summary>Retains the documented rejection of zero-width and unnamed portable bitfields.</summary>
    /// <param name="layout">A complete layout using one unsupported declaration form.</param>
    [TestMethod]
    [DataRow("struct root { uint8 flags:0; };")]
    [DataRow("struct root { uint8 :4; };")]
    public void UnsupportedPortableBitfieldForms_AreRejectedDuringCompilation(string layout)
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout, pointerSize: 1));
    }

    /// <summary>Deliberately accepts every built-in scalar integral spelling, including character and numeric aliases.</summary>
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
    ///     Makes an explicit integer suffix override the opposite layout byte order across parse, debug, address,
    ///     serialization, direct writing, and update.
    /// </summary>
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

    /// <summary>Uses the same explicit integral capability after pointer traversal reaches a bitfield-bearing struct.</summary>
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
