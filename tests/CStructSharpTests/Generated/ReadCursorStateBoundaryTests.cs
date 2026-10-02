namespace CStructSharp.Tests.Generated;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks generated-reader cursor positioning, cancellation, union suppression and pointer lifetimes.</summary>
[TestClass]
public class ReadCursorStateBoundaryTests
{
    /// <summary>
    ///     Sequence buffering first copies at most the byte budget plus one byte; a shorter copy tells the reader that the
    ///     input continues and how long it is, and a whole copy passes the caller's options through unchanged.
    /// </summary>
    [TestMethod]
    public void ReadSequence_FirstCopiesTheBudgetPlusOne()
    {
        byte[] source = [17, 29, 41,];
        foreach (long budget in new long[] { 0, 1, 2, 3, long.MaxValue, })
        {
            var options = new ReadOptions { MaxTotalBytesRead = budget, };
            (byte[] Copy, ReadOptions? Options) seen = ReadCursor.ReadSequence<CopyReader, (byte[], ReadOptions?)>(new ReadOnlySequence<byte>(source), default, options);
            int expected = budget < source.Length ? (int)budget + 1 : source.Length;
            CollectionAssert.AreEqual(source[..expected], seen.Copy);
            if (expected < source.Length)
            {
                Assert.AreEqual(source.Length, seen.Options!.ContinuedInputLength, "the copy records the whole sequence's length");
                Assert.AreEqual(budget, seen.Options.MaxTotalBytesRead);
            }
            else
            {
                Assert.AreSame(options, seen.Options, "a whole copy reads with the caller's options");
            }
        }
    }

    /// <summary>
    ///     An element array that runs past a partly buffered source asks for more of the input even when the budget is
    ///     so large that the end of the first element past the budget overflows a <see langword="long"/>; it never
    ///     reports a short read from the buffered part alone.
    /// </summary>
    [TestMethod]
    public void TakeElements_PastAPartialSource_AsksForMoreInputAtTheLargestBudget()
    {
        foreach (long continued in new long[] { CStructSharp.Streams.BufferedInput.UnknownLength, 64, })
        {
            var cursor = new ReadCursor(new byte[4], new ReadOptions { MaxTotalBytesRead = long.MaxValue, ContinuedInputLength = continued, });
            try
            {
                cursor.TakeElements(10, 1, "values", "uint8");
                Assert.Fail("The array crosses the buffered bytes, so the read needs more of the input.");
            }
            catch (CStructSharp.Streams.BufferedInputShortfallException shortfall)
            {
                Assert.AreEqual(10, shortfall.NeededLength, "the array's end, from the source's first byte");
            }
        }
    }

    /// <summary>Skipping to the exact end, including an empty skip there, does not spend the read budget.</summary>
    [TestMethod]
    public void Skip_AllowsTheExactRemainingLength()
    {
        var cursor = new ReadCursor(new byte[3], new ReadOptions { MaxTotalBytesRead = 0, });
        cursor.Skip(3, "padding", "uint8");
        cursor.Skip(0, "padding", "uint8");
        Assert.AreEqual(3, cursor.Position);
        Assert.AreEqual(0, cursor.Remaining);
    }

    /// <summary>A cancelled operation cannot enter another composite after the cursor was constructed.</summary>
    [TestMethod]
    public void EnterComposite_ObservesCancellationBeforeChangingDepth()
    {
        using var cancellation = new CancellationTokenSource();
        var cursor = new ReadCursor(new byte[1], new ReadOptions { CancellationToken = cancellation.Token, });
        cancellation.Cancel();
        try
        {
            cursor.EnterComposite("child", "record");
            Assert.Fail("Entering a composite must observe cancellation.");
        }
        catch (OperationCanceledException)
        {
            Assert.AreEqual(0, cursor.Position);
        }
    }

    /// <summary>A rejected negative read cannot refund bytes and allow a later read past the total-byte limit.</summary>
    [TestMethod]
    public void NegativeRead_DoesNotRefundTheBudget()
    {
        var cursor = new ReadCursor(new byte[1], new ReadOptions { MaxTotalBytesRead = 0, });
        try
        {
            cursor.Take(-1, "invalid", "uint8");
            Assert.Fail("A negative byte count must be rejected.");
        }
        catch (CStructReadException)
        {
            cursor.Position = 0;
        }

        try
        {
            cursor.Take(1, "value", "uint8");
            Assert.Fail("The failed read must not increase the available byte budget.");
        }
        catch (CStructReadLimitException)
        {
            Assert.AreEqual(0, cursor.Position);
        }
    }

