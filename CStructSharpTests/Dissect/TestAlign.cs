namespace CStructSharp.Tests.Dissect;

using System.Dynamic;

/// <summary>Groups tests for test align so changes to this behavior are caught.</summary>
[TestClass]
public class TestAlign
{
    /// <summary>
    ///     In C, all union members start at offset zero and the union size is driven by the largest member alignment and
    ///     width. This test verifies that aligned parsing follows those ABI-like rules.
    /// </summary>
    [TestMethod]
    public void Test_Align_Union()
    {
        const string d = """
                         union test {
                             uint32  a;
                             uint64  b;
                         };
                         """;

        const string buf = """
                               00 00 00 01 00 00 00 02
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Assert.AreEqual(8, str.Position);
        Assert.AreEqual(8, c.GetStructAlignmentInBytes("test"));
        Assert.AreEqual(8, c.GetStructSizeInBytes("test"));
        Assert.AreEqual(0x01000000, (int)result.a);
        Assert.AreEqual(0x0200000001000000, (long)result.b);
    }

    /// <summary>
    ///     Validates aligned struct layout with fixed-size arrays whose element alignment affects field offsets and total
    ///     size. This mirrors native C padding behavior between mixed-width members.
    /// </summary>
    [TestMethod]
    public void TestAlignArray()
    {
        const string d = """
                         struct test {
                             uint32  a;      // 0x00 = 0  (len 4, align 4, structalign 8)
                             uint64  b[4];   // 0x08 = 8  (len 32, align 8, structalign 8)
                             uint16  c;      // 0x28 = 40 (len 2, align 2, structalign 8)
                             uint32  d[2];   // 0x2c = 44 (len 8, align 4, structalign 8)
                             uint64  e;      // 0x38 = 56 (len 8, align 8, structalign 8)
                         };
                         """;

        const string buf = """
                           00 00 00 00 00 00 00 00  08 00 00 00 00 00 00 00
                           10 00 00 00 00 00 00 00  18 00 00 00 00 00 00 00
                           20 00 00 00 00 00 00 00  28 00 00 00 2c 00 00 00
                           30 00 00 00 00 00 00 00  38 00 00 00 00 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Assert.AreEqual(64, str.Position);
        Assert.AreEqual(8, c.GetStructAlignmentInBytes("test"));
        Assert.AreEqual(64, c.GetStructSizeInBytes("test"));
        Assert.AreEqual(0x00U, (uint)result.test.a);
        Assert.IsTrue(
                      ((List<object>)result.test.b).Select(o => (ulong)o).
                                                    ToArray().
                                                    SequenceEqual(new ulong[] { 0x08, 0x10, 0x18, 0x20, }));
        Assert.AreEqual(0x28U, (uint)result.test.c);
        Assert.IsTrue(
                      ((List<object>)result.test.d).Select(o => (uint)o).
                                                    ToArray().
                                                    SequenceEqual(new uint[] { 0x2C, 0x30, }));
        Assert.AreEqual(0x38U, (uint)result.test.e);
    }

