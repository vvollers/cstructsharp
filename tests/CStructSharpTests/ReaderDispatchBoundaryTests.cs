namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks reader dispatch at nonzero origins, packed bit windows and unnamed debug fields.</summary>
[TestClass]
public class ReaderDispatchBoundaryTests
{
    /// <summary>A void alias has no standalone value reader, even when the input contains bytes.</summary>
    [TestMethod]
    public void VoidAliasRead_ExplainsMissingValueHandler()
    {
        var layout = new CStruct("typedef void opaque;");
        using var stream = new MemoryStream(new byte[] { 17, });

        // Opaque type names are useful for pointers, but cannot decode a value by themselves.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => layout.Parse(stream, "opaque"));
        StringAssert.Contains(failure.Message, "No handler for field type void");
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>A root primitive alias aligns from the actual stream position without rewinding into earlier bytes.</summary>
    /// <param name="type">The primitive behind the root alias.</param>
    /// <param name="alignment">Its byte alignment and encoded width.</param>
    [TestMethod]
    [DataRow("uint16", 2)]
    [DataRow("uint32", 4)]
    public void PrimitiveRootAlias_AlignsFromANonzeroOrigin(string type, int alignment)
    {
        var layout = new CStruct("typedef " + type + " word;", aligned: true);
        var bytes = new byte[alignment * 2];
        bytes.AsSpan(0, alignment).Fill(99);
        bytes[alignment] = 0x34;
        bytes[alignment + 1] = 0x12;
        using var stream = new MemoryStream(bytes);
        stream.Position = 1;
        Assert.AreEqual(0x1234U, layout.ReadValue<uint>(stream, "word"));
        Assert.AreEqual((long)bytes.Length, stream.Position);
    }

    /// <summary>A two-byte packed window must retain big-endian storage when a declared byte field crosses a byte boundary.</summary>
    [TestMethod]
    public void BigEndianPackedWindow_PreservesTheCrossingField()
    {
        var layout = new CStruct(
            "struct root { uint8 first:6; uint8 second:6; uint8 tail; };",
            isLittleEndian: false,
            compilationOptions: new CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst, });

        // 101010 is first; the next six bits are 000011. Four low padding bits finish the second byte.
        byte[] bytes = [0xA8, 0x30, 99,];
        StructValue parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(42, parsed.Get<int>("first"));
        Assert.AreEqual(3, parsed.Get<int>("second"));
        Assert.AreEqual(99, parsed.Get<int>("tail"));
        Assert.AreEqual(3, layout.ReadValue<int>(bytes, "root.second"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>An unnamed ordinary padding byte keeps its underscore spelling in debug ranges.</summary>
    [TestMethod]
    public void UnnamedPadding_KeepsItsReadableDebugPath()
    {
        var layout = new CStruct("struct root { uint8 _; uint8 value; };");
        using var stream = new MemoryStream(new byte[] { 91, 42, });
        (_, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(stream, "root");

        // The padding consumes storage even though it does not become a named result member.
        DebugData padding = debug.Single(entry => entry.Path == "root._");
        Assert.AreEqual(0L, padding.Start);
        Assert.AreEqual(1L, padding.End);
        Assert.AreEqual(2L, stream.Position);
    }
}
