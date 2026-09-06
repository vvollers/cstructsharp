namespace CStructSharp.Tests.Dissect;

using System.Dynamic;

/// <summary>Groups tests for test pointer so changes to this behavior are caught.</summary>
[TestClass]
public class TestPointer
{
    /// <summary>
    ///     args[4] reserves four two-byte pointer slots even though argc is 2.
    /// </summary>
    /// <remarks>
    ///     The first addresses, 9 and 22, lead to the strings argument one and argument two. The remaining zero
    ///     addresses must produce null pointers with no target value. These addresses refer to positions in the
    ///     supplied byte stream, not process memory.
    /// </remarks>
    [TestMethod]
    public void TestPointerArray()
    {
        const string d = """
                         struct mainargs {
                             uint8 argc;
                             char *args[4];
                         }
                         """;

        const string buf = "\u0002\u0009\u0000\u0016\u0000\u0000\u0000\u0000\u0000argument one\u0000argument two\u0000";

        var c = new CStruct(d, 2);
        byte[] bufBytes = buf.Select(o => (byte)o).ToArray();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "mainargs");

        dynamic result = obj;

        Assert.AreEqual(2, result.mainargs.argc);

        Pointer arg0 = result.mainargs.args[0];
        Pointer arg1 = result.mainargs.args[1];
        Pointer arg2 = result.mainargs.args[2];
        Pointer arg3 = result.mainargs.args[3];

        Assert.AreEqual(9, arg0.Address);
        Assert.AreEqual("argument one", (string)arg0.Value!);
        Assert.AreEqual(22, arg1.Address);
        Assert.AreEqual("argument two", (string)arg1.Value!);
        Assert.IsTrue(arg2.IsNull);
        Assert.IsNull(arg2.Value);
        Assert.IsTrue(arg3.IsNull);
        Assert.IsNull(arg3.Value);

