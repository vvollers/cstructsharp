namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks writer diagnostics and selected-field operations that ordinary whole-record round trips can miss.</summary>
[TestClass]
public class RuntimeWriterBoundaryTests
{
    /// <summary>Selecting an unresolved integer definition identifies the definition being evaluated.</summary>
    [TestMethod]
    public void UnresolvedDefinitionWrite_IdentifiesTheDefinition()
    {
        var layout = new CStruct("#define COUNT missing\nstruct root { uint8 value; };");

        // Unused definitions may remain unresolved, but selecting one must explain its evaluation context.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("COUNT", 0));
        StringAssert.StartsWith(failure.Message, "Cannot evaluate definition COUNT:");
        StringAssert.Contains(failure.Message, "missing");
    }

    /// <summary>A text definition is metadata, not writable binary storage.</summary>
    [TestMethod]
    public void TextDefinitionWrite_ExplainsUnsupportedStorage()
    {
        var layout = new CStruct("#define LABEL hello world\nstruct root { uint8 value; };");

        // Text metadata has no binary codec and must not appear to serialize successfully.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => layout.Serialize("LABEL", 0));
        StringAssert.StartsWith(failure.Message, "Unsupported element type for writing: ConstantDefinition");
    }

    /// <summary>A null composite is rejected before an empty shape or static plan can silently accept it.</summary>
    /// <param name="definition">An empty, fixed-size or union root declaration.</param>
    [TestMethod]
    [DataRow("struct root { };")]
    [DataRow("struct root { uint8 value; };")]
    [DataRow("union root { uint8 value; };")]
    public void NullComposite_ExplainsTheRejectedRoot(string definition)
    {
        var layout = new CStruct(definition);
        using var destination = new MemoryStream(new byte[] { 11, 22, 33, });
        destination.Position = 1;

        // Null is valid for pointer values, but it cannot describe a whole struct or union.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Write(destination, "root", null!));
        StringAssert.StartsWith(failure.Message, "Null is not valid for struct or union value: root");
        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(new byte[] { 11, 22, 33, }, destination.ToArray());
    }

    /// <summary>A void alias can name a pointer target, but writing the alias itself explains its missing value handler.</summary>
    [TestMethod]
    public void VoidAliasWrite_ExplainsMissingValueHandler()
    {
        var layout = new CStruct("typedef void opaque;");

        // Naming an opaque type does not supply a codec for a standalone value of that type.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => layout.Serialize("opaque", 0));
        StringAssert.Contains(failure.Message, "No handler for field type void");
    }

    /// <summary>Preserving an incomplete union or bitfield fails with a useful explanation and leaves the source unchanged.</summary>
    /// <param name="union">Whether the update preserves a union rather than a shared bitfield unit.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TruncatedUpdate_ExplainsTheMissingStorage(bool union)
    {
        var layout = new CStruct(union
            ? "union root { uint16 wide; uint8 small; };"
            : "struct root { uint16 low:4; uint16 high:12; };");
        using var stream = new MemoryStream(new byte[] { 0xA5, });
        string path = union ? "root" : "root.low";
        object value = union ? UnionValue.FromMember("root", "small", (byte)3) : 3;

        // Preservation needs the complete old storage before a replacement can be staged.
        CStructReadException failure = Assert.Throws<CStructReadException>(() =>
            layout.Update(stream, path, value, options: new UpdateOptions { ClearUnionStorage = false, }));
        string reason = union
            ? "Cannot preserve union storage because the complete existing extent is not present"
            : "Cannot update a bitfield whose complete storage unit is not present";
        StringAssert.Contains(failure.Message, reason);
        Assert.AreEqual(0L, stream.Position);
        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
    }

    /// <summary>A multidimensional write counts leaf elements and accepts exactly the configured limit.</summary>
    [TestMethod]
    public void MultidimensionalArray_EnforcesTotalLeafLimit()
    {
        var layout = new CStruct("struct root { uint8 values[2][2]; uint8 tail; };");
        var data = new Dictionary<string, object?>
        {
            ["values"] = new byte[][] { new byte[] { 1, 2, }, new byte[] { 3, 4, }, },
            ["tail"] = (byte)5,
        };
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, }, layout.Serialize("root", data, options: new WriteOptions { MaxArrayElements = 4, }));

        // The limit applies to all four leaves, not just the two outer rows.
        CStructWriteLimitException failure = Assert.Throws<CStructWriteLimitException>(() =>
            layout.Serialize("root", data, options: new WriteOptions { MaxArrayElements = 3, }));
        StringAssert.Contains(failure.Message, "Array length exceeds the configured write limit: values");
    }

    /// <summary>An empty write path is rejected before validating write budgets or changing the destination.</summary>
    /// <param name="path">An empty or whitespace-only root selection.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow(" \t")]
    public void EmptyPath_PreservesDestination(string path)
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var destination = new MemoryStream(new byte[] { 11, 22, 33, });
        destination.Position = 1;

        // Validate the path before output begins, even when the caller supplied otherwise valid data.
        CStructPathException failure = Assert.Throws<CStructPathException>(() =>
            layout.Write(destination, path, new Dictionary<string, object?> { ["value"] = (byte)7, }));
        StringAssert.StartsWith(failure.Message, "Path is empty");

        // Path validation also takes precedence over an invalid budget; it is not deferred until layout traversal.
        CStructPathException invalidBudgetFailure = Assert.Throws<CStructPathException>(() =>
            layout.Write(destination, path, new Dictionary<string, object?> { ["value"] = (byte)7, }, options: new WriteOptions { MaxArrayElements = -1, }));
        StringAssert.StartsWith(invalidBudgetFailure.Message, "Path is empty");
        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(new byte[] { 11, 22, 33, }, destination.ToArray());
    }

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
