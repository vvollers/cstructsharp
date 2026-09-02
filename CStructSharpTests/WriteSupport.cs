namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Groups tests for write support so changes to this behavior are caught.</summary>
[TestClass]
public class WriteSupport
{
    /// <summary>
    ///     Verifies the dynamic-object write path maps members by layout name and emits each <c>uint16</c> in the
    ///     configured little-endian order. The assertion protects the fundamental object-to-byte contract used by
    ///     higher-level serializers.
    /// </summary>
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
    ///     Verifies the POCO binding path follows the same member-name and byte-order rules as an <see cref="ExpandoObject"/>.
    ///     This matters because callers can supply ordinary CLR objects without manually building a dynamic graph.
    /// </summary>
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
    ///     Resolves the second field's offset inside an existing struct and overwrites only its two-byte storage. The
    ///     unchanged first field proves an update is positional rather than a complete reserialization of the stream.
    /// </summary>
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
    ///     Preserves the resolved bit offset when an in-place update selects a later bitfield in shared storage. The
    ///     low nibble must remain unchanged, and the operation must restore the caller's original stream position.
    /// </summary>
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
    ///     Writes one selected array element through its scalar codec instead of passing the scalar value to the
    ///     collection writer for the complete declared field.
    /// </summary>
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
    ///     Carries the remaining pointer depth through address resolution so the final <c>.value</c> writes the
    ///     primitive target rather than interpreting it as another pointer address.
    /// </summary>
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
    ///     Follows the pointer's stored address for the <c>.value</c> path, writes the target value there, and leaves the
    ///     pointer field itself intact. This distinguishes a pointer dereference update from writing a new address.
    /// </summary>
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
    ///     Selects a nested struct path and writes only that sub-object's storage. The fixture verifies that path-based
    ///     writing can target an inner declaration without requiring a complete outer object or touching its sibling.
    /// </summary>
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
