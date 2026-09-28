namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Values;

/// <summary>
///     Verifies <c>@count(N)</c> pointer targets and the rule behind them: a struct follows its pointers after its
///     last field, so a count can name a field declared after the pointer.
/// </summary>
[TestClass]
public class CountedPointerTests
{
    // The PKCS#11 CK_GCM_MESSAGE_PARAMS shape: both lengths are declared after their pointers, and the tag length
    // is stored in bits.
    private const string GcmParams = """
        typedef uint8 *CK_BYTE_PTR;
        typedef uint32 CK_ULONG;
        struct params {
            CK_BYTE_PTR pIv @count(ulIvLen);
            CK_ULONG ulIvLen;
            CK_ULONG ulIvFixedBits;
            CK_ULONG ivGenerator;
            CK_BYTE_PTR pTag @count(ulTagBits / 8);
            CK_ULONG ulTagBits;
        };
        """;

    /// <summary>A count declared after its pointer, through a pointer typedef, reads the counted bytes.</summary>
    [TestMethod]
    public void CountNamedAfterThePointer_ReadsTheCountedElements()
    {
        var layout = new CStruct(GcmParams, pointerSize: 4);
        byte[] bytes =
        [
            0x18, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x1B, 0, 0, 0, 16, 0, 0, 0,
            0xA1, 0xA2, 0xA3, 0xB1, 0xB2,
        ];

        StructValue value = layout.Parse(bytes, "params");

        Pointer iv = value.Get<Pointer>("pIv");
        Assert.IsTrue(iv.IsDereferenced);
        Assert.AreEqual(0x18L, iv.Address);
        CollectionAssert.AreEqual(new object[] { (byte)0xA1, (byte)0xA2, (byte)0xA3, }, ((IEnumerable<object?>)iv.Value!).ToArray());
        CollectionAssert.AreEqual(new object[] { (byte)0xB1, (byte)0xB2, }, ((IEnumerable<object?>)value.Get<Pointer>("pTag").Value!).ToArray());
        Assert.AreEqual(16u, value.Get<uint>("ulTagBits"));
    }

    /// <summary>Struct elements, character text, an earlier count, and a literal count each read as the element type dictates.</summary>
    [TestMethod]
    public void CountedTargets_UseTheirElementTypes()
    {
        var structs = new CStruct("struct blob { uint16 a; uint16 b; }; struct rec { blob *items @count(n); uint16 n; };", pointerSize: 2);
        var items = (List<object?>)structs.Parse(new byte[] { 4, 0, 2, 0, 1, 0, 2, 0, 3, 0, 4, 0, }, "rec").Get<Pointer>("items").Value!;
        Assert.HasCount(2, items);
        Assert.AreEqual((ushort)3, ((StructValue)items[1]!).Get<ushort>("a"));

        var text = new CStruct("struct rec { uint8 len; char *name @count(len); };", pointerSize: 1);
        Assert.AreEqual("abc", text.Parse(new byte[] { 3, 2, (byte)'a', (byte)'b', (byte)'c', }, "rec").Get<Pointer>("name").Value);

        var literal = new CStruct("struct rec { uint16 *pair @count(2); };", pointerSize: 1);
        CollectionAssert.AreEqual(new object[] { (ushort)0x1234, (ushort)0x5678, }, ((IEnumerable<object?>)literal.Parse(new byte[] { 1, 0x34, 0x12, 0x78, 0x56, }, "rec").Get<Pointer>("pair").Value!).ToArray());
    }

    /// <summary>A null counted pointer has no target, whatever its count.</summary>
    [TestMethod]
    public void NullCountedPointer_IsNotFollowed()
    {
        var layout = new CStruct("struct rec { uint8 *iv @count(n); uint8 n; };", pointerSize: 1);

        Pointer iv = layout.Parse(new byte[] { 0, 5, }, "rec").Get<Pointer>("iv");

        Assert.IsTrue(iv.IsNull);
        Assert.IsFalse(iv.IsDereferenced);
    }