    /// <summary>
    ///     Checks bitfields under aligned layout when base integer widths change between groups. It verifies how C-style
    ///     packing interacts with alignment boundaries and subsequent scalar fields.
    /// </summary>
    [TestMethod]
    public void TestAlignBitField()
    {
        const string d = """
                         struct test {
                             uint16  a:4;    // 0x00 - 2b      [0, 1] 
                             uint16  b:4;    //  None- 2b      [0, 1]
                             uint64  c:4;    // 0x08 - 8b      [8, 9, 10, 11,  12, 13, 14, 15]
                             uint64  d:4;    //  None- 8b      [8, 9, 10, 11,  12, 13, 14, 15]
                             uint16  e;      // 0x10 - 2b - 16 [16, 17]
                             uint32  f:4;    // 0x14 - 4b - 20 [20, 21, 22, 23]
                             uint64  g;      // 0x18 - 8b - 24 [24, 25, 26, 27,  28, 29, 30, 31]
                         };
                         """;

        const string buf = """
                           12 00 00 00 00 00 00 00  12 00 00 00 00 00 00 00
                           10 00 00 00 02 00 00 00  18 00 00 00 00 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Assert.AreEqual((ushort)0b10, (ushort)result.test.a);
        Assert.AreEqual((ushort)0b01, (ushort)result.test.b);
        Assert.AreEqual(0b10UL, (ulong)result.test.c);
        Assert.AreEqual(0b01UL, (ulong)result.test.d);
        Assert.AreEqual((ushort)0x10, (ushort)result.test.e);
        Assert.AreEqual(0b10U, (uint)result.test.f);
        Assert.AreEqual(0x18UL, (ulong)result.test.g);
    }

    /// <summary>
    ///     Dynamic arrays sized by earlier fields are common in C-derived record formats. This test verifies that
    ///     variable-length segments still honor alignment padding before later members.
    /// </summary>
    [TestMethod]
    public void TestAlignDynamic()
    {
        const string d = """
                         struct test {
                             uint8   a;      // 0x00 (value is 6 in test case)
                             uint16  b[a];   // 0x02
                             uint32  c;      // 0x?? (0x10 in test case)
                             uint64  d;      // 0x?? (0x18 in test case)
                             uint8   e;      // 0x?? (0x20, value is 2 in test case)
                             uint32  f[e];   // 0x?? (0x24 in test case)
                             uint64  g;      // 0x?? (0x30 in test case)
                         };
                         """;

        const string buf = """
                           06 00 02 00 04 00 06 00  08 00 0a 00 0c 00 00 00
                           10 00 00 00 00 00 00 00  18 00 00 00 00 00 00 00
                           02 00 00 00 24 00 00 00  28 00 00 00 00 00 00 00
                           30 00 00 00 00 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Assert.AreEqual(0x06, (byte)result.test.a);
        Assert.IsTrue(
                      ((List<object>)result.test.b).Select(o => (ushort)o).
                                                    ToArray().
                                                    SequenceEqual(
                                                                  new ushort[]
                                                                  {
                                                                      0x02, 0x04, 0x06, 0x08, 0x0A, 0x0C,
                                                                  }));
        Assert.AreEqual(0x10, (int)result.test.c);
        Assert.AreEqual(0x18, (long)result.test.d);
        Assert.AreEqual(0x02, (byte)result.test.e);
        Assert.IsTrue(
                      ((List<object>)result.test.f).Select(o => (uint)o).
                                                    ToArray().
                                                    SequenceEqual(new uint[] { 0x24, 0x28, }));
        Assert.AreEqual(0x30, (long)result.test.g);
    }

    /// <summary>
    ///     Nested structs carry their own alignment requirements into the parent struct layout. This test validates offset
    ///     propagation across parent and child boundaries.
    /// </summary>
    [TestMethod]
    public void TestAlignNestedStruct()
    {
        const string d = """
                         struct test {
                             uint32  a;      // 0x00
                             struct {
                                 uint64  b;  // 0x08 8 
                                 uint32  c;  // 0x10 16 
                             } nested;
                             uint64  d;      // 0x18 24
                         };
                         """;

        const string buf = """
                           00 00 00 00 00 00 00 00  08 00 00 00 00 00 00 00
                           10 00 00 00 00 00 00 00  18 00 00 00 00 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Assert.AreEqual(0x00, (int)result.test.a);
        Assert.AreEqual(0x08, (byte)result.test.nested.b);
        Assert.AreEqual(0x10, (byte)result.test.nested.c);
        Assert.AreEqual(0x18, (byte)result.test.d);
    }

    /// <summary>
    ///     Pointer fields in C are alignment-sensitive and often larger than nearby scalars. This test verifies pointer
    ///     placement and resulting offsets for following members.
    /// </summary>
    [TestMethod]
    public void TestAlignPointer()
    {
        const string d = """
                         struct test {
                             uint32  a; // 0x00
                             uint32  *b; // 0x08
                             uint16  c; // 0x10 (16)
                             uint16  d; // 0x12 (18)
                         };
                         """;

        const string buf = """
                           00 00 00 00 00 00 00 00  18 00 00 00 00 00 00 00
                           10 00 12 00 00 00 00 00  18 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Pointer ptr = result.test.b;

        Assert.AreEqual(0x00U, (uint)result.test.a);
        Assert.AreEqual(0x18U, (uint)ptr.Address);
        Assert.AreEqual(0x18U, (uint)ptr.Value!);
        Assert.AreEqual((ushort)0x10, (ushort)result.test.c);
        Assert.AreEqual((ushort)0x12, (ushort)result.test.d);
    }

