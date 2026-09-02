namespace CStructSharp.Tests.Dissect;

using System.Dynamic;

/// <summary>Groups tests for test pointer so changes to this behavior are caught.</summary>
[TestClass]
public class TestPointer
{
    /// <summary>
    ///     Models a C argument-vector style structure where an array holds multiple char pointers. This test verifies pointer
    ///     array storage, address interpretation, and dereferencing to pointed strings.
    /// </summary>
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
    ///     Verifies core pointer semantics with two uint32 pointers stored in one struct. It checks that pointer values
    ///     resolve to target data at the expected offsets.
    /// </summary>
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
    ///     Verifies multi-level pointer dereferencing for a uint32 value with pointer size 2.
    /// </summary>
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
    ///     Parses a two-level pointer chain where the first address leads to a second address and the second leads to a
    ///     primitive value. It protects both pointer-depth bookkeeping and restoration of the stream position between
    ///     dereference hops.
    /// </summary>
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
    ///     Parses a pointer to a pointer to a nested struct, validating two-level indirection into the same structure
    ///     used by TestPointerStruct.
    /// </summary>
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
    ///     Parses a pointer to a nested struct that contains fixed arrays, scalars, and variable-length strings. This
    ///     validates multi-level dereference and mixed-type decoding through pointer indirection.
    /// </summary>
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