    /// <summary>Position zero and the end are valid; seeking outside the input reports the requested field.</summary>
    [TestMethod]
    public void Positions_AllowBothBoundariesAndRejectOutsideSeeks()
    {
        var cursor = new ReadCursor(new byte[2]) { Position = 0, };
        cursor.Seek(2, "tail", "uint8");
        Assert.AreEqual(2, cursor.Position);
        cursor.Seek(0, "head", "uint8");
        Assert.AreEqual(0, cursor.Position);
        foreach (int position in new[] { -1, 3, })
        {
            // The seek guard itself must categorize an invalid placement before a later byte read occurs.
            CStructReadException failure = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[2], path: "root").Seek(position, "field", "uint8"));
            Assert.AreEqual("field", failure.Member);
            Assert.AreEqual("root", failure.Path);
        }
    }

    /// <summary>Union suppression nests and never overrides an explicit request not to follow pointers.</summary>
    [TestMethod]
    public void UnionScopes_RestorePointerFollowingWithoutEnablingDisabledPointers()
    {
        foreach (bool follow in new[] { false, true, })
        {
            var cursor = new ReadCursor(new byte[4], new ReadOptions { DereferencePointers = follow, });
            Assert.AreEqual(follow, cursor.FollowsPointers);
            cursor.EnterUnion();
            cursor.EnterUnion();
            Assert.IsFalse(cursor.FollowsPointers);
            cursor.ExitUnion();
            Assert.IsFalse(cursor.FollowsPointers);
            cursor.ExitUnion();
            Assert.AreEqual(follow, cursor.FollowsPointers);
        }
    }

    /// <summary>Leaving a pointer target releases both its depth slot and cycle marker so a later sibling may read it.</summary>
    [TestMethod]
    public void ExitPointer_AllowsASiblingToReadTheSameTarget()
    {
        var cursor = new ReadCursor(new byte[] { 99, 17, 29, }, new ReadOptions { MaxPointerDepth = 1, });
        for (int repetition = 0; repetition < 3; repetition++)
        {
            int resume = cursor.EnterPointer(1, 1, 1, "uint8", "pointer", "uint8*");
            Assert.AreEqual(0, resume);
            Assert.AreEqual((byte)17, cursor.Take(1, "pointer", "uint8")[0]);
            cursor.ExitPointer(resume);
            Assert.AreEqual(0, cursor.Position);
        }
    }

    /// <summary>The awaitable stream form links both tokens, preserves the other read settings, and either original token stops the read.</summary>
    /// <returns>A task that completes after every token combination is checked.</returns>
    [TestMethod]
    public async Task ReadStreamAsync_LinksTokensWithoutDroppingSettings()
    {
        byte[] bytes = [1, 2,];
        ReadOptions? absent = await ReadCursor.ReadStreamAsync<OptionsReader, ReadOptions?>(new MemoryStream(bytes), default, null);
        Assert.IsNull(absent, "neither token can cancel and no options were given");
        foreach (bool cancelOptions in new[] { false, true, })
        {
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            var original = new ReadOptions { MaxArrayElements = 17, TrimFixedText = true, CancellationToken = first.Token, };
            bool cancelled = await ReadCursor.ReadStreamAsync<CancellingReader, bool>(new MemoryStream(bytes), new CancellingReader(cancelOptions ? first : second), original, second.Token);
            Assert.IsTrue(cancelled, "the linked token follows the cancelled original");
        }

        using var only = new CancellationTokenSource();
        ReadOptions? defaults = await ReadCursor.ReadStreamAsync<OptionsReader, ReadOptions?>(new MemoryStream(bytes), default, null, only.Token);
        Assert.IsNotNull(defaults);
        Assert.AreEqual(only.Token, defaults.CancellationToken);
    }

    /// <summary>The largest signed address is valid; the next unsigned domain is rejected with its original overflow cause.</summary>
    [TestMethod]
    public void PointerAddress_ReportsTheSignedPositionBoundary()
    {
        byte[] maximum = [255, 255, 255, 255, 255, 255, 255, 127,];
        var cursor = new ReadCursor(maximum);
        Assert.AreEqual(long.MaxValue, cursor.TakePointerAddress(8, true, "pointer", "uint8*"));
        CStructReadException failure = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[] { 0, 0, 0, 0, 0, 0, 0, 128, }).TakePointerAddress(8, true, "pointer", "uint8*"));
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
        StringAssert.Contains(failure.InnerException.Message, "Pointer address exceeds the signed stream-position range");
        Assert.AreEqual("pointer", failure.Member);
    }

    /// <summary>Identifier reads include exactly sixteen bytes and still charge short input to the total budget.</summary>
    [TestMethod]
    public void Identifiers_RespectWidthOrderAndShortReadBudgets()
    {
        byte[] source = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,];
        var network = new ReadCursor(source);
        Assert.AreEqual(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), network.TakeGuid(true, "id", "uuid"));
        Assert.AreEqual(16, network.Position);
        var windows = new ReadCursor(source);
        Assert.AreEqual(Guid.Parse("33221100-5544-7766-8899-aabbccddeeff"), windows.TakeGuid(false, "id", "guid"));
        CStructReadException shortRead = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[15]).TakeGuid(true, "id", "uuid"));
        StringAssert.Contains(shortRead.Message, "Not enough bytes for a 16-byte identifier");
        Assert.Throws<CStructReadLimitException>(() => new ReadCursor(new byte[15], new ReadOptions { MaxTotalBytesRead = 14, }).TakeGuid(true, "id", "uuid"));
    }

    /// <summary>LEB128 reads continue across payload bytes, sign-extend signed values and categorize overflow at the field.</summary>
    [TestMethod]
    public void Leb128_ReadsMultipleBytesAndAttachesOverflowContext()
    {
        var unsigned = new ReadCursor(new byte[] { 0xE5, 0x8E, 0x26, 99, });
        Assert.AreEqual(624485UL, unsigned.TakeLeb128(32, false, "value", "uleb128"));
        Assert.AreEqual(3, unsigned.Position);
        var signed = new ReadCursor(new byte[] { 0x9B, 0xF1, 0x59, });
        Assert.AreEqual(unchecked((ulong)-624485L), signed.TakeLeb128(32, true, "value", "sleb128"));
        CStructReadException failure = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[] { 255, 255, 255, 255, 16, }, path: "root").TakeLeb128(32, false, "value", "uleb128"));
        Assert.AreEqual("value", failure.Member);
        Assert.AreEqual("uleb128", failure.MemberType);
        Assert.AreEqual("root", failure.Path);
        StringAssert.StartsWith(failure.Message, "LEB128 integer exceeds its declared width");
    }

    /// <summary>A span reader that returns the options its run was given.</summary>
    private readonly struct OptionsReader : IBufferedReader<ReadOptions?>
    {
        /// <inheritdoc/>
        public ReadOptions? Read(ReadOnlySpan<byte> source, ReadOptions? options, out long consumed)
        {
            consumed = 0;
            return options;
        }
    }

    /// <summary>A span reader that returns a copy of the bytes it was handed and the options of its run.</summary>
    private readonly struct CopyReader : IBufferedReader<(byte[], ReadOptions?)>
    {
        /// <inheritdoc/>
        public (byte[], ReadOptions?) Read(ReadOnlySpan<byte> source, ReadOptions? options, out long consumed)
        {
            consumed = source.Length;
            return (source.ToArray(), options);
        }
    }

    /// <summary>
    ///     A span reader that checks its run kept the caller's settings, cancels one of the original tokens, and reports
    ///     whether the run's token followed.
    /// </summary>
    private readonly struct CancellingReader : IBufferedReader<bool>
    {
        private readonly CancellationTokenSource stop;

        /// <summary>Creates the reader.</summary>
        /// <param name="stop">The original token's source the run cancels.</param>
        public CancellingReader(CancellationTokenSource stop) => this.stop = stop;

        /// <inheritdoc/>
        public bool Read(ReadOnlySpan<byte> source, ReadOptions? options, out long consumed)
        {
            consumed = 0;
            Assert.IsNotNull(options);
            Assert.AreEqual(17, options.MaxArrayElements);
            Assert.IsTrue(options.TrimFixedText);
            Assert.IsFalse(options.CancellationToken.IsCancellationRequested);
            this.stop.Cancel();
            return options.CancellationToken.IsCancellationRequested;
        }
    }
}
