namespace CStructSharpTests;

using System.Dynamic;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>Verifies that every read-like path traversal consumes the same caller-configured safety budgets.</summary>
[TestClass]
public class TraversalLimitTests
{
    /// <summary>
    ///     count = 3 requests three item records, but the policy allows only two.
    /// </summary>
    /// <remarks>
    ///     Full parsing and selected path operations must all reject it before scanning to a requested element. Query
    ///     APIs must restore their starting position after the failure instead of providing a way around the array
    ///     limit.
    /// </remarks>
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

    /// <summary>
    ///     root, middle, and leaf form three nested struct levels.
    /// </summary>
    /// <remarks>
    ///     A limit of two must fail even when the caller directly selects the leaf. Starting a selected read must not
    ///     reset the depth counter and hide the containing structures.
    /// </remarks>
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

    /// <summary>
    ///     root already uses the one permitted struct level.
    /// </summary>
    /// <remarks>
    ///     Following selected to a child struct would add another, so address lookup must fail before returning that
    ///     target. The stream must return to its starting position of 1 after the failed query.
    /// </remarks>
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

    /// <summary>
    ///     Following selected reaches a byte at offset 1, not another struct.
    /// </summary>
    /// <remarks>
    ///     A nesting limit of one therefore permits this lookup and returns 1. Pointer depth and composite nesting are
    ///     different counts; a scalar target must not consume a nonexistent struct level.
    /// </remarks>
    [TestMethod]
    public void ScalarPointerTarget_AllowsExactRootNestingLimit()
    {
        var cstruct = new CStruct("struct root { byte *selected; };", pointerSize: 1);
        var options = new ReadOptions { MaxNestingDepth = 1, };

        using var stream = new MemoryStream([0x01, 0x2A,]);
        Assert.AreEqual(1L, cstruct.ResolveAddress(stream, "root.selected.value", options: options));
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>
    ///     A limit of two is sufficient to locate root.selected, but reading its nested leaf would enter a third level.
    /// </summary>
    /// <remarks>
    ///     Address lookup may therefore succeed while parsing that selected object's contents fails. The distinction is
    ///     the work performed after locating the target.
    /// </remarks>
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

    /// <summary>
    ///     Locating the first byte field requires no payload read, array scan, string scan, or pointer hop.
    /// </summary>
    /// <remarks>
    ///     Zero budgets for those kinds of work are therefore valid for this query. A zero nesting limit remains
    ///     invalid because even the root is a structure; the test distinguishes these cases.
    /// </remarks>
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

    /// <summary>
    ///     Locating selected first reads count to skip a data-sized array; reading the child then consumes its own two
    ///     bytes.
    /// </summary>
    /// <remarks>
    ///     Both phases must spend one shared budget, and debug rereads count too. Resetting accounting at the selected
    ///     object would let the operation exceed its configured limit.
    /// </remarks>
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

    /// <summary>
    ///     Finding text requires reading the earlier count, then measuring A plus its zero terminator requires more
    ///     bytes.
    /// </summary>
    /// <remarks>
    ///     A budget covering only part of that combined work must fail. GetDynamicArrayLength must restore the original
    ///     stream position even after exhausting the budget.
    /// </remarks>
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

    /// <summary>
    ///     Reaching head already follows one pointer.
    /// </summary>
    /// <remarks>
    ///     Reading its next pointer would require a second hop, so a limit of one must reject it. Selected parsing and
    ///     related path operations must carry the depth already spent reaching the target into subsequent work.
    /// </remarks>
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

    /// <summary>
    ///     The UTF-16 text A and its terminator occupy four bytes before selected.
    /// </summary>
    /// <remarks>
    ///     An insufficient string-byte limit must block reads and any path operation that needs to scan past this text.
    ///     Both byte orders follow the same rule, and queries must restore their starting position on failure.
    /// </remarks>
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

    /// <summary>
    ///     The cases exceed array, nesting, pointer, target-size, or total-read limits while locating an update target.
    /// </summary>
    /// <remarks>
    ///     Each must fail before committing a replacement. Checking unchanged bytes and position proves that
    ///     UpdateOptions traversal limits apply before the separate write phase begins.
    /// </remarks>
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
                    options: new UpdateOptions { MaxTraversalArrayElements = 2, }));
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

    /// <summary>
    ///     Regression coverage for the architecture improvement plan's fix (AP-2.3) that added a dedicated <see cref="UpdateOptions.MaxTraversalArrayElements"/> instead of update-path
    ///     traversal silently reusing <see cref="WriteOptions.MaxArrayElements"/> (which is meant to bound the
    ///     array being written, not the arrays traversal passes through to find it). A generous
    ///     <see cref="WriteOptions.MaxArrayElements"/> paired with a tight
    ///     <see cref="UpdateOptions.MaxTraversalArrayElements"/> must still reject; the reverse pairing must
    ///     succeed, proving the two budgets are independently enforced rather than one silently standing in for
    ///     the other.
    /// </summary>
    [TestMethod]
    public void UpdateTraversalArrayLimit_IsIndependentFromTheWriteArrayLimit()
    {
        var arrayStruct = new CStruct(
            "struct item { byte value; }; struct root { byte count; item items[count]; };",
            pointerSize: 1);
        Dictionary<string, Expr> variables = new() { ["count"] = new Literal(3), };

        using (var stream = new MemoryStream([0xEE, 0x03, 0x11, 0x22, 0x33,]) { Position = 1, })
        {
            // A tight traversal limit still rejects even though the write-side array limit is generous - proving
            // MaxArrayElements alone no longer bounds traversal.
            AssertUpdateLimit(
                stream,
                () => arrayStruct.UpdateStream(
                    stream,
                    "root.items[2].value",
                    (byte)0x5A,
                    variables: variables,
                    options: new UpdateOptions { MaxArrayElements = 1_000_000, MaxTraversalArrayElements = 2, }));
        }

        using (var stream = new MemoryStream([0xEE, 0x03, 0x11, 0x22, 0x33,]) { Position = 1, })
        {
            // A tight write-side array limit does not block traversal reaching the same target - proving the two
            // budgets are independently enforced, not aliases of each other.
            arrayStruct.UpdateStream(
                stream,
                "root.items[2].value",
                (byte)0x5A,
                variables: variables,
                options: new UpdateOptions { MaxArrayElements = 1, MaxTraversalArrayElements = 1_000_000, });

            Assert.AreEqual((byte)0x5A, stream.ToArray()[4]);
        }
    }

    /// <summary>
    ///     A fixed-size union before selected includes values[3], while the array limit is two.
    /// </summary>
    /// <remarks>
    ///     Selected operations must still enforce the relevant traversal limit instead of bypassing it because the
    ///     union's extent is known. Its unrelated pointer view must not become an accidental target to follow.
    /// </remarks>
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
                    options: new UpdateOptions { MaxTraversalArrayElements = 2, }));
        }
    }

    /// <summary>
    ///     To reach selected, traversal must account for an earlier middle containing leaf.
    /// </summary>
    /// <remarks>
    ///     That preceding branch still exceeds the configured nesting limit. Selecting a later scalar cannot erase the
    ///     structural work needed to locate it, and failing queries must preserve caller position.
    /// </remarks>
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

    /// <summary>
    ///     The pointer at offset zero selects two aligned big-endian uint16 records.
    /// </summary>
    /// <remarks>
    ///     Limits set exactly to the required work must allow values 0x1234 and 0x5678 and resolve the second value at
    ///     offset 4. A limit is inclusive: using its final permitted unit is valid.
    /// </remarks>
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
