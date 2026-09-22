namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks implicit padding values, recursive promoted-union selection and expression diagnostics during writing.</summary>
[TestClass]
public class WriterPaddingAndPromotionTests
{
    /// <summary>Runtime-sized nested records publish full dotted count paths through the general writer.</summary>
    /// <param name="enumCount">Whether the count is a layout enum rather than an ordinary byte.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DynamicNestedRecords_PublishTheCompleteQualifiedPath(bool enumCount)
    {
        string countType = enumCount ? "kind" : "uint8";
        var layout = new CStruct("enum kind : uint8 { TWO = 2 }; struct inner { " + countType + " count; uint8 padding[count]; }; struct outer { inner child; }; struct root { outer header; uint8 values[header.child.count]; uint8 tail; };");
        var inner = new Dictionary<string, object?> { ["count"] = (byte)2, ["padding"] = new byte[] { 31, 32, }, };
        var data = new Dictionary<string, object?>
        {
            ["header"] = new Dictionary<string, object?> { ["child"] = inner, },
            ["values"] = new byte[] { 41, 42, },
            ["tail"] = (byte)99,
        };
        CollectionAssert.AreEqual(new byte[] { 2, 31, 32, 41, 42, 99, }, layout.Serialize("root", data));
    }

    /// <summary>Unnamed fixed arrays write zero elements without requiring any caller member.</summary>
    /// <param name="type">Numeric or character storage type used for padding.</param>
    /// <param name="paddingBytes">The three elements' complete byte extent.</param>
    [TestMethod]
    [DataRow("uint8", 3)]
    [DataRow("uint16", 6)]
    [DataRow("char", 3)]
    [DataRow("wchar", 6)]
    public void UnnamedFixedArray_WritesEveryZeroElement(string type, int paddingBytes)
    {
        var layout = new CStruct("struct root { uint8 prefix; " + type + " _[3]; uint8 tail; };");
        var data = new Dictionary<string, object?> { ["prefix"] = (byte)9, ["tail"] = (byte)7, };
        byte[] expected = new byte[paddingBytes + 2];
        expected[0] = 9;
        expected[^1] = 7;
        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
    }

    /// <summary>Runtime-sized records also synthesize every numeric and character padding element without caller values.</summary>
    /// <param name="type">Numeric or character storage type used for padding.</param>
    /// <param name="paddingBytes">The three padding elements' complete byte extent.</param>
    [TestMethod]
    [DataRow("uint8", 3)]
    [DataRow("uint16", 6)]
    [DataRow("char", 3)]
    [DataRow("wchar", 6)]
    public void DynamicRecordPadding_WritesEveryZeroElement(string type, int paddingBytes)
    {
        var layout = new CStruct("struct root { uint8 count; uint8 values[count]; " + type + " _[3]; uint8 tail; };");
        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)1,
            ["values"] = new byte[] { 9, },
            ["tail"] = (byte)7,
        };
        byte[] expected = new byte[paddingBytes + 3];
        expected[0] = 1;
        expected[1] = 9;
        expected[^1] = 7;
        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
    }

    /// <summary>Nested anonymous members are selected only when one of their transitive leaves is supplied.</summary>
    /// <param name="nested">Whether to supply the wider nested view as well as the small view.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PromotedUnion_RecognizesTransitiveSuppliedLeaves(bool nested)
    {
        var layout = new CStruct("struct root { uint8 prefix; union { struct { struct { uint16 first; }; uint8 second; }; uint8 small; }; uint8 tail; };");
        var data = new Dictionary<string, object?> { ["prefix"] = (byte)9, ["small"] = (byte)7, ["tail"] = (byte)8, };
        if (nested)
        {
            data["first"] = (ushort)0x1234;
            data["second"] = (byte)0x56;
        }

        byte[] expected = nested ? [9, 0x34, 0x12, 0x56, 8,] : [9, 7, 0, 0, 8,];
        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
    }

    /// <summary>A supplied inactive member identifies the rejected field rather than silently discarding its value.</summary>
    [TestMethod]
    public void InactiveConditionalMember_IsNamedInTheFailure()
    {
        var layout = new CStruct("struct root { uint8 flag; if (flag) { uint8 value; } };");
        var data = new Dictionary<string, object?> { ["flag"] = (byte)0, ["value"] = (byte)1, };

        // Inactive storage cannot receive a supplied value, even under the default unknown-member policy.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", data));
        StringAssert.StartsWith(failure.Message, "Inactive conditional field supplied: value");
    }

    /// <summary>A missing runtime array-count variable reports the field whose length could not be evaluated.</summary>
    [TestMethod]
    public void MissingArrayVariable_IdentifiesTheCountContext()
    {
        var layout = new CStruct("struct root { uint8 values[count]; };");
        var data = new Dictionary<string, object?> { ["values"] = new byte[] { 1, }, };

        // The caller omitted count; the diagnostic must explain which expression needed it.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", data));
        StringAssert.StartsWith(failure.Message, "Cannot evaluate array length for values:");
        StringAssert.Contains(failure.Message, "count");
    }
}