        /*
         *
         * assert obj.argc == 2
    assert obj.args[2] == 0
    assert obj.args[3] == 0
    assert obj.args[0].dereference() == b"argument one"
    assert obj.args[1].dereference() == b"argument two"
         */
    }

    /// <summary>
    ///     Each pointer occupies two bytes, while each target occupies four.
    /// </summary>
    /// <remarks>
    ///     Stored addresses 4 and 8 lead to little-endian values 0x04030201 and 0x08070605. The test checks both the
    ///     stored address and the decoded target so confusing pointer width with target width cannot pass unnoticed.
    /// </remarks>
    [TestMethod]
    public void TestPointerBasic()
    {
        const string d = """
                         struct ptrtest {
                             uint32  *ptr1;
                             uint32  *ptr2;
                         };
                         """;

        const string buf = """
                           04 00 08 00 01 02 03 04 05 06 07 08
                           """;

        var c = new CStruct(d, 2);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "ptrtest");

        dynamic result = obj;

        Pointer ptr1 = result.ptrtest.ptr1;
        Pointer ptr2 = result.ptrtest.ptr2;

        Assert.AreEqual(4, ptr1.Address);
        Assert.AreEqual(8, ptr2.Address);
        Assert.AreEqual(0x04030201U, (uint)ptr1.Value!);
        Assert.AreEqual(0x08070605U, (uint)ptr2.Value!);
    }

    /// <summary>
    ///     Three stars mean three levels of indirection.
    /// </summary>
    /// <remarks>
    ///     Two-byte addresses lead from the field to offset 6, then 8, then 10. Only the final target is a uint32,
    ///     which must decode as 0x11223344. Intermediate targets must remain Pointer objects rather than being mistaken
    ///     for the final integer.
    /// </remarks>
    [TestMethod]
    public void TestPointerDepth()
    {
        const string d = """
                         struct ptrtest {
                             uint32 ***ptr;
                         };
                         """;

        const string buf = "06 00 00 00 00 00 08 00 0A 00 44 33 22 11";

        var c = new CStruct(d, 2);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "ptrtest");

        dynamic result = obj;

        Pointer level1 = result.ptrtest.ptr;
        var level2 = (Pointer)level1.Value!;
        var level3 = (Pointer)level2.Value!;

        Assert.AreEqual(6, level1.Address);
        Assert.AreEqual(8, level2.Address);
        Assert.AreEqual(10, level3.Address);
        Assert.AreEqual(0x11223344U, (uint)level3.Value!);
    }

    /// <summary>
    ///     A one-byte address leads to offset 1, which stores another address leading to offset 2.
    /// </summary>
    /// <remarks>
    ///     The four A bytes there form 0x41414141. The outer pointer reports depth 2 and the inner depth 1;
    ///     Dereference() and Value must expose the same already-decoded target.
    /// </remarks>
    [TestMethod]
    public void TestPointerPointer()
    {
        const string d = """
                         struct test {
                             uint32  **ptr;
                         };
                         """;

        // python string: b"\x01\x02AAAA
        const string buf = "\u0001\u0002AAAA";
        byte[] bufBytes = buf.Select(o => (byte)o).ToArray();
        var c = new CStruct(d, 1);

        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Pointer level1 = result.test.ptr;
        var level2 = (Pointer)level1.Dereference()!;

        Assert.AreEqual(1, level1.Address);
        Assert.AreEqual(2, level2.Address);
        Assert.AreEqual(0x41414141U, (uint)level2.Dereference()!);
        Assert.AreEqual(0x41414141U, (uint)level2.Value!);
        Assert.AreEqual(2, level1.Depth);
        Assert.AreEqual(1, level2.Depth);

        /*
         * assert isinstance(obj.ptr, Pointer)
            assert isinstance(obj.ptr.dereference(), Pointer)
            assert obj.ptr == 1
            assert obj.ptr.dereference() == 2
            assert obj.ptr.dereference().dereference() == 0x41414141
         */
    }

    /// <summary>
    ///     The two-byte pointer chain visits offsets 4 and 6 before reading the test record.
    /// </summary>
    /// <remarks>
    ///     That record mixes fixed narrow and UTF-16 text, little-endian integers, and terminated strings. The expected
    ///     strings are test, test, lalala, and test; reaching the record through two pointers must preserve the same
    ///     field interpretation as a direct read.
    /// </remarks>
    [TestMethod]
    public void TestPointerPointerStruct()
    {
        const string d = """
                         struct test {
                             char    magic[4];
                             wchar   wmagic[4];
                             uint8   a;
                             uint16  b;
                             uint32  c;
                             char    string[];
                             wchar   wstring[];
                         };

                         struct ptrtest {
                             test    **ptr;
                         };
                         """;

        const string buf
            = "\u0004\u0000\u0000\u0000\u0006\u0000testt\u0000e\u0000s\u0000t\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007lalala\u0000t\u0000e\u0000s\u0000t\u0000\u0000\u0000";

        var c = new CStruct(d, 2);
        byte[] bufBytes = buf.Select(o => (byte)o).ToArray();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "ptrtest");

        dynamic result = obj;

        Pointer level1 = result.ptrtest.ptr;
        var level2 = (Pointer)level1.Value!;
        dynamic deref = level2.Value!;

        Assert.AreEqual(4, level1.Address);
        Assert.AreEqual(6, level2.Address);
        Assert.AreEqual(2, level1.Depth);
        Assert.AreEqual(1, level2.Depth);
        Assert.AreEqual("test", deref.magic);
        Assert.AreEqual("test", deref.wmagic);
        Assert.AreEqual(0x01, deref.a);
        Assert.AreEqual(0x0302, deref.b);
        Assert.AreEqual(0x07060504UL, (ulong)deref.c);
        Assert.AreEqual("lalala", deref.@string);
        Assert.AreEqual("test", deref.wstring);
    }

    /// <summary>
    ///     A single two-byte pointer leads to the test record at offset 4. char[4] and wchar[4] both decode to test but
    ///     occupy different byte counts; the later unsized text fields end at their terminators.
    /// </summary>
    /// <remarks>
    ///     Values such as b = 0x0302 confirm that following the pointer preserves the configured little-endian
    ///     interpretation.
    /// </remarks>
    [TestMethod]
    public void TestPointerStruct()
    {
        const string d = """
                         struct test {
                             char    magic[4];
                             wchar   wmagic[4];
                             uint8   a;
                             uint16  b;
                             uint32  c;
                             char    string[];
                             wchar   wstring[];
                         };

                         struct ptrtest {
                             test    *ptr;
                         };
                         """;

        const string buf
            = "\u0004\u0000\u0000\u0000testt\u0000e\u0000s\u0000t\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007lalala\u0000t\u0000e\u0000s\u0000t\u0000\u0000\u0000";

        var c = new CStruct(d, 2);
        byte[] bufBytes = buf.Select(o => (byte)o).ToArray();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "ptrtest");

        dynamic result = obj;

        Pointer ptr = result.ptrtest.ptr;
        dynamic deref = ptr.Value!;

        Assert.AreEqual("test", deref.magic);
        Assert.AreEqual("test", deref.wmagic);
        Assert.AreEqual(0x01, deref.a);
        Assert.AreEqual(0x0302, deref.b);
        Assert.AreEqual(0x07060504UL, (ulong)deref.c);
        Assert.AreEqual("lalala", deref.@string);
        Assert.AreEqual("test", deref.wstring);
    }

    /*
     *
     * def test_pointer_of_pointer(cs: cstruct, compiled: bool) -> None:
        cdef = """
        struct test {
            uint32  **ptr;
        };
        """
        cs.pointer = cs.uint8
        cs.load(cdef, compiled=compiled)

        assert verify_compiled(cs.test, compiled)

        obj = cs.test(b"\x01\x02AAAA")
        assert isinstance(obj.ptr, Pointer)
        assert isinstance(obj.ptr.dereference(), Pointer)
        assert obj.ptr == 1
        assert obj.ptr.dereference() == 2
        assert obj.ptr.dereference().dereference() == 0x41414141
     */
}
