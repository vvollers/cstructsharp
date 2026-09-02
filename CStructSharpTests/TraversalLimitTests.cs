namespace CStructSharpTests;

using System.Dynamic;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>Verifies that every read-like path traversal consumes the same caller-configured safety budgets.</summary>
[TestClass]
public class TraversalLimitTests
{
    /// <summary>Rejects an excessive runtime array before selected traversal can scan to a high index.</summary>
    [TestMethod]
    public void RuntimeArrayLimit_AgreesAcrossReadLikeOperations()
    {
        const string layout = """
                              struct item { byte value; };
                              struct root { byte count; item items[count]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        byte[] bytes = [0x03, 0x11, 0x22, 0x33,];
        var options = new ReadOptions { MaxArrayElements = 2, };

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.items[2]", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(stream, "root.items[2]", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream([0xEE, .. bytes]) { Position = 1, })
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(
                    stream,
                    "root.items[2]",
                    new Dictionary<string, Expr>(),
                    options));
            Assert.AreEqual(1L, stream.Position);
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.GetDynamicArrayLength(
                    stream,
                    "root.items",
                    new Dictionary<string, Expr>(),
                    options));
            Assert.AreEqual(0L, stream.Position);
        }
    }

    /// <summary>Counts the complete lexical struct path instead of restarting depth at a selected object.</summary>
    [TestMethod]
    public void NestingLimit_AgreesAcrossSelectedAndAddressTraversal()
    {
        const string layout = """
                              struct leaf { byte value; };
                              struct middle { leaf child; };
                              struct root { middle child; };
                              """;
        var cstruct = new CStruct(layout);
        var options = new ReadOptions { MaxNestingDepth = 2, };

        using (var stream = new MemoryStream([0x2A,]))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream([0x2A,]))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.child.child", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream([0x2A,]))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.child.child",
                    new Dictionary<string, Expr>(),
                    options));
        }

        using (var stream = new MemoryStream([0xEE, 0x2A,]) { Position = 1, })
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(stream, "root.child.child.value", options: options));
            Assert.AreEqual(1L, stream.Position);
        }

        using (var stream = new MemoryStream([0xEE, 0x2A,]) { Position = 1, })
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(stream, "root.child.child", options: options));
            Assert.AreEqual(1L, stream.Position);
        }
    }

    /// <summary>Counts a terminal pointer-to-struct object as one additional structure level.</summary>
    [TestMethod]
    public void PointerStructTarget_AppliesNestingLimitBeforeReturningAddress()
    {
        const string layout = "struct child { byte value; }; struct root { child *selected; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        var options = new ReadOptions { MaxNestingDepth = 1, };

        using var stream = new MemoryStream([0xEE, 0x02, 0x2A,]) { Position = 1, };
        Assert.Throws<CStructReadLimitException>(
            () => cstruct.ResolveAddress(stream, "root.selected.value", options: options));
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>Does not count a terminal scalar pointer target as a nested structure.</summary>
    [TestMethod]
    public void ScalarPointerTarget_AllowsExactRootNestingLimit()
    {
        var cstruct = new CStruct("struct root { byte *selected; };", pointerSize: 1);
        var options = new ReadOptions { MaxNestingDepth = 1, };

        using var stream = new MemoryStream([0x01, 0x2A,]);
        Assert.AreEqual(1L, cstruct.ResolveAddress(stream, "root.selected.value", options: options));
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Counts the selected struct itself before parsing composites nested inside it.</summary>
    [TestMethod]
    public void SelectedStructContents_ContinueFromContainingNestingDepth()
    {
        const string layout = """
                              struct leaf { byte value; };
                              struct middle { leaf child; };
                              struct root { middle selected; };
                              """;
        var cstruct = new CStruct(layout);
        var options = new ReadOptions { MaxNestingDepth = 2, };

        using (var stream = new MemoryStream([0x2A,]))
        {
            Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root.selected", options: options));
        }

        using (var stream = new MemoryStream([0x2A,]))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.selected", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream([0x2A,]))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.selected",
                    new Dictionary<string, Expr>(),
                    options));
        }
    }

    /// <summary>Allows zero-sized limits when an operation performs none of the work they govern.</summary>
    [TestMethod]
    public void ZeroWorkLimits_AreValidAndInclusive()
    {
        var cstruct = new CStruct("struct root { byte value; };");
        ReadOptions[] validOptions =
        [
            new() { MaxPointerDepth = 0, },
            new() { MaxPointerTargetBytes = 0, },
            new() { MaxArrayElements = 0, },
            new() { MaxStringBytes = 0, },
            new() { MaxTotalBytesRead = 0, },
        ];

        foreach (ReadOptions options in validOptions)
        {
            using var stream = new MemoryStream([0x2A,]);
            Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root.value", options: options));
            Assert.AreEqual(0L, stream.Position);
        }

        using var invalidStream = new MemoryStream([0x2A,]);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cstruct.ResolveAddress(
                invalidStream,
                "root.value",
                options: new ReadOptions { MaxNestingDepth = 0, }));
        Assert.AreEqual(0L, invalidStream.Position);
    }

    /// <summary>Shares physical-read accounting between target location and the selected object reader.</summary>
    [TestMethod]
    public void SelectedRead_UsesOneTotalByteBudget()
    {
        const string layout = """
                              struct child { byte first; byte second; };
                              struct root { byte count; byte skipped[count]; child selected; };
                              """;
        var cstruct = new CStruct(layout);
        byte[] bytes = [0x02, 0xA1, 0xA2, 0x11, 0x22,];

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(
                    stream,
                    "root.selected",
                    new Dictionary<string, Expr>(),
                    new ReadOptions { MaxTotalBytesRead = 2, }));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.selected",
                    new Dictionary<string, Expr>(),
                    new ReadOptions { MaxTotalBytesRead = 4, }));
        }
    }

    /// <summary>Does not reset total-byte accounting before measuring a selected terminated string.</summary>
    [TestMethod]
    public void DynamicLength_UsesResolverAndStringReadsAsOneBudget()
    {
        const string layout = "struct root { byte count; byte skipped[count]; char text[]; };";
        var cstruct = new CStruct(layout);
        using var stream = new MemoryStream([0x02, 0xA1, 0xA2, 0x41, 0x00,]);

        Assert.Throws<CStructReadLimitException>(
            () => cstruct.GetDynamicArrayLength(
                stream,
                "root.text",
                new Dictionary<string, Expr>(),
                new ReadOptions { MaxTotalBytesRead = 2, }));
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Keeps pointer depth consumed by a selected path active while reading the selected target.</summary>
    [TestMethod]
    public void SelectedPointerTarget_PreservesPathDereferenceDepth()
    {
        const string layout = """
                              struct node { node *next; byte value; };
                              struct root { node *head; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        byte[] bytes = [0x01, 0x03, 0x11, 0x00, 0x22,];
        var options = new ReadOptions { MaxPointerDepth = 1, };

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.head.value", new Dictionary<string, Expr>(), options));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.head.value",
                    new Dictionary<string, Expr>(),
                    options));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(
                    stream,
                    "root.head.value.next.value",
                    options: options));
            Assert.AreEqual(0L, stream.Position);
        }
    }

    /// <summary>Applies the selected UTF-16 byte budget before every path-based operation scans past the terminator.</summary>
    /// <param name="isLittleEndian">Whether neutral wide characters use UTF-16LE instead of UTF-16BE.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void WideStringLimit_AgreesAcrossReadAddressLengthAndUpdate(bool isLittleEndian)
    {
        const string layout = """
                              struct child { byte value; };
                              struct root { wchar text[]; child selected; };
                              """;
        var cstruct = new CStruct(layout, isLittleEndian: isLittleEndian);
        byte[] bytes = isLittleEndian
                           ? [0x41, 0x00, 0x00, 0x00, 0x2A,]
                           : [0x00, 0x41, 0x00, 0x00, 0x2A,];
        var readOptions = new ReadOptions { MaxStringBytes = 3, };

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root", new Dictionary<string, Expr>(), readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.selected", new Dictionary<string, Expr>(), readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.selected",
                    new Dictionary<string, Expr>(),
                    readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(stream, "root.selected.value", options: readOptions));
            Assert.AreEqual(0L, stream.Position);
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.GetDynamicArrayLength(
                    stream,
                    "root.text",
                    new Dictionary<string, Expr>(),
                    readOptions));
            Assert.AreEqual(0L, stream.Position);
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            AssertUpdateLimit(
                stream,
                () => cstruct.UpdateStream(
                    stream,
                    "root.selected.value",
                    (byte)0x5A,
                    options: new UpdateOptions { MaxTraversalStringBytes = 3, }));
        }
    }

    /// <summary>Copies every read-side traversal limit into update resolution before any caller bytes are changed.</summary>
    [TestMethod]
    public void UpdateTraversalLimits_RejectBeforeMutationAndRestorePosition()
    {
        var arrayStruct = new CStruct(
            "struct item { byte value; }; struct root { byte count; item items[count]; };",
            pointerSize: 1);
        using (var stream = new MemoryStream([0xEE, 0x03, 0x11, 0x22, 0x33,]) { Position = 1, })
        {
            AssertUpdateLimit(
                stream,
                () => arrayStruct.UpdateStream(
                    stream,
                    "root.items[2].value",
                    (byte)0x5A,
                    variables: new Dictionary<string, Expr> { ["count"] = new Literal(3), },
                    options: new UpdateOptions { MaxArrayElements = 2, }));
        }

        var nestedStruct = new CStruct(
            "struct leaf { byte value; }; struct middle { leaf child; }; struct root { middle child; };");
        using (var stream = new MemoryStream([0x2A,]))
        {
            AssertUpdateLimit(
                stream,
                () => nestedStruct.UpdateStream(
                    stream,
                    "root.child.child.value",
                    (byte)0x5A,
                    options: new UpdateOptions { MaxTraversalNestingDepth = 2, }));
        }

        var pointerStruct = new CStruct(
            "struct node { node *next; byte value; }; struct root { node *head; };",
            pointerSize: 1);
        using (var stream = new MemoryStream([0x01, 0x03, 0x11, 0x00, 0x22,]))
        {
            AssertUpdateLimit(
                stream,
                () => pointerStruct.UpdateStream(
                    stream,
                    "root.head.value.next.value.value",
                    (byte)0x5A,
                    options: new UpdateOptions { MaxTraversalPointerDepth = 1, }));
        }

        var targetStruct = new CStruct("struct root { uint16 *pointer; };", pointerSize: 1);
        using (var stream = new MemoryStream([0x02, 0xEE, 0x34, 0x12,]))
        {
            AssertUpdateLimit(
                stream,
                () => targetStruct.UpdateStream(
                    stream,
                    "root.pointer.value",
                    (ushort)0xBEEF,
                    options: new UpdateOptions { MaxTraversalPointerTargetBytes = 1, }));
        }

        var totalStruct = new CStruct("struct root { byte count; byte skipped[count]; byte target; };");
        using (var stream = new MemoryStream([0x01, 0xEE, 0x2A,]))
        {
            AssertUpdateLimit(
                stream,
                () => totalStruct.UpdateStream(
                    stream,
                    "root.target",
                    (byte)0x5A,
                    options: new UpdateOptions { MaxTraversalBytesRead = 0, }));
        }
    }

    /// <summary>Validates bounded array work hidden inside a fixed-size union that precedes a selected target.</summary>
    [TestMethod]
    public void PrecedingUnion_AppliesArrayLimitsAcrossReadLikeOperations()
    {
        const string layout = """
                              union choice { byte values[3]; byte *unrelated; };
                              struct child { byte value; };
                              struct root { choice data; child selected; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        byte[] bytes = [0xFF, 0x11, 0x22, 0x2A,];
        var readOptions = new ReadOptions { MaxArrayElements = 2, };

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root", new Dictionary<string, Expr>(), readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.selected", new Dictionary<string, Expr>(), readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.selected",
                    new Dictionary<string, Expr>(),
                    readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(stream, "root.selected.value", options: readOptions));
            Assert.AreEqual(0L, stream.Position);
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            AssertUpdateLimit(
                stream,
                () => cstruct.UpdateStream(
                    stream,
                    "root.selected.value",
                    (byte)0x5A,
                    options: new UpdateOptions { MaxArrayElements = 2, }));
        }
    }

    /// <summary>Applies nesting limits while measuring an unselected composite that precedes the requested field.</summary>
    [TestMethod]
    public void PrecedingComposite_AppliesNestingLimitAcrossReadLikeOperations()
    {
        const string layout = """
                              struct leaf { byte value; };
                              struct middle { leaf child; };
                              struct root { middle skipped; byte selected; };
                              """;
        var cstruct = new CStruct(layout);
        byte[] bytes = [0x11, 0x2A,];
        var readOptions = new ReadOptions { MaxNestingDepth = 2, };

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStream(stream, "root.selected", new Dictionary<string, Expr>(), readOptions));
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ParseStreamWithDebug(
                    stream,
                    "root.selected",
                    new Dictionary<string, Expr>(),
                    readOptions));
        }

        using (var stream = new MemoryStream([0xEE, .. bytes]) { Position = 1, })
        {
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.ResolveAddress(stream, "root.selected", options: readOptions));
            Assert.AreEqual(1L, stream.Position);
        }

        using (var stream = new MemoryStream((byte[])bytes.Clone()))
        {
            AssertUpdateLimit(
                stream,
                () => cstruct.UpdateStream(
                    stream,
                    "root.selected",
                    (byte)0x5A,
                    options: new UpdateOptions { MaxTraversalNestingDepth = 2, }));
        }
    }

    /// <summary>Accepts values exactly at every configured boundary across aligned selected traversal.</summary>
    [TestMethod]
    public void TraversalLimits_AreInclusiveAtConfiguredBoundaries()
    {
        const string layout = """
                              struct leaf { uint16 value; };
                              struct middle { leaf items[2]; };
                              struct root { middle *selected; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: true, isLittleEndian: false);
        byte[] bytes = [0x02, 0xEE, 0x12, 0x34, 0x56, 0x78,];
        var options = new ReadOptions
        {
            MaxArrayElements = 2,
            MaxNestingDepth = 3,
            MaxPointerDepth = 1,
            MaxPointerTargetBytes = 4,
            MaxTotalBytesRead = 5,
        };

        using var stream = new MemoryStream((byte[])bytes.Clone());
        dynamic selected = cstruct.ParseStream(
            stream,
            "root.selected.value",
            new Dictionary<string, Expr>(),
            options);

        Assert.AreEqual((ushort)0x1234, (ushort)selected.items[0].value);
        Assert.AreEqual((ushort)0x5678, (ushort)selected.items[1].value);

        using var addressStream = new MemoryStream((byte[])bytes.Clone());
        long resolved = cstruct.ResolveAddress(
            addressStream,
            "root.selected.value.items[1].value",
            options: options);
        Assert.AreEqual(4L, resolved);
        Assert.AreEqual(0L, addressStream.Position);
    }

    /// <summary>Asserts that a failed update is classified as a read limit and preserves caller state.</summary>
    private static void AssertUpdateLimit(MemoryStream stream, Action action)
    {
        long originalPosition = stream.Position;
        byte[] originalBytes = stream.ToArray();

        Assert.Throws<CStructReadLimitException>(action);

        Assert.AreEqual(originalPosition, stream.Position);
        CollectionAssert.AreEqual(originalBytes, stream.ToArray());
    }
}