    /// <summary>
    ///     Provides a baseline mixed-width aligned struct to validate per-field padding and final struct tail alignment. These
    ///     checks reflect the core C ABI layout contract.
    /// </summary>
    [TestMethod]
    public void TestAlignStruct()
    {
        const string d = """
                         struct test {
                             uint32  a;  // 0x00 align 4 0+0 -> 0 ( 0%4 == 0 )
                             uint64  b;  // 0x08 align 8 (4+4) -> 8 ( 4%8 == 4 )
                             uint16  c;  // 0x10 align 2 16+0 -> 16 ( %2 == 0 )
                             uint32  d;  // 0x14 align 4 18+2 -> 20 ( 18%4 == 2 )
                             uint8   e;  // 0x18 align 1 24+0 -> 24 ( 24%1 == 0 )
                             uint16  f;  // 0x1a align 2 25+1 -> 26 ( 25%2 == 1 )
                         };
                         """;

        const string buf = """
                               00 00 00 00 00 00 00 00  08 00 00 00 00 00 00 00
                               10 00 00 00 14 00 00 00  18 00 1a 00 00 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        Assert.AreEqual(32, str.Position);
        Assert.AreEqual(8, c.GetStructAlignmentInBytes("test"));
        Assert.AreEqual(32, c.GetStructSizeInBytes("test"));
        Assert.AreEqual(0x00, debug[0].CurPos);
        Assert.AreEqual(0x08, debug[1].CurPos);
        Assert.AreEqual(0x10, debug[2].CurPos);
        Assert.AreEqual(0x14, debug[3].CurPos);
        Assert.AreEqual(0x18, debug[4].CurPos);
        Assert.AreEqual(0x1A, debug[5].CurPos);
    }

    /// <summary>
    ///     Arrays of structs repeat each element at the struct stride, including internal and tail padding. This test confirms
    ///     aligned element-to-element spacing is preserved.
    /// </summary>
    [TestMethod]
    public void TestAlignStructArray()
    {
        const string d = """
                         struct test {
                             uint32  a;      // 0x00
                             uint64  b;      // 0x08
                         };

                         struct array {
                             test    a[4];
                         };
                         """;

        const string buf = """
                           00 00 00 00 00 00 00 00  08 00 00 00 00 00 00 00
                           10 00 00 00 00 00 00 00  18 00 00 00 00 00 00 00
                           20 00 00 00 00 00 00 00  28 00 00 00 00 00 00 00
                           30 00 00 00 00 00 00 00  38 00 00 00 00 00 00 00
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "array");

        dynamic result = obj;

        Assert.AreEqual(0x00, (int)result.array.a[0].a);
        Assert.AreEqual(0x08, (long)result.array.a[0].b);
        Assert.AreEqual(0x10, (int)result.array.a[1].a);
        Assert.AreEqual(0x18, (long)result.array.a[1].b);
        Assert.AreEqual(0x20, (int)result.array.a[2].a);
        Assert.AreEqual(0x28, (long)result.array.a[2].b);
        Assert.AreEqual(0x30, (int)result.array.a[3].a);
        Assert.AreEqual(0x38, (long)result.array.a[3].b);
    }

    /// <summary>
    ///     Contrasts simple union sizing by using a larger array member that extends beyond another member width. It verifies
    ///     union total size keeps the full largest-member footprint, including tail bytes.
    /// </summary>
    [TestMethod]
    public void UnionTail()
    {
        const string d = """
                         union test {
                             uint64  a;
                             uint32  b[3];
                         };
                         """;

        const string buf = """
                               00 00 00 01 00 00 00 02 00 00 00 03 00 00 00 04
                           """;

        var c = new CStruct(d, aligned: true);
        byte[]? bufBytes = buf.ParseHexDataContent();
        var str = new MemoryStream(bufBytes);
        (List<DebugData>? debug, dynamic obj) = c.ParseStreamWithDebug(str, "test");

        dynamic result = obj;

        Assert.AreEqual(16, str.Position);
        Assert.AreEqual(8, c.GetStructAlignmentInBytes("test"));
        Assert.AreEqual(16, c.GetStructSizeInBytes("test"));
        Assert.AreEqual(0x0200000001000000, (long)result.a);
        Assert.AreEqual(0x01000000, (long)result.b[0]);
        Assert.AreEqual(0x02000000, (long)result.b[1]);
        Assert.AreEqual(0x03000000, (long)result.b[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => result.b[3]);
    }

    /*
def test_align_pointer():
    d = """
    struct test {
        uint32  a;
        uint32  *b;
        uint16  c;
        uint16  d;
    };
    """
    c = cstruct.cstruct(pointer="uint64")
    c.load(d, align=True)

    assert c.pointer is c.uint64

    fields = c.test.fields
    assert c.test.align
    assert c.test.alignment == 8
    assert c.test.size == 24
    assert fields[0].offset == 0x00
    assert fields[1].offset == 0x08
    assert fields[2].offset == 0x10
    assert fields[3].offset == 0x12

    buf = """
        00 00 00 00 00 00 00 00  18 00 00 00 00 00 00 00
        10 00 12 00 00 00 00 00  18 00 00 00
    """
    buf = bytes.fromhex(buf)
    obj = c.test(buf)

    assert obj.a == 0x00
    assert obj.b.dereference() == 0x18
    assert obj.c == 0x10
    assert obj.d == 0x12

    assert obj.dumps() == buf[:-4]  # Without pointer value
     */
}
