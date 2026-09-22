namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>
///     Exercises <see cref="CStructOperationContext"/> directly, independent of a real parse/read operation. Only
///     reachable indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c>
///     partial class.
/// </summary>
[TestClass]
public class CStructOperationContextTests
{
    /// <summary>Each invalid budget explains which setting prevents constructing a usable read context.</summary>
    [TestMethod]
    public void Constructor_InvalidBudgetsIdentifyTheRejectedSetting()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings defaults = ReadOperationSettings.SnapshotReadOptions(null);
        (ReadOperationSettings Settings, string Reason)[] cases =
        [
            (defaults with { MaxPointerDepth = -1, }, "Maximum pointer depth cannot be negative"),
            (defaults with { MaxPointerTargetBytes = -1, }, "Maximum pointer target bytes cannot be negative"),
            (defaults with { MaxArrayElements = -1, }, "Maximum array elements cannot be negative"),
            (defaults with { MaxStringBytes = -1, }, "Read byte limits cannot be negative"),
            (defaults with { MaxTotalBytesRead = -1, }, "Read byte limits cannot be negative"),
            (defaults with { MaxNestingDepth = 0, }, "Maximum nesting depth must be greater than zero"),
        ];

        foreach ((ReadOperationSettings settings, string reason) in cases)
        {
            // Check the diagnostic as well as rejection so callers can correct the responsible option.
            ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
                () => new CStructOperationContext(stream, [], aligned: false, settings));
            Assert.AreEqual("options", failure.ParamName);
            StringAssert.Contains(failure.Message, reason);
        }
    }

    /// <summary>Only a resolved selective dictionary can skip publishing unreferenced layout variables.</summary>
    [TestMethod]
    public void Constructor_PreservesSelectiveAndCaptureAllVariableModes()
    {
        using var stream = new MemoryStream();
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null);
        Assert.IsTrue(new CStructOperationContext(stream, [], false, settings).CaptureAllLayoutVariables);
        Assert.IsFalse(new CStructOperationContext(stream, new LayoutVariables(), false, settings).CaptureAllLayoutVariables);
        Assert.IsTrue(new CStructOperationContext(stream, new LayoutVariables { CaptureAll = true, }, false, settings).CaptureAllLayoutVariables);
    }

    /// <summary>Leaving a nested scope removes its prefix marker and missing fields cannot retain stale qualified values.</summary>
    [TestMethod]
    public void PublishQualified_RemovesStaleValuesAndReleasesPrefixState()
    {
        using var stream = new MemoryStream();
        var context = new CStructOperationContext(stream, [], false, ReadOperationSettings.SnapshotReadOptions(null));
        context.Variables["count"] = new Literal(3);
        context.QualifiedPrefix = "header.";
        context.PublishQualified("count");
        Assert.AreEqual(3, context.Variables["header.count"].Value);
        context.Variables.Remove("count");
        context.PublishQualified("count");
        Assert.IsFalse(context.Variables.ContainsKey("header.count"));
        context.QualifiedPrefix = null;
        Assert.IsFalse(context.HasQualifiedPrefix);
        Assert.IsNull(context.QualifiedPrefix);
        Assert.HasCount(0, context.Variables, "leaving the scope must release its internal prefix marker");
    }

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

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new CStructOperationContext(
                writeOnly,
                [],
                aligned: false,
                ReadOperationSettings.SnapshotReadOptions(null)));
        StringAssert.Contains(failure.Message, "Parsing requires a readable, seekable stream");
        Assert.AreEqual("stream", failure.ParamName);
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
        Assert.AreEqual(1L, entry.Start);
        Assert.AreEqual(3L, entry.End);
        Assert.AreEqual("uint16", entry.TypeName);
        Assert.IsTrue(entry.Bytes.IsEmpty, "a field record carries its range, not a copy of its bytes");
        Assert.AreEqual(4L, context.Stream.Position, "registering a record does not move the stream");
    }

    /// <summary>A zero-width value produces an empty range and carries no bytes.</summary>
    [TestMethod]
    public void RegisterDebugData_ZeroWidthValue_HasEmptyRange()
    {
        using var stream = new MemoryStream([0xAA, 0xBB,]);
        var context = new CStructOperationContext(
            stream,
            [],
            aligned: false,
            ReadOperationSettings.SnapshotReadOptions(null));

        context.RegisterDebugData(curPos: 0, endPos: 0, debugStack: null, value: 0, fieldTypeName: "none");

        Assert.AreEqual(0L, context.DebugMapping[0].Length);
        Assert.IsTrue(context.DebugMapping[0].Bytes.IsEmpty);
    }

    /// <summary>A minimal stream that reports itself as write-only, for exercising the readable-stream guard.</summary>
    private sealed class NonReadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
