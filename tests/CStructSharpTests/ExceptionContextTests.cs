namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="ExceptionContext"/> directly, independent of a real read/write/path-resolution failure.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class ExceptionContextTests
{
    /// <summary>A plain segment and an indexed segment must join with dots, and the index must use bracket syntax.</summary>
    [TestMethod]
    public void FormatPath_MixOfPlainAndIndexedSegments_JoinsWithDotsAndBrackets()
    {
        PathSegment[] segments =
        [
            new PathSegment("root", []),
            new PathSegment("items", [3]),
            new PathSegment("value", []),
        ];

        string? formatted = ExceptionContext.FormatPath(segments);

        Assert.AreEqual("root.items[3].value", formatted);
    }

    /// <summary>An empty path resolves to an empty string, not null and not a stray separator.</summary>
    [TestMethod]
    public void FormatPath_NoSegments_ReturnsEmptyString()
    {
        string? formatted = ExceptionContext.FormatPath([]);

        Assert.AreEqual(string.Empty, formatted);
    }

    /// <summary>
    ///     A fresh exception has no path or offset yet, and the supplied segments and stream position are both
    ///     readable.
    /// </summary>
    [TestMethod]
    public void Attach_FreshException_SetsPathAndOffsetFromSegmentsAndStreamPosition()
    {
        var exception = new CStructPathException("boom");
        using var stream = new MemoryStream(new byte[8]) { Position = 5, };
        PathSegment[] segments = [new PathSegment("root", []), new PathSegment("value", [2]),];

        ExceptionContext.Attach(exception, segments, stream);

        Assert.AreEqual("root.value[2]", exception.Path);
        Assert.AreEqual(5L, exception.Offset);
    }

    /// <summary>
    ///     CStructException.AttachContext only fills in a path or offset that is not already set, so a lower layer's
    ///     more precise context must survive being passed back up through additional Attach calls from outer layers.
    /// </summary>
    [TestMethod]
    public void Attach_ExceptionAlreadyHasContext_DoesNotOverwriteExistingPathOrOffset()
    {
        var exception = new CStructPathException("boom");
        using var innerStream = new MemoryStream(new byte[4]) { Position = 1, };
        ExceptionContext.Attach(exception, [new PathSegment("inner", []),], innerStream);

        using var outerStream = new MemoryStream(new byte[4]) { Position = 3, };
        ExceptionContext.Attach(exception, [new PathSegment("outer", []),], outerStream);

        Assert.AreEqual("inner", exception.Path);
        Assert.AreEqual(1L, exception.Offset);
    }

    /// <summary>
    ///     A stream that cannot report its position (here, because it has already been disposed) must not turn a
    ///     diagnostic lookup into a second, unrelated failure - the offset is simply left unknown.
    /// </summary>
    [TestMethod]
    public void Attach_StreamPositionThrows_LeavesOffsetNullInsteadOfPropagating()
    {
        var exception = new CStructPathException("boom");
        var stream = new MemoryStream(new byte[4]);
        stream.Dispose();

        ExceptionContext.Attach(exception, [new PathSegment("root", []),], stream);

        Assert.AreEqual("root", exception.Path);
        Assert.IsNull(exception.Offset);
    }
}
