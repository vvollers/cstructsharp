namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Where collected constraint violations and the offset base meet the reads that move or buffer their input: a record
///     sequence qualifies each violation with its record, a stream read reports the stream's own positions, and a buffered read
///     that runs again does not add a violation twice.
/// </summary>
[TestClass]
public class ConstraintReportingFollowUpTests
{
    private const string Record = "struct r { uint8 a @expect(a == 1); };";

    /// <summary>A violation collected from record 3 of a sequence names the record and its position in the input, as a thrown failure does.</summary>
    [TestMethod]
    public void RecordSequence_QualifiesCollectedViolationsWithTheirRecord()
    {
        var layout = new CStruct(Record);
        var found = new List<CStructConstraintException>();
        _ = layout.ParseMany(new byte[] { 1, 2, 1, 3 }, "r", options: new ReadOptions { ConstraintViolations = found }).ToList();

        Assert.AreEqual(2, found.Count);
        StringAssert.StartsWith(found[0].Path, "[1]");
        Assert.AreEqual(2L, found[0].Offset);
        StringAssert.StartsWith(found[1].Path, "[3]");
        Assert.AreEqual(4L, found[1].Offset);
    }

    /// <summary>A record sequence over memory adds the offset base once to a collected violation.</summary>
    [TestMethod]
    public void RecordSequence_AddsTheOffsetBaseOnceToCollectedViolations()
    {
        var layout = new CStruct(Record);
        var found = new List<CStructConstraintException>();
        _ = layout.ParseMany(new byte[] { 1, 2 }, "r", options: new ReadOptions { ConstraintViolations = found, OffsetBase = 1000 }).ToList();

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual(1002L, found[0].Offset);
    }

    /// <summary>The records of a stream at a non-zero position report the stream's positions, with or without an offset base.</summary>
    [TestMethod]
    public void RecordSequence_OverAStream_IgnoresTheOffsetBase()
    {
        var layout = new CStruct(Record);
        byte[] data = [9, 9, 9, 1, 2];

        long? Thrown(long offsetBase) => Assert.Throws<CStructConstraintException>(() => layout.ParseMany(new MemoryStream(data) { Position = 3 }, "r", options: new ReadOptions { OffsetBase = offsetBase }).ToList()).Offset;
        Assert.AreEqual(Thrown(0), Thrown(1000));

        long? Collected(long offsetBase)
        {
            var found = new List<CStructConstraintException>();
            _ = layout.ParseMany(new MemoryStream(data) { Position = 3 }, "r", options: new ReadOptions { OffsetBase = offsetBase, ConstraintViolations = found }).ToList();
            return found.Single().Offset;
        }

        Assert.AreEqual(Collected(0), Collected(1000));
        Assert.AreEqual(5L, Collected(1000));
    }

    /// <summary>A runtime read of a stream, awaited or not, ignores the offset base for a failure and for a collected violation alike.</summary>
    /// <returns>A task that completes after the offsets are compared.</returns>
    [TestMethod]
    public async Task StreamRead_IgnoresTheOffsetBase()
    {
        var layout = new CStruct("struct s { uint8 pad; uint8 a @expect(a == 1); };");
        byte[] data = [0, 2];

        async Task<long?> Async(long offsetBase) => (await Assert.ThrowsAsync<CStructConstraintException>(async () => await layout.ParseAsync(new MemoryStream(data), "s", options: new ReadOptions { OffsetBase = offsetBase }))).Offset;
        Assert.AreEqual(await Async(0), await Async(1000));

        long? Sync(long offsetBase, bool collect)
        {
            var found = new List<CStructConstraintException>();
            var options = new ReadOptions { OffsetBase = offsetBase, ConstraintViolations = collect ? found : null };
            if (!collect)
            {
                return Assert.Throws<CStructConstraintException>(() => layout.Parse(new MemoryStream(data), "s", options: options)).Offset;
            }

            _ = layout.Parse(new MemoryStream(data), "s", options: options);
            return found.Single().Offset;
        }

        Assert.AreEqual(Sync(0, false), Sync(1000, false));
        Assert.AreEqual(Sync(0, true), Sync(1000, true));
    }

    /// <summary>The message of a collected violation is composed on first use and carries the field, the condition and the position facts.</summary>
    [TestMethod]
    public void CollectedViolation_ComposesItsMessageFromItsFacts()
    {
        var layout = new CStruct(Record);
        var found = new List<CStructConstraintException>();
        _ = layout.ParseMany(new byte[] { 1, 2 }, "r", options: new ReadOptions { ConstraintViolations = found }).ToList();

        string message = found.Single().Message;
        StringAssert.Contains(message, "Invalid value for 'a': it must satisfy 'a == 1'");
        StringAssert.Contains(message, "offset 2");
        Assert.AreEqual(message, found.Single().Message, "the message is the same each time it is read");
    }

    /// <summary>Violations collected by an operation on a path (a nested struct read by its path) carry the operation's path.</summary>
    [TestMethod]
    public void PathRead_CollectedViolationsCarryThePath()
    {
        var layout = new CStruct("struct inner { uint8 a @expect(a == 1); }; struct outer { uint8 x; inner part; };");
        var found = new List<CStructConstraintException>();
        _ = layout.ReadValue(new byte[] { 7, 2 }, "outer.part", options: new ReadOptions { ConstraintViolations = found });

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("outer.part", found[0].Path);
    }

    /// <summary>A buffered read that needs bytes past its first copy and runs again reports each violation once, as the span read does.</summary>
    /// <returns>A task that completes after the forms are compared.</returns>
    [TestMethod]
    public async Task BufferedRerun_AddsEachViolationOnce()
    {
        var layout = new CStruct("struct far { uint8 a @expect(a == 9); uint16* p; uint8 z; };", pointerSize: 8);

        // The pointer's target lies at 100, past the 65 bytes the first buffer holds, so the buffered forms read again.
        byte[] bytes = [1, 100, 0, 0, 0, 0, 0, 0, 0, 5, .. new byte[200]];
        int Spanned()
        {
            var found = new List<CStructConstraintException>();
            _ = layout.Parse(bytes, "far", options: new ReadOptions { MaxTotalBytesRead = 64, ConstraintViolations = found });
            return found.Count;
        }

        Assert.AreEqual(1, Spanned());

        var sequence = new List<CStructConstraintException>();
        _ = layout.Parse(ChunkedSequence.Of(bytes, 3), "far", options: new ReadOptions { MaxTotalBytesRead = 64, ConstraintViolations = sequence });
        Assert.AreEqual(1, sequence.Count, "multi-segment sequence");

        var seekable = new List<CStructConstraintException>();
        _ = await layout.ParseAsync(new MemoryStream(bytes, writable: false), "far", options: new ReadOptions { MaxTotalBytesRead = 64, ConstraintViolations = seekable });
        Assert.AreEqual(1, seekable.Count, "seekable stream");

        var unseekable = new List<CStructConstraintException>();
        _ = await layout.ParseAsync(new AsyncStreamBufferTests.NonSeekableStream(bytes), "far", options: new ReadOptions { MaxTotalBytesRead = 64, ConstraintViolations = unseekable });
        Assert.AreEqual(1, unseekable.Count, "stream that cannot seek");
    }
}
