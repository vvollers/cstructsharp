namespace CStructSharp.Tests;

/// <summary>Checks selection and updates through arrays whose string elements have individual terminators.</summary>
[TestClass]
public class TerminatedStringArrayTraversalTests
{
    /// <summary>Each selected string is located by its preceding encoded extents, not by a fixed byte stride.</summary>
    /// <param name="type">The named terminated-string codec.</param>
    /// <param name="hex">Two terminated strings followed by a scalar tail byte.</param>
    /// <param name="first">The first decoded string.</param>
    /// <param name="secondOffset">The byte offset of the second string.</param>
    /// <param name="tailOffset">The byte offset after the second string's terminator.</param>
    [TestMethod]
    [DataRow("cstring", "410042430063", "A", 2L, 5L)]
    [DataRow("utf8_string_zero", "C3850042430063", "Å", 3L, 6L)]
    [DataRow("unicode_string_zero<", "4100000042004300000063", "A", 4L, 10L)]
    [DataRow("unicode_string_newline>", "0041000A00420043000A63", "A", 4L, 10L)]
    public void SelectedElements_MatchTheWholeArray(string type, string hex, string first, long secondOffset, long tailOffset)
    {
        var layout = new CStruct("struct root { " + type + " names[2]; uint8 tail; };");
        byte[] bytes = Convert.FromHexString(hex);
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        CollectionAssert.AreEqual(new object?[] { first, "BC", }, ((IEnumerable<object?>)parsed.names).ToArray());
        Assert.AreEqual((byte)99, (byte)parsed.tail);

        using var source = new MemoryStream(bytes);
        Assert.AreEqual(0L, layout.ResolveAddress(source, "root.names[0]"));
        Assert.AreEqual(first, layout.ReadValue<string>(source, "root.names[0]"));
        Assert.AreEqual(secondOffset, source.Position);

        source.Position = 0;
        Assert.AreEqual(secondOffset, layout.ResolveAddress(source, "root.names[1]"));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual("BC", layout.ReadValue<string>(source, "root.names[1]"));
        Assert.AreEqual(tailOffset, source.Position);

        source.Position = 0;
        Assert.AreEqual(2, layout.GetArrayLength(source, "root.names"));
        Assert.AreEqual(1, layout.GetArrayLength(source, "root.names[0]"));
        Assert.AreEqual(2, layout.GetArrayLength(source, "root.names[1]"));
        Assert.AreEqual(0L, source.Position);
    }

    /// <summary>A field following the array starts after every string and its complete encoded terminator.</summary>
    /// <param name="type">The named terminated-string codec.</param>
    /// <param name="hex">The encoded array and tail byte.</param>
    /// <param name="tailOffset">The expected absolute tail position in this zero-origin stream.</param>
    [TestMethod]
    [DataRow("cstring", "410042430063", 5L)]
    [DataRow("utf8_string_zero", "C3850042430063", 6L)]
    [DataRow("unicode_string_zero<", "4100000042004300000063", 10L)]
    [DataRow("unicode_string_newline>", "0041000A00420043000A63", 10L)]
    public void FollowingField_UsesAllStringExtents(string type, string hex, long tailOffset)
    {
        var layout = new CStruct("struct root { " + type + " names[2]; uint8 tail; };");
        byte[] bytes = Convert.FromHexString(hex);
        using var source = new MemoryStream(bytes);

        Assert.AreEqual(tailOffset, layout.ResolveAddress(source, "root.tail"));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail"));
        Assert.AreEqual((long)bytes.Length, source.Position);
    }

    /// <summary>An equal-size replacement updates only the selected string and restores the caller's position.</summary>
    [TestMethod]
    public void SelectedUpdate_PreservesTheOtherStringAndTail()
    {
        var layout = new CStruct("struct root { cstring names[2]; uint8 tail; };");
        byte[] bytes = [65, 0, 66, 67, 0, 99,];
        using var source = new MemoryStream(bytes);

        layout.Update(source, "root.names[1]", "XY");

        Assert.AreEqual(0L, source.Position);
        CollectionAssert.AreEqual(new byte[] { 65, 0, 88, 89, 0, 99, }, bytes);
    }
}
