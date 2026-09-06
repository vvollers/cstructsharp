namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Groups tests for write support so changes to this behavior are caught.</summary>
[TestClass]
public class WriteSupport
{
    /// <summary>
    ///     The dynamic input wraps a and b under the root name test.
    /// </summary>
    /// <remarks>
    ///     Their values 0x0102 and 0x0304 must become 02 01 04 03 in little-endian order. Each uint16 contributes two
    ///     bytes, and matching member names connects the C# object to the layout fields.
    /// </remarks>
    [TestMethod]
    public void Serialize_SimpleStruct_Expando_WritesBytes()
    {
        const string d = "struct test { uint16 a; uint16 b; };";
        var c = new CStruct(d, 1);

        dynamic data = new ExpandoObject();
        data.test = new ExpandoObject();
        data.test.a = (ushort)0x0102;
        data.test.b = (ushort)0x0304;

        byte[] bytes = c.Serialize("test", data);

        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x04, 0x03, }, bytes);
    }

    /// <summary>
    ///     An ordinary C# object supplies properties A and B for the layout's a and b fields.
    /// </summary>
    /// <remarks>
    ///     Binding must produce the same bytes, 02 01 04 03, as the dynamic-object example. Users need not construct a
    ///     dynamic dictionary to serialize a simple record.
    /// </remarks>
    [TestMethod]
    public void Serialize_SimpleStruct_Poco_WritesBytes()
    {
        const string d = "struct test { uint16 a; uint16 b; };";
        var c = new CStruct(d, 1);

        var data = new PocoTest { A = 0x0102, B = 0x0304, };

        byte[] bytes = c.Serialize("test", data);

        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, 0x04, 0x03, }, bytes);
    }

    /// <summary>
    ///     b follows a two-byte field, so its replacement 0x1122 belongs at offsets 2 and 3.
    /// </summary>
    /// <remarks>
    ///     The expected buffer is 00 00 22 11. The first two bytes must remain unchanged because only b was selected.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_Field_WritesAtOffset()
    {
        const string d = "struct test { uint16 a; uint16 b; };";
        var c = new CStruct(d, 1);

        using var stream = new MemoryStream(new byte[4]);
        c.UpdateStream(stream, "test.b", (ushort)0x1122);

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x22, 0x11, }, stream.ToArray());
    }

    /// <summary>
    ///     high is the upper four bits of 0xA5.
    /// </summary>
    /// <remarks>
    ///     Replacing it with 3 must produce 0x35, preserving low = 5. The update must remember the bit offset, not just
    ///     the shared byte address, and restore the caller's original position.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_NonFirstBitfield_UsesResolvedBitOffset()
    {
        const string d = "struct root { uint8 low:4; uint8 high:4; };";
        var c = new CStruct(d, 1);

        using var stream = new MemoryStream(new byte[] { 0xA5, });

        c.UpdateStream(stream, "root.high", (byte)0x3);

        CollectionAssert.AreEqual(new byte[] { 0x35, }, stream.ToArray());
        RegressionTestSupport.AssertPositionRestored(stream, 0);
    }

    /// <summary>
    ///     items[1] selects one uint16 from a three-element array.
    /// </summary>
    /// <remarks>
    ///     Writing 0xABCD must replace only the middle pair with CD AB. The writer must treat the replacement as a
    ///     scalar element, not require or rewrite the complete array.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_ArrayElement_UsesResolvedElementCodec()
    {
        const string d = "struct root { uint16 items[3]; };";
        var c = new CStruct(d, 1);

        using var stream = new MemoryStream(new byte[] { 0x11, 0x11, 0x22, 0x22, 0x33, 0x33, });

        c.UpdateStream(stream, "root.items[1]", (ushort)0xABCD);

        CollectionAssert.AreEqual(
            new byte[] { 0x11, 0x11, 0xCD, 0xAB, 0x33, 0x33, },
            stream.ToArray());
        RegressionTestSupport.AssertPositionRestored(stream, 0);
    }

    /// <summary>
    ///     Two one-byte addresses lead to a uint16 at offset 4.
    /// </summary>
    /// <remarks>
    ///     The path ptr.value.value exhausts both pointer levels, so 0x1234 must be written as 34 12 at the final
    ///     target. Neither intermediate address may be replaced.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_MultiLevelPointerFinalTarget_UsesResolvedCodec()
    {
        const string d = "struct root { uint16 **ptr; };";
        var c = new CStruct(d, 1);

        using var stream = new MemoryStream(new byte[] { 0x02, 0x00, 0x04, 0x00, 0x00, 0x00, });

        c.UpdateStream(stream, "root.ptr.value.value", (ushort)0x1234);

        CollectionAssert.AreEqual(
            new byte[] { 0x02, 0x00, 0x04, 0x00, 0x34, 0x12, },
            stream.ToArray());
        RegressionTestSupport.AssertPositionRestored(stream, 0);
    }

    /// <summary>
    ///     The two-byte pointer stores address 4.
    /// </summary>
    /// <remarks>
    ///     Updating ptr.value to 0x1234 must put 34 12 at offsets 4 and 5 while retaining 04 00 in the pointer slot.
    ///     Selecting the target differs from changing the stored address itself.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_PointerValue_WritesTarget()
    {
        const string d = "struct ptrtest { uint16 *ptr; };";
        var c = new CStruct(d, 2);

        byte[] buf = new byte[6];
        buf[0] = 0x04;
        buf[1] = 0x00;

        using var stream = new MemoryStream(buf);
        c.UpdateStream(stream, "ptrtest.ptr.value", (ushort)0x1234);

        CollectionAssert.AreEqual(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x34, 0x12, }, stream.ToArray());
    }

    /// <summary>
    ///     The path outer.i selects the one-byte inner record.
    /// </summary>
    /// <remarks>
    ///     Supplying only x = 0x11 must write that byte without requiring outer.y or a complete outer object. This is
    ///     writing the selected layout at the destination position, rather than rebuilding all of outer.
    /// </remarks>
    [TestMethod]
    public void WriteStream_Path_SubObject_WritesInner()
    {
        const string d = "struct inner { uint8 x; }; struct outer { inner i; uint8 y; };";
        var c = new CStruct(d, 1);

        dynamic inner = new ExpandoObject();
        inner.x = (byte)0x11;

        using var stream = new MemoryStream(new byte[1]);
        c.WriteStream(stream, "outer.i", inner);

        CollectionAssert.AreEqual(new byte[] { 0x11, }, stream.ToArray());
    }

    /// <summary>Groups tests for poco test so changes to this behavior are caught.</summary>
    private sealed class PocoTest
    {
        public ushort A { get; set; }

        public ushort B { get; set; }
    }
}
