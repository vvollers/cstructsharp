namespace CStructSharp.Tests;

using System.Collections;
using System.Dynamic;

/// <summary>Verifies that one shared write policy bounds strings, output, nesting, and collection materialization.</summary>
[TestClass]
public class WriteBudgetTests
{
    /// <summary>
    ///     Negative string or total byte budgets and zero nesting depth are invalid options.
    /// </summary>
    /// <remarks>
    ///     Writing or updating a one-byte record must reject them before replacing the original 0xA5. The unchanged
    ///     position also shows that configuration validation happens before output work.
    /// </remarks>
    [TestMethod]
    public void InvalidWriteBudgets_AreRejectedBeforeOutput()
    {
        var cstruct = new CStruct("struct root { uint8 value; };", pointerSize: 1);
        var invalidOptions = new WriteOptions[]
        {
            new() { MaxStringBytes = -1, },
            new() { MaxTotalBytesWritten = -1, },
            new() { MaxNestingDepth = 0, },
        };

        foreach (WriteOptions options in invalidOptions)
        {
            using var stream = new MemoryStream([0xA5,]) { Position = 0, };
            Assert.Throws<ArgumentOutOfRangeException>(
                () => cstruct.WriteStream(
                    stream,
                    "root",
                    new Dictionary<string, object> { ["value"] = (byte)0x11, },
                    options: options));
            CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
            Assert.AreEqual(0L, stream.Position);
        }

        using var updateStream = new MemoryStream([0xA5,]) { Position = 0, };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cstruct.UpdateStream(
                updateStream,
                "root.value",
                (byte)0x11,
                options: new UpdateOptions { MaxTotalBytesWritten = -1, }));
        CollectionAssert.AreEqual(new byte[] { 0xA5, }, updateStream.ToArray());
        Assert.AreEqual(0L, updateStream.Position);
    }

    /// <summary>
    ///     Each row gives text and its complete encoded length, including the terminator.
    /// </summary>
    /// <remarks>
    ///     ASCII AB needs three bytes, UTF-8 é also needs three, and UTF-16 A needs four. A budget one byte below that
    ///     size must fail; the exact size must succeed.
    /// </remarks>
    /// <param name="typeName">The terminated string codec under test.</param>
    /// <param name="value">A value with a known encoded size.</param>
    /// <param name="encodedBytes">The payload size including the terminator.</param>
    [TestMethod]
    [DataRow("ascii_string_zero", "AB", 3)]
    [DataRow("ascii_string_newline", "AB", 3)]
    [DataRow("utf8_string_zero", "é", 3)]
    [DataRow("unicode_string_zero<", "A", 4)]
    [DataRow("unicode_string_newline>", "A", 4)]
    [DataRow("string", "A", 4)]
    public void TerminatedStrings_EnforceExactEncodedByteBudget(
        string typeName,
        string value,
        int encodedBytes)
    {
        var cstruct = new CStruct(
            $"struct root {{ {typeName} value; }};",
            pointerSize: 1,
            isLittleEndian: true);
        var data = new Dictionary<string, object> { ["value"] = value, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxStringBytes = encodedBytes - 1, }));

        byte[] bytes = cstruct.Serialize(
            "root",
            data,
            options: new WriteOptions { MaxStringBytes = encodedBytes, });
        Assert.AreEqual(encodedBytes, bytes.Length);
    }

    /// <summary>
    ///     value[2] reserves two character units even when the supplied text is only A.
    /// </summary>
    /// <remarks>
    ///     Narrow storage therefore needs two bytes and wide storage four, including padding. The string budget must
    ///     cover the entire fixed buffer rather than only the nonzero text bytes.
    /// </remarks>
    /// <param name="typeName">The fixed character codec.</param>
    /// <param name="encodedBytes">The complete padded buffer size.</param>
    [TestMethod]
    [DataRow("char", 2)]
    [DataRow("wchar>", 4)]
    [DataRow("wchar<", 4)]
    public void FixedCharacterBuffers_EnforcePaddedEncodedByteBudget(string typeName, int encodedBytes)
    {
        var cstruct = new CStruct($"struct root {{ {typeName} value[2]; }};", pointerSize: 1);
        var data = new Dictionary<string, object> { ["value"] = "A", };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxStringBytes = encodedBytes - 1, }));

        byte[] exactBytes = cstruct.Serialize(
            "root",
            data,
            options: new WriteOptions { MaxStringBytes = encodedBytes, });
        Assert.AreEqual(encodedBytes, exactBytes.Length);
    }

    /// <summary>
    ///     The strings A and B each need two bytes with their zero terminators.
    /// </summary>
    /// <remarks>
    ///     A per-string limit of two allows both fields, but the operation's total must allow four. This distinguishes
    ///     a limit checked separately for each field from a budget shared by the whole write.
    /// </remarks>
    [TestMethod]
    public void StringBudget_ResetsPerField_WhileTotalBudgetAccumulates()
    {
        var cstruct = new CStruct("struct root { cstring first; cstring second; };", pointerSize: 1);
        var data = new Dictionary<string, object> { ["first"] = "A", ["second"] = "B", };
        var exact = new WriteOptions { MaxStringBytes = 2, MaxTotalBytesWritten = 4, };

        CollectionAssert.AreEqual(
            new byte[] { (byte)'A', 0x00, (byte)'B', 0x00, },
            cstruct.Serialize("root", data, options: exact));
        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxStringBytes = 2, MaxTotalBytesWritten = 3, }));
    }

    /// <summary>
    ///     low = 5 and high = 10 share the final byte 0xA5.
    /// </summary>
    /// <remarks>
    ///     Writing the two slices updates that storage more than once, so a one-byte total budget is insufficient
    ///     despite the one-byte output size. The larger accepted budget verifies that actual writes, including
    ///     rewrites, are counted.
    /// </remarks>
    [TestMethod]
    public void TotalBudget_CountsPhysicalBitfieldRewrites()
    {
        var cstruct = new CStruct("struct root { uint8 low:4; uint8 high:4; };", pointerSize: 1);
        var data = new Dictionary<string, object> { ["low"] = 5, ["high"] = 10, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxTotalBytesWritten = 1, }));
        CollectionAssert.AreEqual(
            new byte[] { 0xA5, },
            cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxTotalBytesWritten = 2, }));
    }

    /// <summary>
    ///     A byte followed by an aligned uint32 occupies eight bytes after padding.
    /// </summary>
    /// <remarks>
    ///     The writer must charge newly created gaps as part of output extent, so seeking over padding cannot evade the
    ///     budget. A failed direct write can retain its earlier field; an adequate budget produces all eight bytes.
    /// </remarks>
    [TestMethod]
    public void TotalBudget_ChargesNewAlignedOutputExtent()
    {
        var cstruct = new CStruct(
            "struct root { uint8 first; uint32 second; };",
            pointerSize: 1,
            aligned: true);
        var data = new Dictionary<string, object> { ["first"] = (byte)1, ["second"] = 2U, };
        using var limited = new MemoryStream();

        Assert.Throws<CStructWriteException>(
            () => cstruct.WriteStream(
                limited,
                "root",
                data,
                options: new WriteOptions { MaxTotalBytesWritten = 7, }));
        CollectionAssert.AreEqual(new byte[] { 0x01, }, limited.ToArray());
        Assert.AreEqual(4L, limited.Position);

        byte[] exactBytes = cstruct.Serialize(
            "root",
            data,
            options: new WriteOptions { MaxTotalBytesWritten = 8, });
        Assert.AreEqual(8, exactBytes.Length);
    }

    /// <summary>
    ///     Selecting a small union member still reserves the wider member's storage, and aligned structs may need
    ///     padding after their last field.
    /// </summary>
    /// <remarks>
    ///     Both kinds of extra bytes must count toward the total write budget. Limits below the required storage fail;
    ///     adequate limits produce the full declared extent.
    /// </remarks>
    [TestMethod]
    public void TotalBudget_ChargesUnionReservationAndStructTailPadding()
    {
        var union = new CStruct("union root { uint32 wide; uint8 small; };", pointerSize: 1);
        UnionValue unionData = UnionValue.FromMember("root", "small", (byte)1);

        Assert.Throws<CStructWriteException>(
            () => union.Serialize(
                "root",
                unionData,
                options: new WriteOptions { MaxTotalBytesWritten = 3, }));
        CollectionAssert.AreEqual(
            new byte[] { 1, 0, 0, 0, },
            union.Serialize(
                "root",
                unionData,
                options: new WriteOptions { MaxTotalBytesWritten = 4, }));

        var aligned = new CStruct(
            "struct root { uint32 first; uint8 last; };",
            pointerSize: 1,
            aligned: true);
        var alignedData = new Dictionary<string, object> { ["first"] = 1U, ["last"] = (byte)2, };

        Assert.Throws<CStructWriteException>(
            () => aligned.Serialize(
                "root",
                alignedData,
                options: new WriteOptions { MaxTotalBytesWritten = 7, }));
        byte[] alignedBytes = aligned.Serialize(
            "root",
            alignedData,
            options: new WriteOptions { MaxTotalBytesWritten = 8, });
        Assert.AreEqual(8, alignedBytes.Length);
    }

    /// <summary>
    ///     With room for only one write, first becomes 0x11 but writing second must fail.
    /// </summary>
    /// <remarks>
    ///     The destination is then 11 A5 and the position is 1. This deliberately documents that direct WriteStream can
    ///     leave earlier fields written; it does not provide the staged validation behavior of UpdateStream.
    /// </remarks>
    [TestMethod]
    public void DirectWrite_TotalBudgetNeverExceedsLimit_ButMayLeaveEarlierFields()
    {
        var cstruct = new CStruct("struct root { uint8 first; uint8 second; };", pointerSize: 1);
        var data = new Dictionary<string, object> { ["first"] = (byte)0x11, ["second"] = (byte)0x22, };
        using var stream = new MemoryStream([0xA5, 0xA5,]);

        Assert.Throws<CStructWriteException>(
            () => cstruct.WriteStream(
                stream,
                "root",
                data,
                options: new WriteOptions { MaxTotalBytesWritten = 1, }));
        CollectionAssert.AreEqual(new byte[] { 0x11, 0xA5, }, stream.ToArray());
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>
    ///     The helper tries updates to an ordinary scalar, a bitfield, a pointer address, and a selected union under a
    ///     zero write budget.
    /// </summary>
    /// <remarks>
    ///     Every path must reject output and preserve the existing bytes. Specialized writers must not bypass the
    ///     shared budget just because they handle storage differently.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_TotalBudgetCoversEveryWriteDispatch()
    {
        AssertUpdateRejectedWithoutMutation(
            "struct root { uint16 value; };",
            new byte[] { 0x11, 0x11, },
            "root.value",
            (ushort)0xBEEF);
        AssertUpdateRejectedWithoutMutation(
            "struct root { uint8 low:4; uint8 high:4; };",
            new byte[] { 0xA5, },
            "root.high",
            3);
        AssertUpdateRejectedWithoutMutation(
            "struct root { uint8 *value; };",
            new byte[] { 0x01, 0xA5, },
            "root.value.address",
            0);

        UnionValue unionValue = UnionValue.FromMember("root", "small", (byte)0x11);
        AssertUpdateRejectedWithoutMutation(
            "union root { uint16 wide; uint8 small; };",
            new byte[] { 0x34, 0x12, },
            "root",
            unionValue);
    }

    /// <summary>
    ///     Two pointer hops lead to the existing string old.
    /// </summary>
    /// <remarks>
    ///     Replacing it with hi still needs space for its terminator and must obey the configured string budget. A
    ///     rejected replacement must preserve the pointer chain, old text, and the caller's starting position of 1.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_PointerTargetStringUsesSharedStringBudget()
    {
        var cstruct = new CStruct("struct root { char **name; };", pointerSize: 1);
        byte[] original = [0xEE, 0x03, 0xA5, 0x05, 0xA5, (byte)'o', (byte)'l', (byte)'d', 0x00,];
        using var stream = new MemoryStream((byte[])original.Clone()) { Position = 1, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(
                stream,
                "root.name.value.value",
                "hi",
                options: new UpdateOptions { MaxStringBytes = 2, }));
        CollectionAssert.AreEqual(original, stream.ToArray());
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>
    ///     root contains two middle records, each containing a leaf.
    /// </summary>
    /// <remarks>
    ///     A shallow limit must reject this depth, while a sufficient limit writes both siblings. Finished siblings and
    ///     array elements must release their nesting level; total object count is not the same as active nesting depth.
    /// </remarks>
    [TestMethod]
    public void NestingBudget_TracksActiveCompositeDepth()
    {
        const string layout = """
                              struct leaf { uint8 value; };
                              struct middle { leaf child; };
                              struct root { middle first; middle second; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        var data = new Dictionary<string, object>
        {
            ["first"] = CreateMiddle(1),
            ["second"] = CreateMiddle(2),
        };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxNestingDepth = 2, }));
        CollectionAssert.AreEqual(
            new byte[] { 0x01, 0x02, },
            cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxNestingDepth = 3, }));

        var array = new CStruct(
            "struct leaf { uint8 value; }; struct root { leaf values[2]; };",
            pointerSize: 1);
        var arrayData = new Dictionary<string, object>
        {
            ["values"] = new object[]
            {
                new Dictionary<string, object> { ["value"] = (byte)3, },
                new Dictionary<string, object> { ["value"] = (byte)4, },
            },
        };
        CollectionAssert.AreEqual(
            new byte[] { 0x03, 0x04, },
            array.Serialize(
                "root",
                arrayData,
                options: new WriteOptions { MaxNestingDepth = 2, }));
    }

    /// <summary>
    ///     A pointer leads to middle, which contains leaf.
    /// </summary>
    /// <remarks>
    ///     Locating that target and writing its replacement have separate limits. An insufficient write-depth limit
    ///     must preserve the destination; raising it sufficiently must replace the target value with 0x22 while leaving
    ///     the pointer untouched.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_PointerTargetUsesWriteNestingBudget()
    {
        const string layout = """
                              struct leaf { uint8 value; };
                              struct middle { leaf child; };
                              struct root { middle *selected; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        byte[] original = [0x02, 0xA5, 0x11,];
        using var stream = new MemoryStream((byte[])original.Clone());

        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(
                stream,
                "root.selected.value",
                CreateMiddle(0x22),
                options: new UpdateOptions { MaxNestingDepth = 1, }));
        CollectionAssert.AreEqual(original, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);

        cstruct.UpdateStream(
            stream,
            "root.selected.value",
            CreateMiddle(0x22),
            options: new UpdateOptions { MaxNestingDepth = 2, });
        CollectionAssert.AreEqual(new byte[] { 0x02, 0xA5, 0x22, }, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>
    ///     values[2] is supplied by an enumerable that can yield five items.
    /// </summary>
    /// <remarks>
    ///     The writer may read a third item to discover the mismatch, but must stop there and reject it. An exact two-
    ///     item enumerable succeeds, preventing unnecessary or endless enumeration before writing.
    /// </remarks>
    [TestMethod]
    public void RuntimeEnumerableMaterialization_IsBoundedBeforeArrayWrites()
    {
        var cstruct = new CStruct("struct root { uint8 values[2]; };", pointerSize: 1);
        var values = new CountingEnumerable<byte>([1, 2, 3, 4, 5,]);
        var data = new Dictionary<string, object> { ["values"] = values, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxArrayElements = 2, }));
        Assert.AreEqual(3, values.Yielded);

        var exactValues = new CountingEnumerable<byte>([1, 2,]);
        CollectionAssert.AreEqual(
            new byte[] { 0x01, 0x02, },
            cstruct.Serialize(
                "root",
                new Dictionary<string, object> { ["values"] = exactValues, },
                options: new WriteOptions { MaxArrayElements = 2, }));
        Assert.AreEqual(2, exactValues.Yielded);
    }

    /// <summary>
    ///     The fixed char buffer holds two characters, but its source enumerable yields more.
    /// </summary>
    /// <remarks>
    ///     The writer must stop after the third character proves it is too long, rather than first joining the entire
    ///     sequence into a string. No unbounded text materialization is allowed before the size check.
    /// </remarks>
    [TestMethod]
    public void RuntimeCharacterEnumerableMaterialization_IsBoundedBeforeStringWrites()
    {
        var cstruct = new CStruct("struct root { char value[2]; };", pointerSize: 1);
        var characters = new CountingEnumerable<char>(['A', 'B', 'C', 'D', 'E',]);
        var data = new Dictionary<string, object> { ["value"] = characters, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                data,
                options: new WriteOptions { MaxArrayElements = 2, }));
        Assert.AreEqual(3, characters.Yielded);
    }

    /// <summary>
    ///     The layout itself requests three elements while the configured limit allows two.
    /// </summary>
    /// <remarks>
    ///     The writer already knows this cannot succeed, so it must reject the request without asking the source
    ///     enumerable for even its first item. This avoids invoking caller code unnecessarily.
    /// </remarks>
    [TestMethod]
    public void DeclaredArrayLimit_PreventsEnumerableMaterialization()
    {
        var cstruct = new CStruct("struct root { uint8 values[3]; };", pointerSize: 1);
        var values = new CountingEnumerable<byte>([1, 2, 3,]);

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                new Dictionary<string, object> { ["values"] = values, },
                options: new WriteOptions { MaxArrayElements = 2, }));
        Assert.AreEqual(0, values.Yielded);
    }

    /// <summary>Asserts that a zero total budget rejects one update before its first physical write.</summary>
    private static void AssertUpdateRejectedWithoutMutation(
        string layout,
        byte[] original,
        string path,
        object value)
    {
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream((byte[])original.Clone());

        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(
                stream,
                path,
                value,
                options: new UpdateOptions { MaxTotalBytesWritten = 0, }));
        CollectionAssert.AreEqual(original, stream.ToArray(), path);
        Assert.AreEqual(0L, stream.Position, path);
    }

    /// <summary>Creates one middle/leaf object without relying on the production parser.</summary>
    private static Dictionary<string, object> CreateMiddle(byte value)
    {
        return new Dictionary<string, object>
        {
            ["child"] = new Dictionary<string, object> { ["value"] = value, },
        };
    }

    /// <summary>Records how many values a writer consumes from an otherwise ordinary single-pass sequence.</summary>
    /// <typeparam name="T">The sequence item type.</typeparam>
    private sealed class CountingEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        public int Yielded { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (T value in values)
            {
                this.Yielded++;
                yield return value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
