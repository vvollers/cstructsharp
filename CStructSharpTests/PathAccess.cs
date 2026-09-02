namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Groups tests for path access so changes to this behavior are caught.</summary>
[TestClass]
public class PathAccess
{
    /// <summary>
    ///     Confirms a path to a fixed-size array reports the declaration's element count, not the number of bytes in the
    ///     stream. This lets callers reason about array shape without decoding every element.
    /// </summary>
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
    ///     Confirms an unsized character array derives its logical length from the first NUL terminator. The terminator
    ///     is storage metadata, so it is excluded from the returned character count.
    /// </summary>
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
    ///     Selects the second nested-struct array element by path and verifies the parser starts at that element's
    ///     computed stride rather than treating the array as an unstructured byte range.
    /// </summary>
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
    ///     Selects a nested object directly, then compares it with a full-root parse. This verifies path traversal
    ///     preserves the same field offsets and values while returning only the requested object.
    /// </summary>
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
    ///     Selects individual structs inside an array whose fixed character buffers differ in termination. It verifies
    ///     array indexing, fixed-buffer decoding, and preservation of embedded trailing NUL characters.
    /// </summary>
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
    ///     Requests debug information for a nested path and verifies every returned range belongs to that subtree. This
    ///     prevents unrelated parent or sibling bytes from being presented as part of a focused inspection result.
    /// </summary>
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
    ///     Resolves an array-index path to the second element's byte address, proving path resolution applies the
    ///     element stride instead of returning the array field's base address.
    /// </summary>
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
    ///     Resolves a field after a one-byte predecessor, protecting the basic field-offset calculation used by update
    ///     operations and debug ranges.
    /// </summary>
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
    ///     Distinguishes pointer-field storage from dereferenced target storage in a nested path. The <c>.address</c>
    ///     accessor must identify the pointer bytes, whereas <c>.value</c> must follow the stored address.
    /// </summary>
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
    ///     Combines nested-struct layout, array stride, and a field offset to find a scalar inside the second element.
    ///     This is the address calculation required for safe targeted updates in arrays of records.
    /// </summary>
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
    ///     Confirms the explicit <c>.address</c> accessor returns the pointer field's own storage, which is where callers
    ///     write when changing the address rather than the pointee.
    /// </summary>
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
    ///     Confirms the explicit <c>.value</c> accessor follows the pointer and returns the target's byte address. This
    ///     is the complementary contract to <c>.address</c> and underpins pointer-target updates.
    /// </summary>
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