    /// <summary>A negative count, a count over the array limit, and a target over the byte budget all fail before the target is read.</summary>
    [TestMethod]
    public void CountedTargets_ApplyTheArrayAndTargetLimits()
    {
        var signed = new CStruct("struct rec { uint8 *iv @count(n); int8 n; };", pointerSize: 1);
        CStructReadException negative = Assert.Throws<CStructReadException>(() => signed.Parse(new byte[] { 2, 0xFF, 0, }, "rec"));
        StringAssert.StartsWith(negative.Message, "Array length cannot be negative: iv (field 'iv' (uint8)");

        var layout = new CStruct("struct rec { uint16 *v @count(n); uint8 n; };", pointerSize: 1);
        byte[] bytes = [2, 3, 1, 0, 2, 0, 3, 0,];
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes, "rec", options: new ReadOptions { MaxArrayElements = 2, }));
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes, "rec", options: new ReadOptions { MaxPointerTargetBytes = 5, }));
        Assert.HasCount(3, (IEnumerable<object?>)layout.Parse(bytes, "rec", options: new ReadOptions { MaxPointerTargetBytes = 6, }).Get<Pointer>("v").Value!);
    }

    /// <summary>Following after the struct means a truncated later field is reported before a dangling pointer.</summary>
    [TestMethod]
    public void TruncatedLaterField_IsReportedBeforeADanglingPointer()
    {
        var layout = new CStruct("struct rec { uint8 *p; uint32 tail; };", pointerSize: 1);

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 200, 1, 2, }, "rec"));

        StringAssert.Contains(exception.Message, "field 'tail'");
    }

    /// <summary>A pointer's debug record holds the followed pointer, and its target's records follow the struct's own fields.</summary>
    [TestMethod]
    public void DebugRecords_ListTargetsAfterTheStructFields()
    {
        var layout = new CStruct("struct box { uint8 v; }; struct rec { box *p; uint8 tail; };", pointerSize: 1);

        ParseResult result = layout.ParseWithDebug(new byte[] { 2, 7, 9, }, "rec");

        CollectionAssert.AreEqual(new[] { "rec.p", "rec.tail", "rec.p.v", }, result.Debug.Select(record => record.Path).ToArray());
        Assert.IsTrue(((Pointer)result.Debug[0].Value!).IsDereferenced);
    }

    /// <summary>Each struct element of a counted target reports its own indexed debug path after the struct's fields.</summary>
    [TestMethod]
    public void DebugRecords_IndexCountedStructElements()
    {
        var layout = new CStruct("struct blob { uint8 a; }; struct rec { blob *items @count(n); uint8 n; };", pointerSize: 1);

        ParseResult result = layout.ParseWithDebug(new byte[] { 2, 2, 7, 8, }, "rec");

        CollectionAssert.AreEqual(new[] { "rec.items", "rec.n", "rec.items[0].a", "rec.items[1].a", }, result.Debug.Select(record => record.Path).ToArray());
        CollectionAssert.AreEqual(new long[] { 0, 1, 2, 3, }, result.Debug.Select(record => record.Start).ToArray());
    }

    /// <summary>
    ///     Writing stores a counted pointer's address, as for any pointer: serialization and writes never place a
    ///     target, and an update changes the stored address.
    /// </summary>
    [TestMethod]
    public void SerializeWriteAndUpdate_StoreTheAddressOnly()
    {
        var layout = new CStruct("struct rec { uint8 *iv @count(n); uint8 n; uint8 data[2]; };", pointerSize: 1);
        byte[] bytes = [2, 2, 0xAA, 0xBB,];
        StructValue parsed = layout.Parse(bytes, "rec");

        CollectionAssert.AreEqual(bytes, layout.Serialize("rec", parsed));

        using var output = new MemoryStream();
        layout.Write(output, "rec", parsed);
        CollectionAssert.AreEqual(bytes, output.ToArray());

        using var existing = new MemoryStream((byte[])bytes.Clone());
        layout.Update(existing, "rec.iv.address", 3L);
        CollectionAssert.AreEqual(new byte[] { 3, 2, 0xAA, 0xBB, }, existing.ToArray());
    }

    /// <summary>The address of a counted pointer is a path; its target is read with the containing struct.</summary>
    [TestMethod]
    public void CountedTargetPaths_AreRejectedButAddressesResolve()
    {
        var layout = new CStruct("struct rec { uint8 *iv @count(n); uint8 n; };", pointerSize: 1);
        byte[] bytes = [2, 1, 0xAA,];

        Assert.AreEqual(2L, layout.ReadValue(bytes, "rec.iv.address"));
        CStructPathException exception = Assert.Throws<CStructPathException>(() => layout.ReadValue(bytes, "rec.iv.value"));
        StringAssert.Contains(exception.Message, "@count pointer");
    }

    /// <summary>The count is part of the layout: it survives rendering and is refused where it cannot apply.</summary>
    [TestMethod]
    public void CountSuffix_IsRenderedAndValidated()
    {
        StringAssert.Contains(new CStruct("struct rec { uint8 *iv @count(n + 1); uint8 n; };").ToDefinition(), "uint8 *iv @count(n + 1);");

        Assert.Throws<CStructLayoutException>(() => new CStruct("struct rec { uint8 iv @count(2); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct rec { uint8 *iv[2] @count(2); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct rec { void *iv @count(2); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct rec { uint8 *iv @count(2) @count(3); };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct rec { uint8 *iv @count(EOF); };"));
    }

    /// <summary>The memory API projects one value per pointer, so it refuses a counted pointer instead of dropping its count.</summary>
    [TestMethod]
    public void MemorySchema_RejectsCountedPointers()
    {
        var layout = new CStruct("struct rec { uint8 *iv @count(2); uint8 n; };", pointerSize: 8);

        Assert.Throws<CStructLayoutException>(() => PortableMemorySchema.Create(layout, "rec"));
    }
}
