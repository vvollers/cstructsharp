namespace CStructSharp.Tests.Generated;

using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks element, block and terminator boundaries in the cursor used by generated readers.</summary>
[TestClass]
public class ReadCursorArrayBoundaryTests
{
    /// <summary>Runs one generated-reader operation against a borrowed cursor without capturing a ref struct.</summary>
    /// <param name="cursor">The cursor whose position and budget the operation may change.</param>
    private delegate void CursorOperation(ref ReadCursor cursor);

    /// <summary>Whole elements are consumed exactly, including zero-width and empty arrays, without losing the budget already spent.</summary>
    [TestMethod]
    public void TakeElements_ConsumesExactBytesAndPreservesEmptyArrays()
    {
        byte[] source = [99, 1, 2, 3, 4, 5, 6,];
        var cursor = new ReadCursor(source, new ReadOptions { MaxTotalBytesRead = 7, });
        cursor.Take(1, "header", "uint8");
        Assert.AreEqual(0, cursor.TakeElements(0, 2, "items", "uint16").Length);
        Assert.AreEqual(0, cursor.TakeElements(3, 0, "items", "empty").Length);
        Assert.AreEqual(1, cursor.Position);
        CollectionAssert.AreEqual(source[1..], cursor.TakeElements(3, 2, "items", "uint16").ToArray());
        Assert.AreEqual(7, cursor.Position);
        Assert.AreEqual(0, cursor.Remaining);
    }

    /// <summary>When available whole elements fit the budget, a partial trailing element is reported as a short read.</summary>
    [TestMethod]
    public void TakeElements_PartialElementReportsItsRemainderBeforeTheBudget()
    {
        // Two complete two-byte elements fit the budget; the fifth byte is an incomplete third element.
        CStructException failure = Failure(new byte[5], new ReadOptions { MaxTotalBytesRead = 4, }, static (ref ReadCursor cursor) => cursor.TakeElements(3, 2, "items", "uint16"), 5);
        Assert.IsInstanceOfType<CStructReadException>(failure);
        Assert.IsNotInstanceOfType<CStructReadLimitException>(failure);
        StringAssert.Contains(failure.Message, "Not enough bytes: needed 2, available 1");
    }

    /// <summary>A byte limit fails after the first unaffordable element, counting bytes consumed before the array.</summary>
    [TestMethod]
    public void TakeElements_BudgetFailureStopsAtTheCrossingElement()
    {
        // The header spends one byte: only one of the remaining two-byte elements fits a four-byte total budget.
        CStructException failure = Failure(
            new byte[9],
            new ReadOptions { MaxTotalBytesRead = 4, },
            static (ref ReadCursor cursor) =>
            {
                cursor.Take(1, "header", "uint8");
                cursor.TakeElements(4, 2, "items", "uint16");
            },
            5);
        Assert.IsInstanceOfType<CStructReadLimitException>(failure);
        StringAssert.Contains(failure.Message, "total read-byte limit");

        // The available input also bounds the failure position when the crossing element is incomplete.
        CStructException partial = Failure(new byte[3], new ReadOptions { MaxTotalBytesRead = 1, }, static (ref ReadCursor cursor) => cursor.TakeElements(2, 2, "items", "uint16"), 2);
        Assert.IsInstanceOfType<CStructReadLimitException>(partial);

        // The element count exceeds Int32 bytes, but the first unaffordable element must still report the limit.
        CStructException oversized = Failure(new byte[3], new ReadOptions { MaxTotalBytesRead = 1, }, static (ref ReadCursor cursor) => cursor.TakeElements(int.MaxValue, 2, "items", "uint16"), 2);
        Assert.IsInstanceOfType<CStructReadLimitException>(oversized);
    }

    /// <summary>Block reads cross 64 KiB on whole three-byte element boundaries and return exactly the requested region.</summary>
    [TestMethod]
    public void TakeInBlocks_CrossesTheBlockBoundaryWithoutPadding()
    {
        const int byteCount = 65538;
        byte[] source = new byte[byteCount + 2];
        source[2] = 17;
        source[^1] = 29;
        var cursor = new ReadCursor(source, new ReadOptions { MaxTotalBytesRead = byteCount, }) { Position = 2, };
        Assert.AreEqual(0, cursor.TakeInBlocks(0, 3, "items", "uint24").Length);
        Assert.AreEqual(0, cursor.TakeInBlocks(4, 0, "items", "empty").Length);
        Assert.AreEqual(2, cursor.Position);
        CollectionAssert.AreEqual(source[2..], cursor.TakeInBlocks(byteCount / 3, 3, "items", "uint24").ToArray());
        Assert.AreEqual(source.Length, cursor.Position);
    }

    /// <summary>A short block names the block's byte need, while a budget failure leaves the cursor before that block.</summary>
    [TestMethod]
    public void TakeInBlocks_ReportsShortAndUnaffordableFinalBlocks()
    {
        // 21846 three-byte elements need 65535 bytes in the first block and three bytes in the final block.
        CStructException shortRead = Failure(new byte[65537], null, static (ref ReadCursor cursor) => cursor.TakeInBlocks(21846, 3, "items", "uint24"), 65537);
        StringAssert.Contains(shortRead.Message, "Not enough bytes: needed 3, available 2");
        CStructException limited = Failure(new byte[65538], new ReadOptions { MaxTotalBytesRead = 65535, }, static (ref ReadCursor cursor) => cursor.TakeInBlocks(21846, 3, "items", "uint24"), 65535);
        Assert.IsInstanceOfType<CStructReadLimitException>(limited);
    }

