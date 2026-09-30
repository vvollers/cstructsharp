namespace CStructSharp.Tests;

using System.Globalization;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Values;

/// <summary>
///     Checks that a memory session's paths use the layout path grammar relative to the selected type, that a schema
///     rejects member names no path can spell, and that a scalar or bit slice decoded straight from its bytes equals a
///     read of the schema's one-member layout.
/// </summary>
[TestClass]
public class MemoryPathAndCodecTests
{
    /// <summary>
    ///     Over <c>struct s { uint8 items[3]; }</c> holding <c>01 02 03</c>: an empty path is the value itself, a path may
    ///     start with an index of an array type, surrounding white space is ignored and <c>[01]</c> is element 1, while
    ///     the forms the layout grammar rejects fail with its messages.
    /// </summary>
    [TestMethod]
    public void Paths_UseTheLayoutGrammarRelativeToTheType()
    {
        MemoryTypeDefinition u8 = new("u", "uint8", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        MemoryTypeDefinition array = new("arr", "arr", MemoryTypeKind.Array, 3, elementTypeId: "u", count: 3);
        MemoryTypeDefinition record = new("s", "s", MemoryTypeKind.Struct, 3, [new("items", "arr", 0),]);
        var session = new MemorySession(new MemorySchema([u8, array, record,]));
        var region = new MemoryRegion(new ByteArrayMemorySource("m", [1, 2, 3,]), 0, 3);

        Assert.AreEqual((byte)1, session.Read(region, "u", string.Empty));
        Assert.AreEqual((byte)2, session.Read(region, "arr", "[1]"));
        Assert.AreEqual((byte)3, session.Read(region, "s", "items[2]"));
        Assert.AreEqual((byte)2, session.Read(region, "s", " items[01] "));

        string[] malformed = ["items.[1]", "items[1\0]", "items[-1]", "bad-name", "a b", "items..x",];
        foreach (string path in malformed)
        {
            Assert.Throws<CStructPathException>(() => session.Read(region, "s", path), path);
        }

        CStructPathException trailing = Assert.Throws<CStructPathException>(() => session.Read(region, "s", "items."));
        StringAssert.Contains(trailing.Message, "Path contains an empty segment");
        CStructPathException element = Assert.Throws<CStructPathException>(() => session.Read(region, "s", "[0]"));
        StringAssert.Contains(element.Message, "Cannot traverse '[0]' through 's'");
    }

    /// <summary>A member name that is not an identifier fails schema loading; identifiers, including non-ASCII letters, load.</summary>
    [TestMethod]
    public void Schema_RejectsMemberNamesNoPathCanSpell()
    {
        MemoryTypeDefinition u8 = new("u", "uint8", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        foreach (string name in new[] { "bad-name", "a b", "1st", "x]", })
        {
            CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new MemorySchema([u8, new("s", "s", MemoryTypeKind.Struct, 1, [new(name, "u", 0),]),]), name);
            StringAssert.Contains(failure.Message, "is not an identifier");
        }

        foreach (string name in new[] { "_ok1", "Élément", "value", })
        {
            _ = new MemorySchema([u8, new("s", "s", MemoryTypeKind.Struct, 1, [new(name, "u", 0),]),]);
        }
    }

    /// <summary>
    ///     Every whole scalar spelling decodes directly to what a read of its layout returns - numbers, booleans, floats,
    ///     a big-endian spelling and an enum - and the spellings the engine reads another way (<c>float16</c>,
    ///     <c>int128</c>) read through the layout, over several byte patterns.
    /// </summary>
    [TestMethod]
    public void Values_DecodeAsTheirLayoutReadsThem()
    {
        const string Enum = "enum kind : uint16 { a = 1, b = 0x8000 };";
        string[] spellings = ["uint8", "int8", "bool", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64", "uint16>", "int32<", "kind", "float16", "int128",];
        foreach (string spelling in spellings)
        {
            var layout = new CStruct(Enum + $"\nstruct __memory_scalar {{ {spelling} value; }};", isLittleEndian: true);
            var codec = MemoryScalarCodec.ForValue(layout, spelling);
            int size = layout.GetStructSizeInBytes("__memory_scalar");
            foreach (byte[] bytes in Patterns(size))
            {
                AssertSame(layout.ReadValue(bytes, spelling)!, codec.Decode(bytes), spelling);
            }
        }
    }

    /// <summary>
    ///     A bit slice decodes directly to what a read of its <c>__bits</c> layout returns, for unsigned, signed,
    ///     big-endian and enum storage and slices at the low bit, in the middle and at the top of the unit, narrower and
    ///     wider than 32 bits.
    /// </summary>
    [TestMethod]
    public void Slices_DecodeAsTheirLayoutReadsThem()
    {
        const string Enum = "enum kind : uint16 { a = 1, b = 5 };";
        (string Storage, int Size)[] storages = [("uint8", 1), ("uint16", 2), ("int32", 4), ("uint64", 8), ("uint16>", 2), ("int64>", 8), ("kind", 2),];
        var options = new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, BitfieldAllocation = BitfieldAllocation.LowBitFirst, };
        foreach ((string storage, int size) in storages)
        {
            int bits = size * 8;
            foreach ((int offset, int width) in new[] { (0, 1), (0, bits), (1, bits - 1), (bits / 2, bits / 4), (bits - 3, 3), })
            {
                if (storage == "kind" && width > 3)
                {
                    continue;
                }

                string padding = offset == 0 ? string.Empty : $"{storage} :{offset};";
                var layout = new CStruct(Enum + $"\nstruct __bits {{ {padding} {storage} value:{width}; }};", isLittleEndian: true, compilationOptions: options);
                var codec = MemoryScalarCodec.ForSlice(layout, offset, width);
                foreach (byte[] bytes in Patterns(size))
                {
                    AssertSame(layout.ReadValue(bytes, "__bits.value")!, codec.Decode(bytes), $"{storage} {offset}:{width}");
                }
            }
        }
    }

    /// <summary>Returns byte patterns of one size: all zeros, all ones, alternating bits, and an ascending sequence.</summary>
    /// <param name="size">The size in bytes.</param>
    /// <returns>The patterns.</returns>
    private static IEnumerable<byte[]> Patterns(int size)
    {
        yield return new byte[size];
        yield return Enumerable.Repeat((byte)0xFF, size).ToArray();
        yield return Enumerable.Repeat((byte)0xA5, size).ToArray();
        yield return Enumerable.Range(1, size).Select(value => (byte)(value * 37)).ToArray();
    }

    /// <summary>Asserts two decoded values are the same: the same type, and an equal value (an enum result by its value and name).</summary>
    /// <param name="expected">The value a read of the layout returned.</param>
    /// <param name="actual">The value decoded directly.</param>
    /// <param name="label">The case, named in a failure.</param>
    private static void AssertSame(object expected, object actual, string label)
    {
        Assert.AreEqual(expected.GetType(), actual.GetType(), label);
        if (expected is EnumValueResult enumExpected)
        {
            var enumActual = (EnumValueResult)actual;
            Assert.AreEqual(enumExpected.Value, enumActual.Value, label);
            Assert.AreEqual(enumExpected.Name, enumActual.Name, label);
            Assert.AreEqual(enumExpected.RawBits, enumActual.RawBits, label);
            return;
        }

        Assert.AreEqual(Convert.ToString(expected, CultureInfo.InvariantCulture), Convert.ToString(actual, CultureInfo.InvariantCulture), label);
    }
}
