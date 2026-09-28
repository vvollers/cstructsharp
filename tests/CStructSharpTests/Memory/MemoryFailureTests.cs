namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Checks failure atomicity promises, custom codecs, and address boundary behavior.</summary>
[TestClass]
public class MemoryFailureTests
{
    /// <summary>A later write failure reports the confirmed prefix without concealing partial mutation.</summary>
    [TestMethod]
    public void Patch_ReportsPartialCommit()
    {
        var source = new FailingSource();
        var mapped = new MappedMemorySource("mapped", [new(100, new MemoryRegion(source, 0, 1)), new(101, new MemoryRegion(source, 2, 1)),]);
        MemoryPatch patch = MemoryPatch.Create(new MemoryRegion(mapped, 100, 2), new byte[] { 8, 9, });
        MemoryPatchCommitException failure = Assert.Throws<MemoryPatchCommitException>(() => patch.Commit());
        Assert.AreEqual(1L, failure.CompletedBytes);
        Assert.AreEqual(1, failure.FragmentIndex);
        Assert.IsInstanceOfType<IOException>(failure.InnerException);
        Assert.AreEqual(8, source.Bytes[0]);
    }

    /// <summary>Fixed custom types receive finite streams and participate in metadata creation, inspection, and patches.</summary>
    [TestMethod]
    public void Codec_ReadsAndWritesThroughFiniteRegion()
    {
        var codec = new LetterCodec();
        var options = new CStructCompilationOptions { Codecs = [codec,], };
        var layout = new CStruct("struct Message { letter text; };", compilationOptions: options);
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Message"));
        byte[] bytes = session.Serialize("Message", new Dictionary<string, object?> { ["text"] = "A", });
        var source = new ByteArrayMemorySource("image", bytes);
        var mapped = new MappedMemorySource("high", [new(ulong.MaxValue, new MemoryRegion(source, 0, 1)),]);
        var region = new MemoryRegion(mapped, ulong.MaxValue, 1);
        MemoryInspection inspected = session.Inspect(region, "Message", "text");
        Assert.AreEqual("A", inspected.Value);
        Assert.AreEqual(ulong.MaxValue, inspected.Selection.Region.Address);
        Assert.AreEqual(0UL, inspected.BackingRegions[0].Address);
        session.PlanUpdate(region, "Message", "text", "B").Commit();
        Assert.AreEqual("B", session.Read(region, "Message", "text"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRegion(mapped, ulong.MaxValue, 2));
    }

    /// <summary>Bad paths, finite-stream seeks, and canceled graph operations fail before unbounded work.</summary>
    [TestMethod]
    public void Boundaries_CancelAndRejectMalformedPaths()
    {
        var source = new ByteArrayMemorySource("image", new byte[8]);
        var region = new MemoryRegion(source, 0, 8);
        var session = new MemorySession(new MemorySchema([new("u", "u", MemoryTypeKind.Scalar, 8, scalarType: "uint64"),]));
        foreach (string path in new[] { ".", "x.", "[-1]", "[x]", "[0]x", "..", })
        {
            Assert.Throws<CStructPathException>(() => session.Resolve(region, "u", path));
        }

        using Stream stream = region.OpenRead();
        Assert.AreEqual(7L, stream.Seek(-1, SeekOrigin.End));
        Assert.AreEqual(0L, stream.Seek(-7, SeekOrigin.Current));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(9, SeekOrigin.Begin));

        // Cancellation propagates through the shared callback budget.
        Assert.Throws<OperationCanceledException>(() => MemoryWalker.Tree(region, (_, _) => [], context: new MemoryAccessContext { CancellationToken = new CancellationToken(true), }));
    }