    /// <summary>Read-to-end counting does not consume bytes and includes the configured maximum element count.</summary>
    [TestMethod]
    public void CountToEnd_RequiresWholeElementsAndHonorsTheInclusiveLimit()
    {
        var cursor = new ReadCursor(new byte[5], new ReadOptions { MaxArrayElements = 2, }) { Position = 1, };
        Assert.AreEqual(0, cursor.CountToEnd(0, "items", "items", "empty"));
        Assert.AreEqual(2, cursor.CountToEnd(2, "items", "items", "uint16"));
        Assert.AreEqual(1, cursor.Position);
        CStructException partial = Failure(new byte[3], null, static (ref ReadCursor current) => current.CountToEnd(2, "items", "items", "uint16"), 0);
        StringAssert.Contains(partial.Message, "remaining 3 bytes are not a whole number of 2-byte elements: items");
        CStructException limited = Failure(new byte[4], new ReadOptions { MaxArrayElements = 1, }, static (ref ReadCursor current) => current.CountToEnd(2, "items", "items", "uint16"), 0);
        Assert.IsInstanceOfType<CStructReadLimitException>(limited);
        StringAssert.Contains(limited.Message, "Array length 2 exceeds MaxArrayElements (1)");
    }

    /// <summary>Only a completely zero element terminates an array, and the terminator is not counted or consumed.</summary>
    [TestMethod]
    public void CountTerminated_DistinguishesZeroElementsFromZeroBytes()
    {
        byte[] source = [99, 1, 0, 0, 2, 0, 0,];
        var cursor = new ReadCursor(source, new ReadOptions { MaxArrayElements = 2, }) { Position = 1, };
        Assert.AreEqual(0, cursor.CountTerminated(0, "items", "items", "empty"));
        Assert.AreEqual(2, cursor.CountTerminated(2, "items", "items", "uint16"));
        Assert.AreEqual(1, cursor.Position);
        CStructException limited = Failure(source[1..], new ReadOptions { MaxArrayElements = 1, }, static (ref ReadCursor current) => current.CountTerminated(2, "items", "items", "uint16"), 0);
        Assert.IsInstanceOfType<CStructReadLimitException>(limited);
        StringAssert.Contains(limited.Message, "Array length 2 exceeds MaxArrayElements (1)");
        CStructException partial = Failure(new byte[] { 1, 0, 0, }, null, static (ref ReadCursor current) => current.CountTerminated(2, "items", "items", "uint16"), 0);
        StringAssert.Contains(partial.Message, "Terminated array has no terminating zero element: items");
    }

    /// <summary>Cancellation is observed before both block and per-element array readers consume input.</summary>
    [TestMethod]
    public void ArrayReaders_ObserveCancellationAtTheirReadBoundary()
    {
        using var cancelled = new CancellationTokenSource();
        var options = new ReadOptions { CancellationToken = cancelled.Token, };
        var elements = new ReadCursor(new byte[4], options);
        var blocks = new ReadCursor(new byte[4], options);
        cancelled.Cancel();
        try
        {
            elements.TakeElements(2, 2, "items", "uint16");
            Assert.Fail("A cancelled element read must not succeed.");
        }
        catch (OperationCanceledException)
        {
            Assert.AreEqual(0, elements.Position);
        }

        try
        {
            blocks.TakeInBlocks(2, 2, "items", "uint16");
            Assert.Fail("A cancelled block read must not succeed.");
        }
        catch (OperationCanceledException)
        {
            Assert.AreEqual(0, blocks.Position);
        }
    }

    /// <summary>Captures a categorized failure and verifies the generated operation's member, path and final byte position.</summary>
    /// <param name="source">Input bytes borrowed for the operation.</param>
    /// <param name="options">Read limits, or the defaults.</param>
    /// <param name="operation">The operation expected to fail on field items.</param>
    /// <param name="expectedPosition">Expected input-relative cursor and diagnostic offset.</param>
    /// <returns>The failure for its scenario-specific type and message assertions.</returns>
    private static CStructException Failure(byte[] source, ReadOptions? options, CursorOperation operation, int expectedPosition)
    {
        var cursor = new ReadCursor(source, options, "root");
        CStructException? failure = null;
        try
        {
            operation(ref cursor);
        }
        catch (CStructException exception)
        {
            cursor.Complete(exception);
            failure = exception;
        }

        Assert.IsNotNull(failure, "The invalid input or exhausted budget must fail.");
        Assert.AreEqual(expectedPosition, cursor.Position);
        Assert.AreEqual((long)expectedPosition, failure.Offset);
        Assert.AreEqual("root", failure.Path);
        Assert.AreEqual("items", failure.Member);
        return failure;
    }
}
