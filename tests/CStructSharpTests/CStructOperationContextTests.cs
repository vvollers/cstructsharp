namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="CStructOperationContext"/> directly, independent of a real parse/read operation. Only
///     reachable indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c>
///     partial class.
/// </summary>
[TestClass]
public class CStructOperationContextTests
{
    /// <summary>A valid readable, seekable stream and default settings must produce a context exposing those settings.</summary>
    [TestMethod]
    public void Constructor_ValidStream_ExposesTheSuppliedSettings()
    {
        using var stream = new MemoryStream(new byte[16]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(
            new ReadOptions { MaxPointerDepth = 5, MaxArrayElements = 10, MaxNestingDepth = 8, });

        var context = new CStructOperationContext(stream, [], aligned: true, settings);

        Assert.AreEqual(5, context.MaxPointerDepth);
        Assert.AreEqual(10, context.MaxArrayElements);
        Assert.AreEqual(8, context.MaxNestingDepth);
        Assert.IsTrue(context.Aligned);
        Assert.AreEqual(0, context.StructureDepth);
    }

    /// <summary>A null stream is a caller programming error, not a domain failure.</summary>
    [TestMethod]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CStructOperationContext(
                null!,
                [],
                aligned: false,
                ReadOperationSettings.SnapshotReadOptions(null)));
    }

    /// <summary>A write-only, non-readable stream cannot back a read operation.</summary>
    [TestMethod]
    public void Constructor_NonReadableStream_Throws()
    {
        using var writeOnly = new NonReadableStream();

        Assert.Throws<ArgumentException>(
            () => new CStructOperationContext(
                writeOnly,
                [],
                aligned: false,
                ReadOperationSettings.SnapshotReadOptions(null)));
    }

    /// <summary>A negative pointer depth has no meaningful safety interpretation.</summary>
    [TestMethod]
    public void Constructor_NegativeMaxPointerDepth_Throws()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null) with { MaxPointerDepth = -1, };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CStructOperationContext(stream, [], aligned: false, settings));
    }

    /// <summary>A zero or negative nesting depth would forbid even the root object.</summary>
    [TestMethod]
    public void Constructor_NonPositiveMaxNestingDepth_Throws()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null) with { MaxNestingDepth = 0, };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CStructOperationContext(stream, [], aligned: false, settings));
    }

    /// <summary>Entering structures up to the configured limit succeeds; one more must fail and not corrupt the depth.</summary>
    [TestMethod]
    public void EnterStructure_ExceedsMaxNestingDepth_ThrowsAndLeavesDepthAtTheLimit()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null) with { MaxNestingDepth = 2, };
        var context = new CStructOperationContext(stream, [], aligned: false, settings);

        context.EnterStructure();
        context.EnterStructure();

        Assert.Throws<CStructReadLimitException>(() => context.EnterStructure());
        Assert.AreEqual(2, context.StructureDepth);
    }

    /// <summary>Exiting a structure releases exactly one level, allowing another entry afterward.</summary>
    [TestMethod]
    public void ExitStructure_ReleasesOneLevel()
    {
        using var stream = new MemoryStream(new byte[4]);
        var context = new CStructOperationContext(
            stream,
            [],
            aligned: false,
            ReadOperationSettings.SnapshotReadOptions(new ReadOptions { MaxNestingDepth = 1, }));

        context.EnterStructure();
        context.ExitStructure();
        context.EnterStructure();

        Assert.AreEqual(1, context.StructureDepth);
    }

    /// <summary>
    ///     Registering debug data must record exactly the requested byte range and restore the stream position to
    ///     immediately after the value, exactly like ordinary parsing would leave it.
    /// </summary>
    [TestMethod]
    public void RegisterDebugData_CapturesTheRequestedRangeAndRestoresPositionAfterIt()
    {
        byte[] bytes = [0x11, 0x22, 0x33, 0x44,];
        using var stream = new MemoryStream(bytes) { Position = 4, };
        var context = new CStructOperationContext(
            stream,
            [],
            aligned: false,
            ReadOperationSettings.SnapshotReadOptions(null));

        context.RegisterDebugData(curPos: 1, endPos: 3, debugStack: null, value: 0x2233, fieldTypeName: "uint16");

        Assert.HasCount(1, context.DebugMapping);
        DebugData entry = context.DebugMapping[0];
        Assert.AreEqual(1L, entry.CurPos);
        Assert.AreEqual(3L, entry.EndPos);
        Assert.AreEqual("uint16", entry.TypeName);
        CollectionAssert.AreEqual(new[] { 0x22, 0x33, }, entry.Buffer);
        Assert.AreEqual(3L, context.Stream.Position);
    }

    /// <summary>A zero-width value still gets a one-byte debug buffer so consumers always receive inspectable data.</summary>
    [TestMethod]
    public void RegisterDebugData_ZeroWidthValue_StillCapturesOneByte()
    {
        using var stream = new MemoryStream([0xAA, 0xBB,]);
        var context = new CStructOperationContext(
            stream,
            [],
            aligned: false,
            ReadOperationSettings.SnapshotReadOptions(null));

        context.RegisterDebugData(curPos: 0, endPos: 0, debugStack: null, value: 0, fieldTypeName: "none");

        Assert.HasCount(1, context.DebugMapping[0].Buffer);
    }

    /// <summary>A minimal stream that reports itself as write-only, for exercising the readable-stream guard.</summary>
    private sealed class NonReadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
