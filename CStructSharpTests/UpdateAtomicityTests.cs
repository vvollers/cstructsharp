namespace CStructSharp.Tests;

using System.Collections;
using System.Dynamic;

/// <summary>Defines the validation-before-mutation boundary for public in-place updates.</summary>
[TestClass]
public class UpdateAtomicityTests
{
    /// <summary>Rejects a missing late member before any earlier member reaches the destination.</summary>
    [TestMethod]
    public void LateBindingFailures_DoNotReachDestination_ForEveryObjectShape()
    {
        var cstruct = new CStruct("struct root { uint8 first; uint8 second; };", pointerSize: 1);
        dynamic expando = new ExpandoObject();
        expando.first = (byte)0xA1;
        object[] values =
        [
            new Dictionary<string, object> { ["first"] = (byte)0xA1, },
            expando,
            new MissingSecondPoco { First = 0xA1, },
        ];

        foreach (object value in values)
        {
            AssertValidationFailureLeavesDestinationUnchanged(
                new byte[] { 0x11, 0x22, 0x33, 0x44, },
                1,
                stream => cstruct.UpdateStream(stream, "root", value));
        }
    }

    /// <summary>Rejects a bad late array element without retaining already converted leading elements.</summary>
    [TestMethod]
    public void LateArrayConversion_DoesNotReachDestination()
    {
        var cstruct = new CStruct("struct root { uint8 values[3]; };", pointerSize: 1);

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0x11, 0x22, 0x33, },
            0,
            stream => cstruct.UpdateStream(
                stream,
                "root.values",
                new object[] { (byte)1, (byte)2, "not-a-byte", }));
    }

    /// <summary>Applies the complete logical-write budget before committing any accepted prefix.</summary>
    [TestMethod]
    public void LateWriteBudgetFailure_DoesNotReachDestination()
    {
        var cstruct = new CStruct(
            "struct root { uint8 first; uint8 second; uint8 third; };",
            pointerSize: 1);
        var value = new { first = (byte)1, second = (byte)2, third = (byte)3, };

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0x11, 0x22, 0x33, },
            0,
            stream => cstruct.UpdateStream(
                stream,
                "root",
                value,
                options: new UpdateOptions { MaxTotalBytesWritten = 2, }));
    }

    /// <summary>Rejects a late terminated-string budget failure after staging an ordinary leading member.</summary>
    [TestMethod]
    public void LateStringBudgetFailure_DoesNotReachDestination()
    {
        var cstruct = new CStruct("struct root { uint8 prefix; string value; };", pointerSize: 1);
        var value = new { prefix = (byte)0xA5, value = "too-long", };

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0x11, (byte)'o', (byte)'l', (byte)'d', 0, 0, 0, 0, 0, 0, },
            0,
            stream => cstruct.UpdateStream(
                stream,
                "root",
                value,
                options: new UpdateOptions { MaxStringBytes = 3, }));
    }

    /// <summary>Rejects a late runtime-array element conversion after the count and accepted prefix were staged.</summary>
    [TestMethod]
    public void LateRuntimeArrayConversion_DoesNotReachDestination()
    {
        var cstruct = new CStruct(
            "struct root { uint8 prefix; uint8 count; uint8 values[count]; };",
            pointerSize: 1);
        var value = new
        {
            prefix = (byte)0xA5,
            count = (byte)2,
            values = new object[] { (byte)1, "not-a-byte", },
        };

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0x11, 0x02, 0x22, 0x33, },
            0,
            stream => cstruct.UpdateStream(stream, "root", value));
    }

    /// <summary>Rejects a late pointer conversion before an earlier ordinary member is visible.</summary>
    [TestMethod]
    public void LatePointerFailure_DoesNotReachDestination()
    {
        var cstruct = new CStruct("struct root { uint8 marker; uint8 *target; };", pointerSize: 1);
        var value = new { marker = (byte)0xA5, target = -1L, };

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0x11, 0x02, 0x33, },
            0,
            stream => cstruct.UpdateStream(stream, "root", value));
    }

    /// <summary>Stages scalar-domain, string, array, and union failures after a valid leading member.</summary>
    [TestMethod]
    public void ValueFamilyFailures_DoNotReachDestination()
    {
        var cases = new (CStruct Layout, object Value)[]
        {
            (
                new CStruct(
                    "enum mode : uint8 { ok = 1 }; struct root { uint8 prefix; mode value; };",
                    pointerSize: 1),
                new { prefix = (byte)0xA5, value = "missing", }),
            (
                new CStruct("struct root { uint8 prefix; uint8 flags:4; };", pointerSize: 1),
                new { prefix = (byte)0xA5, flags = 16, }),
            (
                new CStruct("struct root { uint8 prefix; char text[2]; };", pointerSize: 1),
                new { prefix = (byte)0xA5, text = "too long", }),
            (
                new CStruct("struct root { uint8 prefix; uint8 values[2]; };", pointerSize: 1),
                new { prefix = (byte)0xA5, values = new byte[] { 1, }, }),
            (
                new CStruct(
                    "union choice { uint32 wide; uint8 small; }; " +
                    "struct root { uint8 prefix; choice value; };",
                    pointerSize: 1),
                new
                {
                    prefix = (byte)0xA5,
                    value = UnionValue.FromMember("choice", "missing", (byte)1),
                }),
        };

        foreach ((CStruct layout, object value) in cases)
        {
            AssertValidationFailureLeavesDestinationUnchanged(
                new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, },
                0,
                stream => layout.UpdateStream(stream, "root", value));
        }
    }

    /// <summary>Charges staged bitfield preservation reads to the same traversal budget used by path resolution.</summary>
    [TestMethod]
    public void PreservationReads_ShareTheTraversalBudget()
    {
        var cstruct = new CStruct("struct root { uint8 low:4; uint8 high:4; };", pointerSize: 1);
        using var stream = new TrackingStream(new byte[] { 0xA5, });

        Assert.Throws<CStructReadLimitException>(
            () => cstruct.UpdateStream(
                stream,
                "root.high",
                3,
                options: new UpdateOptions { MaxTraversalBytesRead = 0, }));

        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.Snapshot());
        Assert.AreEqual(0, stream.WriteCalls);
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Validates union selection before either clear or preserve policy can affect existing storage.</summary>
    /// <param name="clearUnionStorage">Whether the staged union starts from zeroes or existing storage.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void InvalidUnionSelection_DoesNotReachDestination_ForEitherStoragePolicy(bool clearUnionStorage)
    {
        var cstruct = new CStruct("union choice { uint16 wide; uint8 small; };", pointerSize: 1);
        UnionValue invalid = UnionValue.FromMember("choice", "missing", (byte)1);

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0x34, 0x12, },
            0,
            stream => cstruct.UpdateStream(
                stream,
                "choice",
                invalid,
                options: new UpdateOptions { ClearUnionStorage = clearUnionStorage, }));
    }

    /// <summary>Refuses to synthesize missing neighboring bits when an existing storage unit is truncated.</summary>
    [TestMethod]
    public void TruncatedBitfieldStorage_DoesNotReachDestination()
    {
        var cstruct = new CStruct("struct root { uint16 low:4; uint16 high:12; };", pointerSize: 1);

        AssertValidationFailureLeavesDestinationUnchanged(
            new byte[] { 0xA5, },
            0,
            stream => cstruct.UpdateStream(stream, "root.low", 3));
    }

    /// <summary>Rejects absolute and relative pointer targets whose selected replacement starts at the old end.</summary>
    /// <param name="addressingMode">The pointer addressing convention used by the stored byte.</param>
    /// <param name="origin">The physical origin added to a relative stored address.</param>
    /// <param name="storedAddress">The encoded pointer byte that resolves to the old stream end.</param>
    [TestMethod]
    [DataRow(PointerAddressingMode.Absolute, 0L, (byte)4)]
    [DataRow(PointerAddressingMode.Relative, 1L, (byte)3)]
    public void PointerTargetUpdate_CannotExtend(
        PointerAddressingMode addressingMode,
        long origin,
        byte storedAddress)
    {
        var cstruct = new CStruct("struct root { uint8 *target; };", pointerSize: 1);
        using var stream = new TrackingStream(new byte[] { storedAddress, 0, 0, 0, });

        CStructReadException failure = Assert.Throws<CStructReadException>(
            () => cstruct.UpdateStream(
                stream,
                "root.target.value",
                (byte)0xA5,
                options: new UpdateOptions
                {
                    AddressingMode = addressingMode,
                    Origin = origin,
                }));

        Assert.AreEqual(CStructErrorCode.ReadFailed, failure.Code);
        Assert.AreEqual("root.target.value", failure.Path);
        CollectionAssert.AreEqual(new byte[] { storedAddress, 0, 0, 0, }, stream.Snapshot());
        Assert.AreEqual(0, stream.WriteCalls);
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Keeps update as replacement of existing storage instead of silently extending the destination.</summary>
    [TestMethod]
    public void Update_CannotExtendTheExistingStream()
    {
        var cstruct = new CStruct("struct root { uint8 value; };", pointerSize: 1);

        foreach ((byte[] bytes, long position) in new[]
                 {
                     (Array.Empty<byte>(), 0L),
                     (new byte[] { 0x11, }, 1L),
                 })
        {
            AssertValidationFailureLeavesDestinationUnchanged(
                bytes,
                position,
                stream => cstruct.UpdateStream(stream, "root.value", (byte)0xA5));
        }
    }

    /// <summary>Consumes an arbitrary enumerable once while preparing and committing one successful update.</summary>
    [TestMethod]
    public void SuccessfulUpdate_ConsumesSinglePassEnumerableOnce()
    {
        var cstruct = new CStruct("struct root { uint8 values[3]; };", pointerSize: 1);
        var values = new SinglePassEnumerable((byte)1, (byte)2, (byte)3);
        using var stream = new TrackingStream(new byte[3]);

        cstruct.UpdateStream(stream, "root.values", values);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, stream.Snapshot());
        Assert.AreEqual(1, values.EnumerationCount);
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Commits one coalesced aligned replacement in either byte order without charging output twice.</summary>
    /// <param name="isLittleEndian">Whether the multi-byte field uses least-significant-byte-first order.</param>
    /// <param name="expected">The complete expected replacement bytes.</param>
    [TestMethod]
    [DataRow(true, new byte[] { 1, 0, 0, 0, 0x44, 0x33, 0x22, 0x11, })]
    [DataRow(false, new byte[] { 1, 0, 0, 0, 0x11, 0x22, 0x33, 0x44, })]
    public void SuccessfulUpdate_PreservesAlignmentEndianAndLogicalBudget(
        bool isLittleEndian,
        byte[] expected)
    {
        var cstruct = new CStruct(
            "struct root { uint8 prefix; uint32 value; };",
            pointerSize: 1,
            aligned: true,
            isLittleEndian: isLittleEndian);
        using var stream = new TrackingStream(new byte[8]);

        cstruct.UpdateStream(
            stream,
            "root",
            new { prefix = (byte)1, value = 0x11223344U, },
            options: new UpdateOptions { MaxTotalBytesWritten = 5, });

        CollectionAssert.AreEqual(expected, stream.Snapshot());
        Assert.AreEqual(2, stream.WriteCalls);
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Retains the physical commit failure even if restoring the caller position then also fails.</summary>
    /// <param name="partialBytes">The prefix physically changed before the injected write failure.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void PhysicalCommitFailure_HasTheDocumentedPartialBoundary(int partialBytes)
    {
        var cstruct = new CStruct("struct root { uint8 values[3]; };", pointerSize: 1);
        var commitCause = new IOException("injected commit failure");
        var restoreCause = new IOException("injected restoration failure");
        using var stream = new CommitFaultStream(
            new byte[] { 0x11, 0x22, 0x33, },
            commitCause,
            restoreCause,
            partialBytes);

        CStructWriteException failure = Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(
                stream,
                "root.values",
                new byte[] { 1, 2, 3, }));

        Assert.AreSame(commitCause, failure.InnerException);
        Assert.AreEqual(CStructErrorCode.WriteFailed, failure.Code);
        Assert.AreEqual("root.values", failure.Path);
        Assert.AreEqual(partialBytes, failure.Offset);
        CollectionAssert.AreEqual(
            partialBytes == 0
                ? new byte[] { 0x11, 0x22, 0x33, }
                : new byte[] { 1, 0x22, 0x33, },
            stream.Snapshot());
        Assert.AreEqual(1, stream.WriteCalls);
        Assert.AreEqual(0, stream.FlushCalls);
    }

    /// <summary>Reports a post-commit restoration failure with context while retaining the validated replacement.</summary>
    [TestMethod]
    public void SuccessfulCommit_RetainsBytesWhenPositionRestorationFails()
    {
        var cstruct = new CStruct("struct root { uint8 values[3]; };", pointerSize: 1);
        var restoreCause = new IOException("injected restoration failure");
        using var stream = new CommitFaultStream(
            new byte[] { 0x11, 0x22, 0x33, },
            null,
            restoreCause,
            0);

        CStructReadException failure = Assert.Throws<CStructReadException>(
            () => cstruct.UpdateStream(
                stream,
                "root.values",
                new byte[] { 1, 2, 3, }));

        Assert.AreSame(restoreCause, failure.InnerException);
        Assert.AreEqual(CStructErrorCode.ReadFailed, failure.Code);
        Assert.AreEqual("root.values", failure.Path);
        Assert.AreEqual(3L, failure.Offset);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, stream.Snapshot());
        Assert.AreEqual(1, stream.WriteCalls);
    }

    private static void AssertValidationFailureLeavesDestinationUnchanged(
        byte[] bytes,
        long position,
        Action<Stream> update)
    {
        using var stream = new TrackingStream(bytes) { Position = position, };
        byte[] expected = stream.Snapshot();
        long expectedLength = stream.Length;

        Assert.Throws<CStructException>(() => update(stream));

        CollectionAssert.AreEqual(expected, stream.Snapshot());
        Assert.AreEqual(expectedLength, stream.Length);
        Assert.AreEqual(position, stream.Position);
        Assert.AreEqual(0, stream.WriteCalls);
    }

    private sealed class MissingSecondPoco
    {
        public byte First { get; init; }
    }

    private sealed class SinglePassEnumerable(params object[] values) : IEnumerable
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator GetEnumerator()
        {
            this.EnumerationCount++;
            if (this.EnumerationCount != 1)
            {
                throw new InvalidOperationException("The enumerable was consumed more than once.");
            }

            return values.GetEnumerator();
        }
    }

    /// <summary>Mutates a configured prefix, then fails the commit and every subsequent position restoration.</summary>
    private sealed class CommitFaultStream(
        byte[] bytes,
        IOException? commitCause,
        IOException restoreCause,
        int partialBytes) : Stream
    {
        private readonly MemoryStream inner = CreateInner(bytes);
        private bool commitFailed;

        public int FlushCalls { get; private set; }

        public int WriteCalls { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.inner.Position;
            set
            {
                if (this.commitFailed)
                {
                    throw restoreCause;
                }

                this.inner.Position = value;
            }
        }

        public override void Flush()
        {
            this.FlushCalls++;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return this.inner.Read(buffer);
        }

        public override int ReadByte()
        {
            return this.inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteCalls++;
            if (commitCause is null)
            {
                this.inner.Write(buffer, offset, count);
                this.commitFailed = true;
                return;
            }

            if (partialBytes > 0)
            {
                this.inner.Write(buffer, offset, Math.Min(partialBytes, count));
            }

            this.commitFailed = true;
            throw commitCause;
        }

        public byte[] Snapshot()
        {
            return this.inner.ToArray();
        }

        private static MemoryStream CreateInner(byte[] bytes)
        {
            var result = new MemoryStream();
            result.Write(bytes, 0, bytes.Length);
            result.Position = 0;
            return result;
        }
    }

    /// <summary>Records every destination write while retaining ordinary expandable-memory behavior.</summary>
    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream inner = new();

        public TrackingStream(byte[] bytes)
        {
            this.inner.Write(bytes, 0, bytes.Length);
            this.inner.Position = 0;
        }

        public int WriteCalls { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        public override void Flush()
        {
            this.inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this.inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return this.inner.Read(buffer);
        }

        public override int ReadByte()
        {
            return this.inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteCalls++;
            this.inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.WriteCalls++;
            this.inner.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            this.WriteCalls++;
            this.inner.WriteByte(value);
        }

        public byte[] Snapshot()
        {
            return this.inner.ToArray();
        }
    }
}
