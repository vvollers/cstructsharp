namespace CStructSharpTests.Generated;

using System;
using System.Text;
using System.Threading;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks generated output boundaries through bytes, field diagnostics and cancellation.</summary>
[TestClass]
public class WriteCursorBoundaryTests
{
    /// <summary>Seeking pads only the new extent, supports exact endpoints and rejects out-of-range addresses.</summary>
    [TestMethod]
    public void Seek_PreservesExistingBytesAndClearsOnlyNewPadding()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6];
        var cursor = new WriteCursor(bytes, path: "root");
        cursor.Reserve(2, "head", "uint16");
        cursor.Position = 0;
        cursor.Position = 2;
        cursor.Seek(6, "tail", "uint8");
        Assert.AreEqual(6, cursor.Position);
        Assert.AreEqual(6, cursor.Length);
        cursor.Seek(1, "head", "uint8");
        Assert.AreEqual(1, cursor.Position);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0, 0, 0 }, bytes);
        cursor.Dispose();
        Assert.IsFalse(cursor.IsGrowable);
        Assert.AreEqual(6, cursor.Written.Length);

        // These positions cannot be represented by a cursor over a fixed two-byte destination.
        foreach (long invalid in new long[] { -1, 3, (long)int.MaxValue + 1 })
        {
            CStructWriteException failure = Assert.Throws<CStructWriteException>(() =>
            {
                var failing = new WriteCursor(new byte[2], path: "root");
                failing.Seek(invalid, "tail", "uint8");
            });
            Assert.AreEqual("tail", failure.Member);
            Assert.AreEqual("root", failure.Path);
        }

        // Position assignment cannot create a negative byte offset.
        Assert.Throws<CStructWriteException>(() => new WriteCursor(new byte[1]) { Position = -1, });
    }

    /// <summary>A bitfield unit preserves its prefix and clears a newly exposed tail after a nonzero origin.</summary>
    [TestMethod]
    public void Unit_ExtendsFromTheExistingEndAndAlignUsesTheCompositeOrigin()
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8];
        var cursor = new WriteCursor(bytes);
        cursor.Reserve(3, "head", "uint8");
        Span<byte> unit = cursor.Unit(2, 4, "bits", "uint32");
        CollectionAssert.AreEqual(new byte[] { 3, 0, 0, 0 }, unit.ToArray());
        Assert.AreEqual(6, cursor.Position);
        cursor.Seek(2, "bits", "uint32");
        cursor.Align(4, 1, "bits", "uint32");
        Assert.AreEqual(5, cursor.Position);
        Assert.AreEqual(6, cursor.Length);
    }

    /// <summary>Growing output retains old values, zero-fills seek gaps and returns an independent result array.</summary>
    [TestMethod]
    public void GrowableOutput_PreservesValuesAcrossMoreThanOneRental()
    {
        var cursor = new WriteCursor(null, "root");
        try
        {
            cursor.Reserve(200, "head", "uint8").Fill(0xAB);
            cursor.Seek(1_025, "gap", "uint8");
            cursor.Reserve(1, "tail", "uint8")[0] = 0xCD;
            byte[] result = cursor.ToArray();
            Assert.AreEqual(1_026, result.Length);
            for (int index = 0; index < result.Length; index++)
            {
                Assert.AreEqual(index < 200 ? (byte)0xAB : index == 1_025 ? (byte)0xCD : (byte)0, result[index]);
            }

            cursor.Position = 0;
            cursor.Reserve(1, "head", "uint8")[0] = 1;
            Assert.AreEqual(0xAB, result[0]);
        }
        finally
        {
            cursor.Dispose();
        }

        Assert.IsFalse(cursor.IsGrowable);
        cursor.Dispose();
    }

    /// <summary>Fixed text counts UTF-16 code units, honors byte order and includes padding in its byte limit.</summary>
    [TestMethod]
    public void FixedText_UsesExactCharacterAndEncodedByteBoundaries()
    {
        foreach (bool littleEndian in new[] { true, false })
        {
            byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];
            var cursor = new WriteCursor(bytes, new WriteOptions { MaxStringBytes = 6, });
            cursor.WriteFixedText(3, "\u1234", true, littleEndian, "name", "wchar");
            CollectionAssert.AreEqual(
                littleEndian ? new byte[] { 0x34, 0x12, 0, 0, 0, 0 } : new byte[] { 0x12, 0x34, 0, 0, 0, 0 }, bytes);
        }

        var exact = new WriteCursor(new byte[1], new WriteOptions { MaxStringBytes = 1, });
        exact.WriteFixedText(1, "\u00ff", false, true, "name", "char");
        Assert.AreEqual(255, exact.Written[0]);

        // The limit includes the full padded wide-character buffer, not just its nonzero payload.
        Assert.Throws<CStructWriteLimitException>(() =>
            new WriteCursor(new byte[6], new WriteOptions { MaxStringBytes = 5, })
                .WriteFixedText(3, "a", true, true, "name", "wchar"));

        // A lone surrogate cannot be encoded as strict UTF-16.
        CStructWriteException invalid = Assert.Throws<CStructWriteException>(() =>
            new WriteCursor(new byte[2], path: "root").WriteFixedText(1, "\ud800", true, true, "name", "wchar"));
        Assert.IsInstanceOfType<EncoderFallbackException>(invalid.InnerException);
        Assert.AreEqual("name", invalid.Member);
        Assert.AreEqual("wchar", invalid.MemberType);
        Assert.AreEqual("root", invalid.Path);

        // Null text is distinct from an empty character array.
        Assert.Throws<ArgumentNullException>(() =>
            new WriteCursor(new byte[1]).WriteFixedText(1, null!, false, true, "name", "char"));
    }

    /// <summary>Terminated text includes its encoded terminator in the limit and reports invalid input with its cause.</summary>
    [TestMethod]
    public void TerminatedText_CountsTheTerminatorAndPreservesEncodingFailures()
    {
        var cursor = new WriteCursor(new byte[3], new WriteOptions { MaxStringBytes = 3, });
        cursor.WriteTerminatedString(TerminatedTextEncoding.Utf8, '\0', "é", "name", "cstring");
        CollectionAssert.AreEqual(new byte[] { 0xC3, 0xA9, 0 }, cursor.ToArray());

        // The two-byte payload still needs one byte for its terminator.
        Assert.Throws<CStructWriteLimitException>(() =>
            new WriteCursor(new byte[3], new WriteOptions { MaxStringBytes = 2, })
                .WriteTerminatedString(TerminatedTextEncoding.Utf8, '\0', "é", "name", "cstring"));

        // Strict ASCII must reject this character instead of replacing it.
        CStructWriteException invalid = Assert.Throws<CStructWriteException>(() =>
            new WriteCursor(new byte[3], path: "root")
                .WriteTerminatedString(TerminatedTextEncoding.Ascii, '\0', "é", "name", "cstring"));
        Assert.IsInstanceOfType<EncoderFallbackException>(invalid.InnerException);
        Assert.AreEqual("name", invalid.Member);
        Assert.AreEqual("root", invalid.Path);

        // Null is not a terminated empty string.
        Assert.Throws<ArgumentNullException>(() =>
            new WriteCursor(new byte[1]).WriteTerminatedString(TerminatedTextEncoding.Ascii, '\0', null!, "name", "cstring"));
    }

    /// <summary>Async options preserve settings and link both cancellation sources without allocating a needless source.</summary>
    [TestMethod]
    public void CancellationOptions_LinkBothInputsAndPreserveTheNullDefault()
    {
        Assert.IsNull(WriteCursor.WithCancellation(null, default, out CancellationTokenSource? absent));
        Assert.IsNull(absent);
        using var original = new CancellationTokenSource();
        using var operation = new CancellationTokenSource();
        var options = new WriteOptions { MaxStringBytes = 7, CancellationToken = original.Token, };
        WriteOptions? effective = WriteCursor.WithCancellation(options, operation.Token, out CancellationTokenSource? linked);
        using (linked)
        {
            Assert.IsNotNull(linked);
            Assert.IsNotNull(effective);
            Assert.AreEqual(7L, effective.MaxStringBytes);
            operation.Cancel();
            Assert.IsTrue(effective.CancellationToken.IsCancellationRequested);
            Assert.IsFalse(original.IsCancellationRequested);
        }

        WriteOptions? single = WriteCursor.WithCancellation(null, original.Token, out absent);
        Assert.IsNull(absent);
        Assert.AreEqual(original.Token, single!.CancellationToken);
        var cursor = new WriteCursor(new byte[1], single);
        original.Cancel();
        try
        {
            cursor.EnterComposite("root", null);
            Assert.Fail("Cancellation after construction must stop entry into a composite.");
        }
        catch (OperationCanceledException)
        {
            // The caller's cancellation remains an operation cancellation, not a write-format error.
        }
    }

    /// <summary>Attaching an outer member must preserve an already known inner field and exception identity.</summary>
    [TestMethod]
    public void WithMember_PreservesTheOriginalFailureAndInnermostField()
    {
        var cursor = new WriteCursor(new byte[1], path: "root");
        var failure = new CStructWriteException("specific cause");
        Assert.AreSame(failure, cursor.WithMember(failure, "inner", "uint8"));
        Assert.AreSame(failure, cursor.WithMember(failure, "outer", "container"));
        Assert.AreEqual("inner", failure.Member);
        Assert.AreEqual("uint8", failure.MemberType);
        Assert.AreEqual("root", failure.Path);

        // Context enrichment requires an existing failure object.
        Assert.Throws<ArgumentNullException>(() => new WriteCursor(new byte[1]).WithMember(null!, "member", "uint8"));
    }
}
