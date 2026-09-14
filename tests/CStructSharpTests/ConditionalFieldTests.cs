namespace CStructSharpTests;

using CStructSharp;

/// <summary>Checks active storage and result members for runtime conditional groups.</summary>
[TestClass]
public class ConditionalFieldTests
{
    /// <summary>Aligned arrays advance by each active record's padded extent while retaining declared alignment.</summary>
    [TestMethod]
    public void AlignedConditionalArrays_UseActiveExtents()
    {
        const string layout = "struct entry { uint8 tag; if (tag) { uint32 wide; } else { uint8 small; } uint8 tail; }; struct root { uint8 prefix; entry items[2]; uint8 end; };";
        var parser = new CStruct(layout, aligned: true);
        byte[] bytes = [7, 0, 0, 0, 0, 42, 99, 0, 1, 0, 0, 0, 43, 0, 0, 0, 98, 0, 0, 0, 77, 0, 0, 0];
        Assert.AreEqual(4, parser.GetStructAlignmentInBytes("entry"));
        Assert.AreEqual(4, parser.GetStructAlignmentInBytes("root"));
        using var stream = new MemoryStream(bytes);
        (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
        Assert.AreEqual(24L, stream.Position);
        Assert.AreEqual((byte)42, (byte)parsed.root.items[0].small);
        Assert.AreEqual(43U, (uint)parsed.root.items[1].wide);
        Assert.AreEqual((byte)77, (byte)parsed.root.end);
        CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
        foreach ((string path, long start, long end) in new[]
        {
            ("root.items[0].small", 5L, 6L),
            ("root.items[1].wide", 12L, 16L),
            ("root.items[1].tail", 16L, 17L),
            ("root.end", 20L, 21L),
        })
        {
            stream.Position = 0;
            Assert.AreEqual(start, parser.ResolveAddress(stream, path));
            Assert.IsTrue(debug.Any(item => item.DebugStackString == path && item.CurPos == start && item.EndPos == end));
        }

        stream.Position = 0;
        parser.UpdateStream(stream, "root.items[1].wide", 44U);
        Assert.AreEqual(0L, stream.Position);
        Assert.AreEqual(44U, parser.ReadValue<uint>(stream, "root.items[1].wide"));
        byte[] before = (byte[])bytes.Clone();
        stream.Position = 0;
        Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.items[1].tag", 0));
        CollectionAssert.AreEqual(before, bytes);
    }

    /// <summary>Anonymous promotion observes the same inactive-value and local-variable rules as named fields.</summary>
    [TestMethod]
    public void PromotedConditionalFields_RejectInactiveValuesAndStaleCounts()
    {
        const string layout = "struct entry { uint8 tag; if (tag) { struct { struct { uint8 count; }; }; } if (count) { uint8 value; } }; struct root { entry items[2]; };";
        var parser = new CStruct(layout, aligned: false);
        byte[] bytes = [1, 1, 42, 0, 99];
        Assert.Throws<CStructLayoutException>(() => parser.ParseStream(new MemoryStream(bytes), "root"));
        Assert.Throws<CStructLayoutException>(() => parser.ResolveAddress(new MemoryStream(bytes), "root.items[1].value"));
        Assert.Throws<CStructWriteException>(() => parser.Serialize("entry", new { tag = 0, count = 1, value = 42 }));

        const string validLayout = "struct root { uint8 tag; if (tag) { struct { struct { uint8 count; }; uint8 values[count]; }; } uint8 tail; };";
        var valid = new CStruct(validLayout, aligned: false);
        byte[] active = [1, 2, 42, 43, 99];
        using var stream = new MemoryStream(active);
        (List<DebugData> debug, dynamic parsed) = valid.ParseStreamWithDebug(stream, "root");
        CollectionAssert.AreEqual(active, valid.Serialize("root", parsed.root));
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.count" && item.CurPos == 1 && item.EndPos == 2));
        stream.Position = 0;
        Assert.AreEqual(4L, valid.ResolveAddress(stream, "root.tail"));
        Assert.Throws<CStructWriteException>(() => valid.Serialize("root", new { tag = 0, count = 2, values = new byte[] { 42, 43 }, tail = 99 }));
        CollectionAssert.AreEqual(new byte[] { 0, 99 }, valid.Serialize("root", new { tag = 0, tail = 99 }));
    }

    /// <summary>Unused conditional declarations do not force selected updates to parse unrelated bytes.</summary>
    [TestMethod]
    public void Updates_IgnoreUnreachableConditionalTypes()
    {
        const string root = "struct simple { uint8 value; utf8 unrelated[1]; }; typedef simple root;";
        const string unused = "struct unused { uint8 tag; if (tag) { uint16 payload; } unused *next; };";
        foreach (string layout in new[] { root, unused + root })
        {
            var parser = new CStruct(layout, aligned: false);
            byte[] bytes = [1, 255];
            using var stream = new MemoryStream(bytes);
            parser.UpdateStream(stream, "root.value", 42);
            CollectionAssert.AreEqual(new byte[] { 42, 255 }, bytes);
            Assert.AreEqual(0L, stream.Position);
        }
    }

    /// <summary>Reachability follows nested aliases and pointer targets when enforcing branch stability.</summary>
    [TestMethod]
    public void Updates_ProtectReachableConditionalTypes()
    {
        const string types = "struct variant { uint8 tag; if (tag) { uint16 first; } else { uint16 second; } }; typedef variant alias;";
        foreach (bool pointer in new[] { false, true })
        {
            string layout = types + (pointer ? "struct root { alias *entry; };" : "struct root { alias entry; };");
            var parser = new CStruct(layout, aligned: false, pointerSize: 4);
            byte[] bytes = pointer ? [4, 0, 0, 0, 1, 42, 0] : [1, 42, 0];
            byte[] original = (byte[])bytes.Clone();
            using var stream = new MemoryStream(bytes);
            string path = pointer ? "root.entry.value.tag" : "root.entry.tag";
            Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, path, 0));
            CollectionAssert.AreEqual(original, bytes);
            Assert.AreEqual(0L, stream.Position);
        }
    }

    /// <summary>A branch retains its entry decision while nested groups see newly read fields.</summary>
    [TestMethod]
    public void Groups_FreezeDecisionsPerArrayElement()
    {
        const string ifLayout = "struct entry { uint8 tag; if (tag) { struct { uint8 tag; uint8 count; } child; if (count) { uint8 value; } uint8 end; } else { uint16 absent; } if (tag == 1) { uint8 trailer; } }; struct root { entry items[2]; };";
        const string switchLayout = "struct entry { uint8 tag; switch (tag) { case 1: { struct { uint8 tag; uint8 count; } child; if (count) { uint8 value; } uint8 end; } case 0: { uint16 absent; } } if (tag == 1) { uint8 trailer; } }; struct root { entry items[2]; };";
        foreach (string layout in new[]
        {
            ifLayout,
            switchLayout,
        })
        {
            var parser = new CStruct(layout, aligned: false);
            byte[] bytes = [1, 0, 1, 42, 77, 88, 0, 0x34, 0x12];
            using var stream = new MemoryStream(bytes);
            (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
            Assert.AreEqual((byte)42, (byte)parsed.root.items[0].value);
            Assert.AreEqual((byte)77, (byte)parsed.root.items[0].end);
            Assert.AreEqual((byte)88, (byte)parsed.root.items[0].trailer);
            Assert.AreEqual((ushort)0x1234, (ushort)parsed.root.items[1].absent);
            CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
            DebugData value = debug.Single(item => item.DebugStackString == "root.items[0].value");
            Assert.AreEqual(3L, value.CurPos);
            Assert.AreEqual(4L, value.EndPos);
            stream.Position = 0;
            Assert.AreEqual(4L, parser.ResolveAddress(stream, "root.items[0].end"));
            stream.Position = 0;
            Assert.AreEqual(7L, parser.ResolveAddress(stream, "root.items[1].absent"));
            stream.Position = 0;
            parser.UpdateStream(stream, "root.items[0].child.tag", 2);
            stream.Position = 0;
            Assert.AreEqual((byte)77, parser.ReadValue<byte>(stream, "root.items[0].end"));
            stream.Position = 0;
            Assert.AreEqual((byte)88, parser.ReadValue<byte>(stream, "root.items[0].trailer"));
        }
    }

    /// <summary>Absent fields cannot reuse a count from the caller or the preceding array element.</summary>
    [TestMethod]
    public void ConditionalLocals_ShadowStaleVariables()
    {
        const string layout = "struct entry { uint8 tag; if (tag) { uint8 count; } if (count) { uint8 value; } }; struct root { entry items[2]; };";
        var parser = new CStruct(layout, aligned: false);
        byte[] bytes = [1, 1, 42, 0, 99];
        Assert.Throws<CStructLayoutException>(() => parser.ParseStream(new MemoryStream(bytes), "root"));
        Assert.Throws<CStructLayoutException>(() => parser.ResolveAddress(new MemoryStream(bytes), "root.items[1].value"));
        Assert.Throws<CStructLayoutException>(() => parser.Serialize("root", new
        {
            items = new object[] { new { tag = 1, count = 1, value = 42 }, new { tag = 0, value = 99 } },
        }));

        var forward = new CStruct("struct root { if (later) { uint8 first; } uint8 later; };", aligned: false);
        var variables = new Dictionary<string, int> { ["later"] = 1 };
        Assert.Throws<CStructLayoutException>(() => forward.ParseStream(new MemoryStream(new byte[] { 42, 1 }), "root", variables: variables));
    }

    /// <summary>Case labels are distinct compile-time values even when their arms are empty.</summary>
    [TestMethod]
    public void SwitchCases_ValidateConstantValues()
    {
        foreach (string cases in new[]
        {
            "case 1: {} case (1 + 0): {}",
            "case TAG: {} case 1: { uint8 value; }",
            "case runtime: {}",
        })
        {
            Assert.Throws<CStructLayoutException>(() => new CStruct(
                "#define TAG 1\nstruct root { uint8 runtime; switch (runtime) { " + cases + " } };"));
        }

        var parser = new CStruct(
            "#define TAG 1\nstruct root { uint8 tag; switch (tag) { case TAG: { struct { uint8 value; } selected; } default: { uint16 fallback; } } };",
            aligned: false);
        var variables = new Dictionary<string, int> { ["TAG"] = 2 };
        dynamic parsed = parser.ParseStream(new MemoryStream(new byte[] { 1, 42 }), "root", variables: variables);
        Assert.AreEqual((byte)42, (byte)parsed.selected.value);
        CollectionAssert.AreEqual(new byte[] { 1, 42 }, parser.Serialize("root", parsed, variables: variables));
    }

    /// <summary>Changing a discriminator cannot reinterpret or relocate existing fields.</summary>
    [TestMethod]
    public void Update_PreservesActiveLayout()
    {
        var parser = new CStruct("struct root { uint8 tag; if (tag == 1) { uint16 first; } else { uint16 second; } uint8 tail; };", aligned: false);
        using var stream = new MemoryStream(new byte[] { 1, 42, 0, 99 });
        byte[] before = stream.ToArray();
        Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.tag", 2));
        CollectionAssert.AreEqual(before, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
        parser.UpdateStream(stream, "root.first", 43);
        Assert.AreEqual((ushort)43, parser.ReadValue<ushort>(stream, "root.first"));
    }

    /// <summary>A branch change is still a layout change when its field has an empty extent.</summary>
    [TestMethod]
    public void Update_RejectsZeroLengthBranchChanges()
    {
        var parser = new CStruct("struct root { uint8 tag; if (tag) { uint8 empty[0]; } uint8 tail; };", aligned: false);
        using var stream = new MemoryStream(new byte[] { 0, 99 });
        Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.tag", 1));
        CollectionAssert.AreEqual(new byte[] { 0, 99 }, stream.ToArray());
    }

    /// <summary>Switch cases consume one selected record and expose a bounded default.</summary>
    [TestMethod]
    public void SwitchCases_SelectOneAlternative()
    {
        var parser = new CStruct("struct root { uint8 tag; switch (tag) { case 1: { uint16 first; } case 2: { uint32 second; } default: { uint8 unknown; } } uint8 tail; };", aligned: false);
        foreach (byte[] bytes in new byte[][] { [1, 42, 0, 99], [2, 42, 0, 0, 0, 99], [3, 42, 99] })
        {
            dynamic parsed = parser.ParseStream(new MemoryStream(bytes), "root");
            Assert.AreEqual((byte)99, (byte)parsed.tail);
            CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed));
            using var stream = new MemoryStream(bytes);
            Assert.AreEqual(bytes.Length - 1L, parser.ResolveAddress(stream, "root.tail"));
        }
    }

    /// <summary>Only the active named branch consumes bytes or appears in parsed/debug results.</summary>
    [TestMethod]
    public void NamedBranches_ParseWriteAndResolve()
    {
        const string layout = "struct root { uint8 kind; if (kind == 1) { struct { uint16 number; } first; } else { struct { uint32 number; } second; } uint8 tail; };";
        var parser = new CStruct(layout, aligned: false);
        foreach (byte kind in new byte[] { 1, 2 })
        {
            byte[] bytes = kind == 1 ? [1, 42, 0, 99] : [2, 42, 0, 0, 0, 99];
            using var stream = new MemoryStream(bytes);
            (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
            var root = (IDictionary<string, object>)parsed.root;
            Assert.IsTrue(root.ContainsKey(kind == 1 ? "first" : "second"));
            Assert.IsFalse(root.ContainsKey(kind == 1 ? "second" : "first"));
            CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
            stream.Position = 0;
            Assert.AreEqual(bytes.Length - 1L, parser.ResolveAddress(stream, "root.tail"));
            stream.Position = 0;
            Assert.Throws<CStructPathException>(() => parser.ResolveAddress(stream, kind == 1 ? "root.second.number" : "root.first.number"));
            Assert.IsFalse(debug.Any(item => item.DebugStackString.Contains(kind == 1 ? "second" : "first")));
        }

        Assert.Throws<CStructWriteException>(() => parser.Serialize("root", new { kind = 1, first = new { number = 42 }, second = new { number = 42 }, tail = 99 }));
    }

    /// <summary>Inactive groups neither evaluate nested predicates nor consume even alignment padding.</summary>
    [TestMethod]
    public void NestedConditions_SkipInactiveExpressions()
    {
        var parser = new CStruct("struct root { uint8 kind; if (kind) { if (missing) { uint32 value; } } uint8 tail; };", aligned: false);
        using var stream = new MemoryStream(new byte[] { 0, 99 });
        dynamic value = parser.ParseStream(stream, "root");
        Assert.AreEqual((byte)99, (byte)value.tail);
        CollectionAssert.AreEqual(new byte[] { 0, 99 }, parser.Serialize("root", value));
    }
}