    /// <summary>
    ///     A memory access failure is a core read failure: missing bytes carry <see cref="CStructErrorCode.ReadFailed"/>,
    ///     an exhausted budget <see cref="CStructErrorCode.ReadLimitExceeded"/>, and both keep the failing fragment's
    ///     coordinates next to the requested path and root region.
    /// </summary>
    [TestMethod]
    public void AccessFailures_AreCoreReadExceptionsWithTheirCategoryCode()
    {
        var source = new ByteArrayMemorySource("image", new byte[2]);
        var region = new MemoryRegion(source, 0, 4);
        var session = new MemorySession(new MemorySchema([new("u", "u", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),]));

        // Two of the four bytes exist, so the read stops at address 2.
        MemoryAccessException missing = Assert.Throws<MemoryAccessException>(() => session.Read(region, "u"));
        Assert.IsInstanceOfType<CStructReadException>(missing);
        Assert.AreEqual(MemoryFailure.MissingBytes, missing.Failure);
        Assert.AreEqual(CStructErrorCode.ReadFailed, missing.Code);
        Assert.AreEqual("image", missing.SourceId);
        Assert.AreEqual(2UL, missing.Address);
        Assert.AreEqual("u", missing.Path);
        Assert.AreSame(region, missing.LogicalRegion);
        StringAssert.StartsWith(missing.Message, "image:0x2 (2 bytes): ");

        MemoryAccessException budget = Assert.Throws<MemoryAccessException>(() => session.Read(region, "u", context: new MemoryAccessContext { MaxTotalBytes = 1, }));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, budget.Failure);
        Assert.AreEqual(CStructErrorCode.ReadLimitExceeded, budget.Code);
        Assert.AreEqual("image", budget.SourceId);
        Assert.AreEqual(0UL, budget.Address);
    }

    /// <summary>A depth limit and the byte budget of new output are not tied to a source address, so both coordinates are null.</summary>
    [TestMethod]
    public void DepthAndOutputBudgets_HaveNoSourceCoordinates()
    {
        MemoryTypeDefinition scalar = new("u", "u", MemoryTypeKind.Scalar, 4, scalarType: "uint32");
        MemoryTypeDefinition inner = new("inner", "inner", MemoryTypeKind.Struct, 4, [new("value", "u", 0),]);
        MemoryTypeDefinition outer = new("outer", "outer", MemoryTypeKind.Struct, 4, [new("inner", "inner", 0),]);
        var session = new MemorySession(new MemorySchema([scalar, inner, outer,]));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[4]), 0, 4);

        MemoryAccessException depth = Assert.Throws<MemoryAccessException>(() => session.Read(region, "outer", context: new MemoryAccessContext { MaxNestingDepth = 1, }));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, depth.Failure);
        Assert.AreEqual(CStructErrorCode.ReadLimitExceeded, depth.Code);
        Assert.IsNull(depth.SourceId);
        Assert.IsNull(depth.Address);
        Assert.AreEqual("outer", depth.Path);

        MemoryAccessException output = Assert.Throws<MemoryAccessException>(() => session.Serialize("u", 1U, new MemoryAccessContext { MaxTotalBytes = 2, }));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, output.Failure);
        Assert.IsNull(output.SourceId);
        Assert.IsNull(output.Address);
        Assert.AreEqual(4, output.Length);
        Assert.AreEqual("u", output.Path);
        StringAssert.StartsWith(output.Message, "Output exceeds the byte budget");
    }

    /// <summary>A planned update larger than the byte budget reports the selected member's address, not the root record's.</summary>
    [TestMethod]
    public void PlanUpdateBudget_ReportsTheSelectedRegion()
    {
        MemoryTypeDefinition scalar = new("u", "u", MemoryTypeKind.Scalar, 4, scalarType: "uint32");
        MemoryTypeDefinition record = new("r", "r", MemoryTypeKind.Struct, 8, [new("a", "u", 0), new("b", "u", 4),]);
        var session = new MemorySession(new MemorySchema([scalar, record,]));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[8]), 0, 8);

        MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => session.PlanUpdate(region, "r", "b", 1U, new MemoryAccessContext { MaxTotalBytes = 2, }));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
        Assert.AreEqual("image", failure.SourceId);
        Assert.AreEqual(4UL, failure.Address);
        Assert.AreEqual(4, failure.Length);
        Assert.AreEqual("r.b", failure.Path);
    }

    /// <summary>Unknown members and out-of-range indexes are core path errors that carry the requested path.</summary>
    [TestMethod]
    public void PathErrors_AreCStructPathExceptionsWithThePath()
    {
        MemoryTypeDefinition scalar = new("u", "u", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        MemoryTypeDefinition array = new("a", "a", MemoryTypeKind.Array, 2, elementTypeId: "u", count: 2);
        MemoryTypeDefinition record = new("r", "r", MemoryTypeKind.Struct, 2, [new("items", "a", 0),]);
        var session = new MemorySession(new MemorySchema([scalar, array, record,]));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[2]), 0, 2);

        CStructPathException absent = Assert.Throws<CStructPathException>(() => session.Read(region, "r", "missing"));
        Assert.AreEqual(CStructErrorCode.InvalidPath, absent.Code);
        Assert.AreEqual("r.missing", absent.Path);
        StringAssert.Contains(absent.Message, "missing");

        CStructPathException index = Assert.Throws<CStructPathException>(() => session.Resolve(region, "r", "items[2]"));
        Assert.AreEqual("r.items[2]", index.Path);
        StringAssert.Contains(index.Message, "out of range");

        CStructPathException type = Assert.Throws<CStructPathException>(() => session.Read(region, "absent"));
        Assert.AreEqual("absent", type.Path);
    }

    /// <summary>A value without the declared shape is a core write error, for serialization and for a planned update alike.</summary>
    [TestMethod]
    public void BadValues_AreCStructWriteExceptions()
    {
        MemoryTypeDefinition scalar = new("u", "u", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        MemoryTypeDefinition record = new("r", "r", MemoryTypeKind.Struct, 1, [new("x", "u", 0),]);
        var session = new MemorySession(new MemorySchema([scalar, record,]));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", new byte[1]), 0, 1);

        CStructWriteException missing = Assert.Throws<CStructWriteException>(() => session.Serialize("r", new Dictionary<string, object?>()));
        Assert.AreEqual(CStructErrorCode.WriteFailed, missing.Code);
        Assert.AreEqual("r", missing.Path);
        StringAssert.Contains(missing.Message, "'x'");

        Assert.Throws<CStructWriteException>(() => session.Serialize("u", null));
        CStructWriteException shape = Assert.Throws<CStructWriteException>(() => session.PlanUpdate(region, "r", string.Empty, 1));
        Assert.AreEqual("r", shape.Path);
    }

    /// <summary>An invalid type definition and malformed BTF metadata are core layout errors.</summary>
    [TestMethod]
    public void InvalidDefinitionsAndMetadata_AreCStructLayoutExceptions()
    {
        CStructLayoutException reference = Assert.Throws<CStructLayoutException>(() => new MemorySchema([new("r", "r", MemoryTypeKind.Struct, 1, [new("x", "missing", 0),]),]));
        Assert.AreEqual(CStructErrorCode.InvalidLayout, reference.Code);
        StringAssert.Contains(reference.Message, "missing");

        // A 24-byte header of zeroes has no BTF magic number.
        CStructLayoutException header = Assert.Throws<CStructLayoutException>(() => new BtfMetadata(new byte[24]));
        StringAssert.Contains(header.Message, "magic");

        var metadata = new BtfMetadata(MemoryBtfCoverageTests.Blob([1, 1U << 24, 4, 32,], "\0u32\0"));
        StringAssert.Contains(Assert.Throws<CStructLayoutException>(() => metadata.FindType("absent")).Message, "absent");
    }

    /// <summary>A commit that fails after writing started is a core write failure, so one handler covers every write error.</summary>
    [TestMethod]
    public void CommitFailure_IsCStructWriteException()
    {
        var source = new FailingSource();
        var mapped = new MappedMemorySource("mapped", [new(100, new MemoryRegion(source, 0, 1)), new(101, new MemoryRegion(source, 2, 1)),]);
        MemoryPatch patch = MemoryPatch.Create(new MemoryRegion(mapped, 100, 2), new byte[] { 8, 9, });

        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => patch.Commit());
        Assert.IsInstanceOfType<MemoryPatchCommitException>(failure);
        Assert.AreEqual(CStructErrorCode.WriteFailed, failure.Code);
    }

    /// <summary>Independent writable adapter that fails only the second physical fragment.</summary>
    private sealed class FailingSource : IWritableMemorySource
    {
        public byte[] Bytes { get; } = new byte[4];

        public string Id => "failing";

        public long Generation => 0;

        /// <inheritdoc/>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, destination.Length);
            this.Bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            return destination.Length;
        }

        /// <inheritdoc/>
        public void Write(ulong address, ReadOnlySpan<byte> bytes, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, bytes.Length);
            if (address == 2)
            {
                throw new IOException("Synthetic device failure.");
            }

            bytes.CopyTo(this.Bytes.AsSpan((int)address));
        }
    }

    /// <summary>A tiny independently authored one-byte codec whose value is a string.</summary>
    private sealed class LetterCodec : ICustomCodec
    {
        public string Name => "letter";

        public int? FixedSize => 1;

        public int Alignment => 1;

        /// <inheritdoc/>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            if (source.IsEmpty)
            {
                value = null;
                bytesConsumed = 0;
                return OperationStatus.NeedMoreData;
            }

            value = ((char)source[0]).ToString();
            bytesConsumed = 1;
            return OperationStatus.Done;
        }

        /// <inheritdoc/>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            destination[0] = checked((byte)((string)value)[0]);
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }
}
