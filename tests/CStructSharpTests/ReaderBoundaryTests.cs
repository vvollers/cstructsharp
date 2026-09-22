namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks exact reader limits and caller-visible names retained by debug traversal.</summary>
[TestClass]
public class ReaderBoundaryTests
{
    /// <summary>A UTF-16 surrogate pair cannot cross independently decoded rows of a fixed string table.</summary>
    /// <param name="littleEndian">Whether each UTF-16 code unit stores its low byte first.</param>
    /// <param name="debug">Whether the reader also records field ranges.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WideStringTable_RejectsSurrogatesSplitAcrossRows(bool littleEndian, bool debug)
    {
        string type = littleEndian ? "wchar<" : "wchar>";
        var layout = new CStruct($"struct root {{ {type} rows[2][1]; }};");
        byte[] bytes = littleEndian ? new byte[] { 0, 0xD8, 0, 0xDC, } : new byte[] { 0xD8, 0, 0xDC, 0, };

        // The combined units form a pair, but each one-unit row is an invalid independent UTF-16 string.
        CStructReadException failure = Assert.Throws<CStructReadException>(() =>
        {
            if (debug)
            {
                layout.ParseWithDebug(bytes, "root");
            }
            else
            {
                layout.Parse(bytes, "root");
            }
        });
        StringAssert.StartsWith(failure.Message, "Wide-character buffer contains an invalid UTF-16 code-unit sequence");
        Assert.IsInstanceOfType<EncoderFallbackException>(failure.InnerException);
    }

    /// <summary>A union reached through an unaligned pointer still starts each composite member at the union address.</summary>
    /// <param name="address">The stored pointer target, deliberately not aligned to the composite member's two-byte boundary.</param>
    /// <param name="debug">Whether the reader also captures field ranges.</param>
    [TestMethod]
    [DataRow(1, false)]
    [DataRow(1, true)]
    [DataRow(3, false)]
    [DataRow(3, true)]
    public void UnionPointer_CompositeMemberBeginsAtTheStoredTarget(int address, bool debug)
    {
        var layout = new CStruct("union choice { struct { uint8 first; uint16 second; } item; uint32 raw; }; struct root { choice *target; };", pointerSize: 1, aligned: true);
        byte[] bytes = new byte[12];
        bytes[0] = (byte)address;
        bytes[address] = 0xA1;
        bytes[address + 1] = 0xB2;
        bytes[address + 2] = 0xC3;
        bytes[address + 3] = 0xD4;
        using var source = new MemoryStream(bytes);
        StructValue parsed = debug ? layout.ParseWithDebug(source, "root").Value : layout.Parse(source, "root");
        var pointer = (Pointer)parsed["target"]!;
        Assert.AreEqual((long)address, pointer.Address);
        Assert.IsTrue(pointer.IsDereferenced);
        var union = (UnionValue)pointer.Value!;
        Assert.AreEqual((byte)0xA1, ((StructValue)union["item"]!)["first"]);
        Assert.AreEqual(0xD4C3B2A1U, union["raw"]);
        Assert.AreEqual(1L, source.Position, "Following the target must restore the pointer's continuation position.");
    }

    /// <summary>A multidimensional array may contain exactly the configured maximum number of leaf elements.</summary>
    /// <param name="debug">Whether the reader also captures field ranges.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultidimensionalArray_AcceptsTheExactLeafLimit(bool debug)
    {
        var layout = new CStruct("struct root { uint8 values[2][2]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 4, 9, });
        var options = new ReadOptions { MaxArrayElements = 4, };
        StructValue result = debug ? layout.ParseWithDebug(source, "root", options: options).Value : layout.Parse(source, "root", options: options);
        var rows = (IList<object?>)result["values"]!;
        Assert.AreEqual(2, rows.Count);
        CollectionAssert.AreEqual(new object[] { (byte)1, (byte)2, }, ((IList<object?>)rows[0]!).ToArray());
        CollectionAssert.AreEqual(new object[] { (byte)3, (byte)4, }, ((IList<object?>)rows[1]!).ToArray());
        Assert.AreEqual((byte)9, result["tail"]);
        Assert.AreEqual(5L, source.Position);
    }

    /// <summary>Debug paths use the requested typedef alias, not the implementation's different struct tag.</summary>
    [TestMethod]
    public void TaggedAlias_DebugPathsUseThePublicAlias()
    {
        var layout = new CStruct("typedef struct hidden_tag { uint8 value; } public_alias;");
        ParseResult result = layout.ParseWithDebug(new byte[] { 7, }, "public_alias");
        Assert.AreEqual((byte)7, result.Value["value"]);

        // The leaf's public path and byte range are part of the debug result, not just its decoded value.
        Assert.IsTrue(result.Debug.Any(item => item.Path == "public_alias.value" && item.Start == 0 && item.End == 1));
        foreach (var item in result.Debug)
        {
            Assert.IsFalse(item.Path.Contains("hidden_tag", StringComparison.Ordinal), item.Path);
        }
    }
}
