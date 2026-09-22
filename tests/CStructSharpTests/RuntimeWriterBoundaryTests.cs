namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks writer diagnostics and selected-field operations that ordinary whole-record round trips can miss.</summary>
[TestClass]
public class RuntimeWriterBoundaryTests
{
    /// <summary>Whole-union input failures identify the violated type, storage or member contract.</summary>
    /// <param name="kind">The invalid union value shape.</param>
    /// <param name="reason">The required explanation before diagnostic context.</param>
    [TestMethod]
    [DataRow("plain", "A whole union write requires UnionValue.FromRaw or UnionValue.FromMember: choice")]
    [DataRow("name", "Union value 'other' cannot be written as 'choice'")]
    [DataRow("length", "Raw storage length mismatch for choice: expected 2, got 1")]
    [DataRow("member", "Union 'choice' has no member named 'missing'")]
    public void InvalidUnionValue_ExplainsItsContract(string kind, string reason)
    {
        var layout = new CStruct("union choice { uint16 wide; uint8 small; };");
        object value = kind switch
        {
            "plain" => new Dictionary<string, object?> { ["small"] = (byte)1, },
            "name" => UnionValue.FromRaw("other", new byte[] { 1, 2, }),
            "length" => UnionValue.FromRaw("choice", new byte[] { 1, }),
            _ => UnionValue.FromMember("choice", "missing", (byte)1),
        };

        // The reason must remain useful even though the boundary also attaches path/offset context.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("choice", value));
        StringAssert.StartsWith(failure.Message, reason);
    }

    /// <summary>Anonymous unions select the widest supplied view, clear its unused tail and require at least one view.</summary>
    [TestMethod]
    public void PromotedUnion_SelectsWidestAndExplainsMissingMembers()
    {
        var layout = new CStruct("struct root { uint8 prefix; union { uint8 small; uint32 wide; }; uint8 tail; };");
        var data = new Dictionary<string, object?> { ["prefix"] = (byte)9, ["small"] = (byte)7, ["wide"] = 0x04030201U, ["tail"] = (byte)8, };
        CollectionAssert.AreEqual(new byte[] { 9, 1, 2, 3, 4, 8, }, layout.Serialize("root", data));
        data.Remove("wide");
        CollectionAssert.AreEqual(new byte[] { 9, 7, 0, 0, 0, 8, }, layout.Serialize("root", data));
        data.Remove("small");

        // With neither view present the writer must not silently emit an all-zero union.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", data));
        StringAssert.Contains(failure.Message, "No member of the anonymous union was supplied; provide one of: small, wide");
    }

    /// <summary>Selected bitfield updates retain all surrounding bits under both supported packed sharing rules.</summary>
    /// <param name="member">The bitfield to replace.</param>
    /// <param name="value">The replacement unsigned value.</param>
    /// <param name="expectedHex">The complete four-byte destination after replacement.</param>
    [TestMethod]
    [DataRow("a", 1, "F9FFFFAA")]
    [DataRow("b", 17, "8FFFFFAA")]
    [DataRow("c", 257, "FF01FFAA")]
    [DataRow("d", 65, "FFFF83AA")]
    public void SelectedBitfieldUpdate_PreservesNeighbors(string member, int value, string expectedHex)
    {
        foreach (BitfieldPacking packing in new[] { BitfieldPacking.SysV, BitfieldPacking.Msvc, })
        {
            var layout = new CStruct("struct root { uint8 a:3; uint8 b:5; uint16 c:9; uint16 d:7; uint8 tail; };", compilationOptions: new CStructCompilationOptions { BitfieldPacking = packing, });
            using var destination = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xAA, });
            layout.Update(destination, "root." + member, value);
            CollectionAssert.AreEqual(Convert.FromHexString(expectedHex), destination.ToArray(), packing.ToString());
        }
    }

    /// <summary>A runtime array accepts its exact element limit and diagnoses a negative count before materialization.</summary>
    [TestMethod]
    public void RuntimeArray_EnforcesCountAndExactLimit()
    {
        var layout = new CStruct("struct root { uint8 values[count]; };");
        var data = new Dictionary<string, object?> { ["values"] = new byte[] { 11, 12, }, };
        var variables = new Dictionary<string, int> { ["count"] = 2, };
        CollectionAssert.AreEqual(new byte[] { 11, 12, }, layout.Serialize("root", data, variables, new WriteOptions { MaxArrayElements = 2, }));

        // The configured count limit must be checked before the payload is converted into a list.
        CStructWriteLimitException limit = Assert.Throws<CStructWriteLimitException>(() => layout.Serialize("root", data, variables, new WriteOptions { MaxArrayElements = 1, }));
        StringAssert.Contains(limit.Message, "Array length exceeds the configured write limit: values");
        variables["count"] = -1;

        // Negative counts are invalid lengths, not a request to encode an empty array.
        CStructWriteException negative = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", data, variables));
        StringAssert.Contains(negative.Message, "Array length cannot be negative: values");
    }

    /// <summary>Rejected object members distinguish an empty shape from one with a known member list.</summary>
    /// <param name="definition">The target shape.</param>
    /// <param name="declared">The expected description of allowed members.</param>
    [TestMethod]
    [DataRow("struct root { };", "no members")]
    [DataRow("struct root { uint8 value; };", "value")]
    public void UnknownMember_ListsTheAllowedShape(string definition, string declared)
    {
        var layout = new CStruct(definition);
        var data = new Dictionary<string, object?> { ["unexpected"] = 1, };

        // Reject is explicitly selected so this tests a caller-visible validation policy, not optional leniency.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", data, options: new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, }));
        StringAssert.StartsWith(failure.Message, $"'unexpected' is not a member of 'root' (WriteOptions.UnknownMembers is Reject). The layout declares: {declared}");
    }
}
