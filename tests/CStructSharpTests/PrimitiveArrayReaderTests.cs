namespace CStructSharpTests;

using System.Collections.Generic;
using CStructSharp;

/// <summary>
///     Arrays of fixed-width numeric primitives are read in blocks (E2.3); the values, the final position, the
///     captured layout variable, debug output, and budget failures must match the per-element reader.
/// </summary>
[TestClass]
public class PrimitiveArrayReaderTests
{
    /// <summary>Every numeric codec in both byte orders decodes the same values through the bulk and the debug (per-element) paths.</summary>
    [TestMethod]
    public void BulkAndPerElementPaths_ProduceIdenticalValues()
    {
        string[] types = ["uint8", "int8", "bool", "int16<", "int16>", "uint16<", "uint16>", "int32<", "int32>", "uint32<", "uint32>", "int64<", "int64>", "uint64<", "uint64>", "float32<", "float32>", "float64<", "float64>"];
        var random = new Random(1234);
        foreach (string type in types)
        {
            var layout = new CStruct($"struct root {{ uint8 head; {type} values[70000]; uint8 tail; }};");
            byte[] bytes = new byte[layout.GetStructSizeInBytes("root")];
            random.NextBytes(bytes);
            if (type.StartsWith("float", StringComparison.Ordinal))
            {
                // Keep floats finite so the comparison below is not confused by NaN payload differences.
                for (int index = 1; index < bytes.Length - 1; index += type.StartsWith("float32", StringComparison.Ordinal) ? 4 : 8)
                {
                    bytes[index + (type.EndsWith('<') ? (type.StartsWith("float32", StringComparison.Ordinal) ? 3 : 7) : 0)] &= 0x7F;
                }
            }

            using var stream = new MemoryStream(bytes, writable: false);
            dynamic bulk = layout.ParseStream(stream, "root");
            Assert.AreEqual(bytes.Length, stream.Position, type);
            (List<DebugData> _, dynamic perElement) = layout.ParseStreamWithDebug(new MemoryStream(bytes, writable: false), "root");
            var bulkValues = (IList<object?>)bulk.values;
            var perElementValues = (IList<object?>)perElement.root.values;
            Assert.AreEqual(70000, bulkValues.Count, type);
            CollectionAssert.AreEqual(perElementValues.ToArray(), bulkValues.ToArray(), type);
            Assert.AreEqual(perElementValues[0]!.GetType(), bulkValues[0]!.GetType(), type);
            Assert.AreEqual((byte)perElement.root.tail, (byte)bulk.tail, type);
        }
    }

    /// <summary>The last array element remains the value a later array count sees, exactly as with the per-element reader.</summary>
    [TestMethod]
    public void LastElement_IsTheCapturedLayoutVariable()
    {
        var layout = new CStruct("struct root { uint8 counts[3]; uint8 values[counts]; uint32 wide[2]; uint8 more[wide]; };");
        Assert.ThrowsExactly<CStructLayoutException>(
            () => layout.Parse(new byte[] { 9, 9, 2, 1, 2, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 1 }, "root"),
            "an out-of-range last element removes the variable so the count cannot be evaluated");

        dynamic parsed = layout.Parse(new byte[] { 9, 9, 2, 1, 2, 0, 0, 0, 0, 3, 0, 0, 0, 7, 8, 9 }, "root");
        Assert.AreEqual(2, ((IList<object?>)parsed.values).Count);
        Assert.AreEqual(3, ((IList<object?>)parsed.more).Count);
    }

    /// <summary>Exceeding the total read budget inside a bulk array still raises the limit exception, and a short input still fails.</summary>
    [TestMethod]
    public void Budget_AndShortInput_StillFail()
    {
        var layout = new CStruct("struct root { uint32 values[100000]; };");
        byte[] bytes = new byte[400000];
        Assert.ThrowsExactly<CStructReadLimitException>(
            () => layout.Parse(bytes, "root", options: new ReadOptions { MaxTotalBytesRead = 100000 }));
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(bytes.AsSpan(0, 399999).ToArray(), "root"));
        StringAssert.Contains(failure.Message, "Not enough bytes");
    }

    /// <summary>Multidimensional numeric arrays still reshape correctly after the flat bulk read.</summary>
    [TestMethod]
    public void MultidimensionalArrays_ReshapeAfterBulkRead()
    {
        var layout = new CStruct("struct root { uint16 grid[3][4]; };");
        byte[] bytes = new byte[24];
        for (int index = 0; index < 12; index++)
        {
            bytes[index * 2] = (byte)index;
        }

        dynamic parsed = layout.Parse(bytes, "root");
        var rows = (IList<object?>)parsed.grid;
        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual((ushort)7, (ushort)((IList<object?>)rows[1]!)[3]!);
    }
}
