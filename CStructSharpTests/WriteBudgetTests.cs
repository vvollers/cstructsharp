namespace CStructSharp.Tests;

using System.Collections;
using System.Dynamic;

/// <summary>Verifies that one shared write policy bounds strings, output, nesting, and collection materialization.</summary>
[TestClass]
public class WriteBudgetTests
{
    /// <summary>Rejects negative byte budgets and non-positive nesting before any caller-owned bytes change.</summary>
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

    /// <summary>Measures encoded bytes, including each handler's terminator, before allocating or writing its payload.</summary>
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

    /// <summary>Charges the complete padded storage of fixed narrow and wide character buffers as one string field.</summary>
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

    /// <summary>Applies the string limit independently to each field while the total-output budget remains cumulative.</summary>
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

    /// <summary>Counts repeated writes to shared bitfield storage even when the final serialized extent is one byte.</summary>
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

    /// <summary>Charges newly created alignment gaps by output extent so seeking cannot bypass the byte budget.</summary>
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

    /// <summary>Charges zero-filled union reservation and aligned tail storage through the same total budget.</summary>
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

    /// <summary>Stops a direct multi-field write before the byte over budget and documents its current partial-write boundary.</summary>
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

    /// <summary>Covers scalar, bitfield, pointer-address, and union-clear update writes through the shared total budget.</summary>
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

    /// <summary>Applies the encoded-string budget after pointer traversal and restores update position on failure.</summary>
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

    /// <summary>Counts active struct/union recursion and resets depth between sibling objects and array elements.</summary>
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

    /// <summary>Applies write depth to a selected pointer target independently of the already bounded traversal phase.</summary>
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

    /// <summary>Stops arbitrary numeric enumerables after the one extra item needed to prove a count mismatch.</summary>
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

    /// <summary>Bounds character enumerables before joining them into a fixed-buffer string.</summary>
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

    /// <summary>Rejects an oversized declared array before asking a caller enumerable for its first item.</summary>
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
