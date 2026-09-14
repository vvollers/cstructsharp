namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Groups tests for path access so changes to this behavior are caught.</summary>
[TestClass]
public class PathAccess
{
    /// <summary>
    ///     items[3] declares three uint8 elements, so the returned length must be 3.
    /// </summary>
    /// <remarks>
    ///     The API reports an element count, not an address or byte size. For wider element types those quantities
    ///     would differ, even though they happen to match for this one-byte array.
    /// </remarks>
    [TestMethod]
    public void GetDynamicArrayLength_UsesArrayLength()
    {
        const string d = """
                         struct root { uint8 items[3]; };
                         """;

        byte[] buf = [0x10, 0x20, 0x30,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        int length = c.GetDynamicArrayLength(stream, "root.items");

        Assert.AreEqual(3, length);
    }

    /// <summary>
    ///     The bytes spell hi followed by a zero byte.
    /// </summary>
    /// <remarks>
    ///     For char name[], that zero ends the text, so the reported length is 2. The terminator is consumed as part of
    ///     locating the end but is not itself a character in the returned length.
    /// </remarks>
    [TestMethod]
    public void GetDynamicArrayLength_UsesStringLength()
    {
        const string d = """
                         struct root { char name[]; };
                         """;

        byte[] buf = [0x68, 0x69, 0x00,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        int length = c.GetDynamicArrayLength(stream, "root.name");

        Assert.AreEqual(2, length);
    }

    /// <summary>
    ///     Each inner record is a two-byte uint16.
    /// </summary>
    /// <remarks>
    ///     Index 1 selects the second record, starting two bytes after index 0. Reading 22 00 there must return value =
    ///     0x22, demonstrating that the path follows record sizes rather than treating the index as a byte offset.
    /// </remarks>
    [TestMethod]
    public void ParseStream_Path_ArrayElement_ReturnsValue()
    {
        const string d = """
                         struct inner { uint16 value; };
                         struct root { inner items[2]; };
                         """;

        byte[] buf = [0x11, 0x00, 0x22, 0x00,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        dynamic item = c.ParseStream(stream, "root.items[1]");

        Assert.AreEqual(0x22, item.value);
    }

    /// <summary>
    ///     outer contains inn followed by y.
    /// </summary>
    /// <remarks>
    ///     Selecting outer.inn must return the inner object with x = 0x11. After rewinding, parsing outer must still
    ///     find y = 0x22. A path selects a part of the same layout; it does not invent a new layout for the remaining
    ///     bytes.
    /// </remarks>
    [TestMethod]
    public void ParseStream_Path_ReturnsSubObject()
    {
        const string d = """
                         struct inner { uint8 x; };
                         struct outer { inner inn; uint8 y; };
                         """;

        byte[] buf = [0x11, 0x22,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        dynamic inner = c.ParseStream(stream, "outer.inn");

        Assert.AreEqual(0x11, inner.x);

        stream.Seek(0, SeekOrigin.Begin);
        dynamic outer = c.ParseStream(stream, "outer");
        Assert.AreEqual(0x22, outer.y);
    }

    /// <summary>
    ///     Each inner record contains exactly four character bytes.
    /// </summary>
    /// <remarks>
    ///     Index 1 reads test, while index 0 reads one followed by a retained zero character and still has length 4.
    ///     Fixed char buffers preserve their full declared length; they are different from unsized, zero-terminated
    ///     text.
    /// </remarks>
    [TestMethod]
    public void ParseStream_Path_StringInNestedArray_IsExpected_V2()
    {
        const string d = """
                         struct inner { char name[4]; };
                         struct root { inner items[2]; };
                         """;

        byte[] buf = [(byte)'o', (byte)'n', (byte)'e', 0x00, (byte)'t', (byte)'e', (byte)'s', (byte)'t',];

        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        dynamic secondItem = c.ParseStream(stream, "root.items[1]");

        Assert.AreEqual("test", secondItem.name);

        stream.Seek(0, SeekOrigin.Begin);
        dynamic firstItem = c.ParseStream(stream, "root.items[0]");
        Assert.StartsWith("one", firstItem.name);
        Assert.AreEqual(4, firstItem.name.Length);
        Assert.AreEqual(0, firstItem.name[3]);
    }

    /// <summary>
    ///     outer.i occupies the two middle bytes between a and b.
    /// </summary>
    /// <remarks>
    ///     Selecting it must return x = 2, and every debug path must start with outer.i. This keeps the byte inspector
    ///     focused on the selected child rather than including unrelated siblings.
    /// </remarks>
    [TestMethod]
    public void ParseStreamWithDebug_Path_FiltersDebugData()
    {
        const string d = """
                         struct inner { uint8 x; uint8 y; };
                         struct outer { uint8 a; inner i; uint8 b; };
                         """;

        byte[] buf = [0x01, 0x02, 0x03, 0x04,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(stream, "outer.i");
        dynamic inner = obj;

        Assert.AreEqual(0x02, inner.x);
        Assert.IsNotNull(debug);
        Assert.IsTrue(debug.All(dbg => dbg.DebugStackString.StartsWith("outer.i", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Each items element occupies one byte, so zero-based index 1 is at offset 1.
    /// </summary>
    /// <remarks>
    ///     ResolveAddress returns that location, not the value 0x20 stored there. This distinction matters when
    ///     highlighting or updating an element.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_ArrayIndex_ReturnsElementOffset()
    {
        const string d = """
                         struct root { uint8 items[3]; };
                         """;

        byte[] buf = [0x10, 0x20, 0x30,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        long address = c.ResolveAddress(stream, "root.items[1]");

        Assert.AreEqual(1, address);
    }

    /// <summary>
    ///     With packed layout, the uint16 b follows the one-byte a immediately.
    /// </summary>
    /// <remarks>
    ///     Its address must therefore be 1, even though some aligned C layouts would insert padding. This test checks
    ///     location only and does not ask the API to return b's numeric value.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_Field_ReturnsFieldOffset()
    {
        const string d = """
                         struct root { uint8 a; uint16 b; };
                         """;

        byte[] buf = [0x11, 0x22, 0x33,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        long address = c.ResolveAddress(stream, "root.b");

        Assert.AreEqual(1, address);
    }

    /// <summary>
    ///     The pointer slot is at offset 0 and stores 8, pointing to the byte 0x2A.
    /// </summary>
    /// <remarks>
    ///     Resolving ptr.value must return target offset 8, while ptr.address must return slot offset 0. A nested
    ///     containing struct must not blur the difference between the pointer's storage and its target.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_NestedPointerValue_ReturnsTargetOffset()
    {
        const string d = """
                         struct inner { uint8 *ptr; };
                         struct root { inner child; };
                         """;

        byte[] buf = new byte[16];
        buf[0] = 0x08;
        buf[8] = 0x2A;

        var c = new CStruct(d, 1);

        using var stream0 = new MemoryStream(buf);
        long address = c.ResolveAddress(stream0, "root.child.ptr.value");

        using var stream1 = new MemoryStream(buf);
        long field = c.ResolveAddress(stream1, "root.child.ptr.address");

        Assert.AreEqual(8, address);
        Assert.AreEqual(0, field);
    }

    /// <summary>
    ///     Each packed inner record is three bytes: one for a and two for b.
    /// </summary>
    /// <remarks>
    ///     The second record starts at 3, and its b starts one byte later, so the expected address is 4. The path
    ///     calculation must combine the array stride with the field's offset within its record.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_Path_ArrayElementField_ReturnsOffset()
    {
        const string d = """
                         struct inner { uint8 a; uint16 b; };
                         struct root { inner items[2]; };
                         """;

        byte[] buf = [0x10, 0x11, 0x22, 0x20, 0x33, 0x44,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        long address = c.ResolveAddress(stream, "root.items[1].b");

        Assert.AreEqual(4, address);
    }

    /// <summary>
    ///     The two-byte pointer at offset 0 stores the target address 4.
    /// </summary>
    /// <remarks>
    ///     Asking for ptr.address must return 0 because this accessor selects where the address is stored. It does not
    ///     return the stored address number or follow it to the uint16 target.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_PointerAddress_ReturnsPointerFieldOffset()
    {
        const string d = """
                         struct ptrtest { uint16 *ptr; };
                         """;

        byte[] buf = [0x04, 0x00, 0x11, 0x22, 0x33, 0x44,];
        var c = new CStruct(d, 2);
        using var stream = new MemoryStream(buf);

        long address = c.ResolveAddress(stream, "ptrtest.ptr.address");

        Assert.AreEqual(0, address);
    }

    /// <summary>
    ///     The one-byte pointer stores 2, so ptr.value must resolve to offset 2.
    /// </summary>
    /// <remarks>
    ///     The uint32 target is wider than the pointer slot. This checks that following a pointer uses its stored
    ///     address, rather than advancing by the target type's width.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_PointerValue_ReturnsTargetOffset()
    {
        const string d = """
                         struct ptrtest { uint32 *ptr; };
                         """;

        byte[] buf = [0x02, 0x00, 0x11, 0x22, 0x33, 0x44,];
        var c = new CStruct(d, 1);
        using var stream = new MemoryStream(buf);

        long address = c.ResolveAddress(stream, "ptrtest.ptr.value");

        Assert.AreEqual(2, address);
    }
}
