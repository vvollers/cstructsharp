namespace CStructSharp.Tests.Generated;

using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks generated-reader cursor positioning, cancellation, union suppression and pointer lifetimes.</summary>
[TestClass]
public class ReadCursorStateBoundaryTests
{
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

    /// <summary>Linked cancellation preserves other read settings and either original token can stop the generated operation.</summary>
    [TestMethod]
    public void WithCancellation_LinksTokensWithoutDroppingSettings()
    {
        Assert.IsNull(ReadCursor.WithCancellation(null, default, out CancellationTokenSource? absent));
        Assert.IsNull(absent);
        foreach (bool cancelOptions in new[] { false, true, })
        {
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            var original = new ReadOptions { MaxArrayElements = 17, TrimFixedText = true, CancellationToken = first.Token, };
            ReadOptions? effective = ReadCursor.WithCancellation(original, second.Token, out CancellationTokenSource? linked);
            using (linked)
            {
                Assert.IsNotNull(linked);
                Assert.IsNotNull(effective);
                Assert.AreEqual(17, effective.MaxArrayElements);
                Assert.IsTrue(effective.TrimFixedText);
                Assert.IsFalse(effective.CancellationToken.IsCancellationRequested);
                (cancelOptions ? first : second).Cancel();
                Assert.IsTrue(effective.CancellationToken.IsCancellationRequested);
            }
        }

        using var only = new CancellationTokenSource();
        ReadOptions? defaults = ReadCursor.WithCancellation(null, only.Token, out CancellationTokenSource? unnecessary);
        Assert.IsNull(unnecessary);
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
    }
}
