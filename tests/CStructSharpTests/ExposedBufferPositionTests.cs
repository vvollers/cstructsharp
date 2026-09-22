namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks caller positions and failure coordinates when reads borrow a MemoryStream's exposed buffer.</summary>
[TestClass]
public class ExposedBufferPositionTests
{
    /// <summary>A failed checked scalar conversion reports the position after its successfully decoded input.</summary>
    [TestMethod]
    public void TypedConversionFailure_AttachesConsumedOffset()
    {
        var layout = new CStruct("struct root { uint16 value; };");
        byte[] bytes = [99, 44, 1,];
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true) { Position = 1, };

        // The stored value is 300, which cannot be represented by the caller's requested byte type.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.ReadValue<byte>(source, "root.value"));
        Assert.AreEqual(3L, failure.Offset);
        Assert.AreEqual("root.value", failure.Path);
        Assert.AreEqual(3L, source.Position);
    }

    /// <summary>Both selected-object and debug-root reads publish their final cursor to the source.</summary>
    /// <param name="selected">Whether the operation selects the nested struct instead of debugging the root.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SuccessfulRead_PublishesItsPosition(bool selected)
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { uint8 prefix; child item; };");
        byte[] bytes = [99, 3, 7,];
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true) { Position = 1, };
        if (selected)
        {
            Assert.AreEqual((byte)7, layout.Parse(source, "root.item")["value"]);
        }
        else
        {
            Assert.AreEqual((byte)3, layout.ParseWithDebug(source, "root").Value["prefix"]);
        }

        Assert.AreEqual(3L, source.Position);
    }

    /// <summary>Array-query diagnostics capture the failure cursor before restoring the original position.</summary>
    [TestMethod]
    public void FailedArrayLength_AttachesOffsetBeforeRestoringOrigin()
    {
        var layout = new CStruct("struct root { int8 count; uint8 values[count]; };");
        byte[] bytes = [99, 255,];
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true) { Position = 1, };

        // Reading the count moves the private cursor to byte two; the query itself must still restore byte one.
        CStructException failure = Assert.Throws<CStructException>(() => layout.GetArrayLength(source, "root.values"));
        Assert.AreEqual(2L, failure.Offset);
        Assert.AreEqual("root.values", failure.Path);
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>A scalar length query reports the cursor left by resolving a preceding runtime array.</summary>
    [TestMethod]
    public void ScalarArrayLength_AttachesResolutionOffset()
    {
        var layout = new CStruct("struct root { uint8 count; uint8 values[count]; uint8 tail; };");
        byte[] bytes = [99, 2, 5, 6, 7,];
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true) { Position = 1, };

        // Resolution reads the count, then computes the tail address without consuming the array itself.
        CStructPathException failure = Assert.Throws<CStructPathException>(() => layout.GetArrayLength(source, "root.tail"));
        Assert.AreEqual(2L, failure.Offset);
        Assert.AreEqual("root.tail", failure.Path);
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>A selected struct's truncated field reports the consumed input position, not the stale source origin.</summary>
    [TestMethod]
    public void FailedSelectedParse_AttachesConsumedOffset()
    {
        var layout = new CStruct("struct child { uint8 head; uint32 tail; }; struct root { uint8 prefix; child item; };");
        byte[] bytes = [99, 3, 7,];
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true) { Position = 1, };

        // The prefix and child's head exist; only the following four-byte field is missing.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.Parse(source, "root.item"));
        Assert.AreEqual(3L, failure.Offset);
        Assert.AreEqual("root.item", failure.Path);
        Assert.AreEqual(3L, source.Position);
    }
}
