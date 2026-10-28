namespace CStructSharp.Tests;

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;
using CStructSharp.Values;

/// <summary>Exercises prepared memory selections, operation contexts, caller-owned arrays and borrowed evidence.</summary>
[TestClass]
public class MemoryPreparedReaderTests
{
    /// <summary>A resolver's too-small target and its own precise transport diagnostics survive prepared reads.</summary>
    [TestMethod]
    public void ResolverTargets_ValidateSizeAndPreserveFailureContext()
    {
        MemorySchema schema = Schema();
        byte[] bytes = new byte[64];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 32);
        var source = new ByteArrayMemorySource("root", bytes);
        var root = new MemoryRegion(source, 0, 24);

        // A resolver may locate bytes, but it cannot authorize a target shorter than the schema's required extent.
        var shortSession = new MemorySession(schema, (_, _) => new MemoryRegion(source, 32, 1));
        MemoryFieldReader<int> reader = schema.Bind<int>("node", "next.value.pid");
        Assert.IsFalse(reader.TryRead(shortSession, root, out _, out MemoryReadFailure? shortTarget));
        Assert.AreEqual(MemoryReadFailureKind.InsufficientExtent, shortTarget!.Kind);
        Assert.AreEqual(32UL, shortTarget.Address);
        Assert.AreEqual(24, shortTarget.Length);
        var table = new MemoryRegion(source, 48, 8);
        var original = new MemoryAccessException(MemoryFailure.Unmapped, "page-table", 48, 8, "translation unavailable", new IOException("transport")) { LogicalRegion = table, };
        original.AttachContext("table.entry");

        // Lower-level diagnostics already identify the failing table and must not be overwritten by the outer record.
        var failedSession = new MemorySession(schema, (_, _) => throw original);
        Assert.IsFalse(reader.TryRead(failedSession, root, out _, out MemoryReadFailure? failure));
        Assert.AreEqual("table.entry", failure!.Path);
        Assert.AreSame(table, failure.LogicalRegion);
        Assert.AreSame(original, failure.ToException());
        Assert.IsInstanceOfType<IOException>(failure.ToException().InnerException);
    }

    /// <summary>Expected missing members, null pointers and missing bytes do not throw internally before returning failure data.</summary>
    [TestMethod]
    public void ExpectedAbsence_DoesNotRaiseFirstChanceExceptions()
    {
        MemorySchema schema = Schema();
        var session = new MemorySession(schema);
        MemoryFieldReader<int> reader = schema.Bind<int>("node", "pid");
        var missing = new MemoryRegion(new BorrowedMemorySource("missing", ReadOnlyMemory<byte>.Empty), 0, 24);
        var zero = new MemoryRegion(new ByteArrayMemorySource("zero", new byte[24]), 0, 24);
        int thread = Environment.CurrentManagedThreadId;
        int exceptions = 0;

        // Count only this operation thread's domain exceptions; concurrent test hosts must not affect the assertion.
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
        {
            if (Environment.CurrentManagedThreadId == thread && args.Exception is CStructException)
            {
                exceptions++;
            }
        };
        AppDomain.CurrentDomain.FirstChanceException += observe;
        try
        {
            Assert.IsFalse(schema.TrySelect("node", "absent", out _, out _));
            Assert.IsFalse(session.TryResolve(zero, "node", "next.value", out _, out _));
            Assert.IsFalse(reader.TryRead(session, missing, out _, out _));
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= observe;
        }

        Assert.AreEqual(0, exceptions);
    }

    /// <summary>All supported pointer widths bind, expose stored bits and follow a target without numeric shape mismatches.</summary>
    /// <param name="width">Stored byte width.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    public void BoundPointers_SupportEveryStoredWidth(int width)
    {
        var schema = new MemorySchema([new("b", "b", MemoryTypeKind.Scalar, 1, scalarType: "uint8"), new("p", "p", MemoryTypeKind.Pointer, width, elementTypeId: "b"),], pointerSize: width);
        byte[] bytes = new byte[32];
        bytes[0] = 16;
        bytes[16] = 42;
        var session = new MemorySession(schema);
        var root = new MemoryRegion(new ByteArrayMemorySource("pointers", bytes), 0, width);
        Assert.AreEqual(new StoredPointer(16, width), schema.Bind<StoredPointer>("p").Read(session, root));
        Assert.AreEqual((byte)42, schema.Bind<byte>("p", "value").Read(session, root));
    }

    /// <summary>Byte-array type validation precedes budgets and result allocation for both raw-byte overloads.</summary>
    [TestMethod]
    public void ByteReads_RejectNonByteElementsBeforeBudgetChecks()
    {
        var schema = new MemorySchema([new("u", "u", MemoryTypeKind.Scalar, 4, scalarType: "uint32"), new("array", "array", MemoryTypeKind.Array, 400000, elementTypeId: "u", count: 100000),]);
        var session = new MemorySession(schema);
        var root = new MemoryRegion(new BorrowedMemorySource("empty", ReadOnlyMemory<byte>.Empty), 0, 400000);
        var context = new MemoryAccessContext { MaxTotalBytes = 1, };
        Assert.AreEqual("path", Assert.Throws<ArgumentException>(() => session.ReadBytes(root, "array", context: context)).ParamName);
        Assert.AreEqual("path", Assert.Throws<ArgumentException>(() => session.ReadBytes(root, "array", string.Empty, Span<byte>.Empty, context)).ParamName);
        Assert.AreEqual(0, context.Requests);
        Assert.AreEqual(0L, context.BytesRequested);
    }

    /// <summary>Custom codecs cannot turn cancellation or a core read limit into ordinary unavailable evidence.</summary>
    [TestMethod]
    public void CustomCodecs_PropagateTerminatingFailures()
    {
        foreach (Exception expected in new Exception[] { new OperationCanceledException(), new CStructReadLimitException("custom limit"), })
        {
            var codec = new TokenCodec { Failure = expected, };
            var schema = new MemorySchema([new("token", "token", MemoryTypeKind.Scalar, 1, scalarType: "token"),], options: new CStructCompilationOptions { Codecs = [codec,], });
            MemoryFieldReader<object> reader = schema.Bind<object>("token");
            var region = new MemoryRegion(new ByteArrayMemorySource("custom", [1]), 0, 1);

            // The non-throwing route preserves terminating exceptions from the decoder unchanged.
            Action read = () => reader.TryRead(new MemorySession(schema), region, out _, out _);
            Exception actual = expected is OperationCanceledException
                ? Assert.Throws<OperationCanceledException>(read)
                : Assert.Throws<CStructReadLimitException>(read);
            Assert.AreSame(expected, actual);
        }
    }

    /// <summary>Prepared raw arrays retain unterminated storage bits and bypass custom value interpretation explicitly.</summary>
    [TestMethod]
    public void PreparedByteArrays_DoNotInvokeCustomElementCodecs()
    {
        var codec = new TokenCodec();
        MemoryTypeDefinition[] types = [
            new("token", "token", MemoryTypeKind.Scalar, 1, scalarType: "token"),
            new("array", "array", MemoryTypeKind.Array, 4, elementTypeId: "token", count: 4),
            new("root", "root", MemoryTypeKind.Struct, 4, [new("bytes", "array", 0),]),
        ];
        var schema = new MemorySchema(types, options: new CStructCompilationOptions { Codecs = [codec,], });
        var session = new MemorySession(schema);
        byte[] bytes = [255, 254, 253, 252];
        var region = new MemoryRegion(new ByteArrayMemorySource("raw", bytes), 0, 4);
        MemoryByteReader reader = schema.BindBytes("root", "bytes");
        byte[] destination = new byte[4];
        Assert.IsTrue(reader.Read(session, region, destination, new MemoryAccessContext()).IsComplete);
        Assert.AreEqual(0, codec.Reads);
        CollectionAssert.AreEqual(bytes, destination);
        Assert.IsTrue(reader.TryReadBuffer(bytes, new MemoryAccessContext(), out ReadOnlySpan<byte> borrowed));
        CollectionAssert.AreEqual(bytes, borrowed.ToArray());

        // Raw evidence succeeds although interpreting the first element invokes a rejecting codec.
        Assert.IsFalse(schema.Bind<object>("root", "bytes[0]").TryRead(session, region, out _, out MemoryReadFailure? failure));
        Assert.AreEqual(MemoryReadFailureKind.InvalidValue, failure!.Kind);
        Assert.AreEqual(1, codec.Reads);
    }

    /// <summary>Binding does not invoke an object-shaped custom decoder, and custom rejection retains addressed diagnostics.</summary>
    [TestMethod]
    public void CustomCodecs_KeepCallbacksAtReadTime()
    {
        var codec = new TokenCodec();
        var schema = new MemorySchema([new("token", "token", MemoryTypeKind.Scalar, 1, scalarType: "token"),], options: new CStructCompilationOptions { Codecs = [codec,], });
        MemoryFieldReader<object> reader = schema.Bind<object>("token");
        Assert.AreEqual(0, codec.Reads);
        Assert.IsFalse(schema.TryBind<int>("token", string.Empty, out _, out _));
        Assert.AreEqual(0, codec.Reads);
        var session = new MemorySession(schema);
        Assert.AreEqual("ok", reader.Read(session, new MemoryRegion(new ByteArrayMemorySource("valid", [1]), 0, 1)));
        Assert.AreEqual(1, codec.Reads);
        var invalid = new MemoryRegion(new BorrowedMemorySource("invalid", new byte[] { 255, }, 100), 100, 1);
        Assert.IsFalse(reader.TryRead(session, invalid, out _, out MemoryReadFailure? failure));
        Assert.AreEqual(MemoryReadFailureKind.InvalidValue, failure!.Kind);
        Assert.AreEqual("token", failure.Path);
        Assert.AreEqual(100UL, failure.Address);
        Assert.AreSame(invalid, failure.LogicalRegion);
    }

    /// <summary>A warmed fixed numeric binding and caller-owned array avoid intermediate selections, regions and scalar boxes.</summary>
    [TestMethod]
    public void FixedReaders_DoNotAllocatePerValueWithCallerContext()
    {
        MemorySchema schema = Schema();
        var session = new MemorySession(schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("fixed", new byte[24]), 0, 24);
        MemoryFieldReader<int> reader = schema.Bind<int>("node", "pid");
        var context = new MemoryAccessContext();
        int[] array = new int[4];
        for (int index = 0; index < 1000; index++)
        {
            _ = reader.Read(session, region, context);
            session.ReadArray<int>(region, "i", array, context);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            _ = reader.Read(session, region, context);
            session.ReadArray<int>(region, "i", array, context);
        }

        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    /// <summary>Promotion, ambiguity, signed enum slices, depth limits and concurrent contexts keep the ordinary reader's meaning.</summary>
    [TestMethod]
    public void PromotedBitfields_KeepTypesAndIndependentContexts()
    {
        var schema = new MemorySchema([
            new("s", "s", MemoryTypeKind.Scalar, 2, scalarType: "kind", declaration: "enum kind : int16 { negative = -1, zero = 0 };"),
            new("u", "u", MemoryTypeKind.Scalar, 2, scalarType: "uint16"),
            new("inner", "inner", MemoryTypeKind.Struct, 2, [new("signed", "s", 0, 0, 3, true),]),
            new("root", "root", MemoryTypeKind.Struct, 2, [new("anonymous", "inner", 0, promoted: true),]),
            new("ambiguous", "ambiguous", MemoryTypeKind.Union, 2, [new("one", "inner", 0, promoted: true), new("two", "inner", 0, promoted: true),]),
        ]);
        var session = new MemorySession(schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("bits", [7, 0]), 0, 2);
        MemoryFieldReader<EnumValueResult> reader = schema.Bind<EnumValueResult>("root", "signed");
        Assert.AreEqual(-1, (int)reader.Read(session, region).Value);
        Assert.AreEqual(((EnumValueResult)session.Read(region, "root", "signed")!).Value, reader.Read(session, region).Value);
        Assert.AreEqual(0L, reader.Selection.Offset);
        Assert.AreEqual("inner", reader.Selection.ParentTypeId);
        Assert.IsFalse(schema.TrySelect("ambiguous", "signed", out _, out MemoryReadFailure? ambiguity));
        Assert.AreEqual(MemoryReadFailureKind.AmbiguousMember, ambiguity!.Kind);

        // Prepared readers share schema decisions, while each parallel operation owns its context and counters.
        Parallel.For(0, 16, _ =>
        {
            var context = new MemoryAccessContext();
            Assert.AreEqual(-1, (int)reader.Read(session, region, context).Value);
            Assert.AreEqual(2L, context.BytesRequested);
            Assert.AreEqual(2, context.Requests);
        });
        Assert.Throws<MemoryAccessException>(() => reader.Read(session, region, new MemoryAccessContext { MaxNestingDepth = 1, }));
    }

    /// <summary>An unselected empty member beyond the last unsigned address still invalidates a prepared selection.</summary>
    [TestMethod]
    public void BoundPaths_KeepUnselectedMemberOverflowChecks()
    {
        var schema = new MemorySchema([
            new("b", "b", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("empty", "empty", MemoryTypeKind.Struct, 0),
            new("root", "root", MemoryTypeKind.Struct, 1, [new("value", "b", 0), new("end", "empty", 1),]),
        ]);
        var session = new MemorySession(schema);
        var region = new MemoryRegion(new BorrowedMemorySource("edge", new byte[] { 42, }, ulong.MaxValue), ulong.MaxValue, 1);
        Assert.Throws<OverflowException>(() => schema.Bind<byte>("root", "value").Read(session, region));
        Assert.Throws<OverflowException>(() => session.Read(region, "root", "value"));
    }

    /// <summary>Bulk failure preserves every fully captured pointer before a hole, including a partial chunk.</summary>
    [TestMethod]
    public void PartialBulkChunk_PreservesCompletePointerEvidence()
    {
        var schema = new MemorySchema([new("p", "p", MemoryTypeKind.Pointer, 8),]);
        var session = new MemorySession(schema);
        byte[] bytes = new byte[26];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, 0x101);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 0x202);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), 0x303);
        var region = new MemoryRegion(new BorrowedMemorySource("truncated", bytes), 0, 32);
        ulong[] values = [9, 9, 9, 9,];
        Assert.IsFalse(session.TryReadArray<ulong>(region, "p", values, out int written, out _, mode: MemoryArrayReadMode.Bulk));
        Assert.AreEqual(3, written);
        CollectionAssert.AreEqual(new ulong[] { 0x101, 0x202, 0x303, 9, }, values);
    }

    /// <summary>Direct, nested, indexed and pointer paths agree with ordinary reads in both byte orders.</summary>
    /// <param name="little">Target byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void BoundPaths_MatchOrdinaryReads(bool little)
    {
        MemorySchema schema = Schema(little);
        var session = new MemorySession(schema);
        byte[] bytes = session.Serialize("node", new Dictionary<string, object?> { ["pid"] = -7, ["next"] = new StoredPointer(32), ["values"] = new[] { 10, 20, }, });
        byte[] image = new byte[64];
        bytes.CopyTo(image, 0);
        bytes.CopyTo(image, 32);
        var source = new ByteArrayMemorySource("image", image);
        var region = new MemoryRegion(source, 0, 24);
        foreach (string path in new[] { "pid", "values[1]", "next.value.pid", "next.value.next.value.values[0]", })
        {
            MemoryFieldReader<int> reader = schema.Bind<int>("node", path);
            var ordinaryContext = new MemoryAccessContext();
            var boundContext = new MemoryAccessContext();
            Assert.AreEqual(session.Read(region, "node", path, ordinaryContext), reader.Read(session, region, boundContext));
            Assert.AreEqual(ordinaryContext.BytesRequested, boundContext.BytesRequested);
            Assert.AreEqual(ordinaryContext.Requests, boundContext.Requests);
            Assert.AreEqual(path.Contains("value.", StringComparison.Ordinal), reader.Selection.Offset is null);
        }

        Assert.AreEqual(16L, schema.Select("node", "values").Offset);
        Assert.AreEqual(32UL, schema.Bind<StoredPointer>("node", "next.address").Read(session, region).Address);
        Assert.IsFalse(schema.TryBind<uint>("node", "pid", out _, out MemoryReadFailure? wrong));
        Assert.AreEqual(MemoryReadFailureKind.WrongType, wrong!.Kind);

        // A prepared reader rejects a structurally identical schema because codecs and metadata belong to an instance.
        Assert.Throws<ArgumentException>(() => schema.Bind<int>("node", "pid").Read(new MemorySession(Schema(little)), region));
    }

    /// <summary>Prepared paths never retain pointer values and preserve the immediate containing record for relative resolvers.</summary>
    [TestMethod]
    public void PointerResolvers_ReceiveActiveContextAndFreshBits()
    {
        MemorySchema schema = Schema();
        var image = new ByteArrayMemorySource("image", new byte[80]);
        image.Write(8, BitConverter.GetBytes(32UL), new MemoryAccessContext());
        image.Write(32, BitConverter.GetBytes(42), new MemoryAccessContext());
        image.Write(56, BitConverter.GetBytes(99), new MemoryAccessContext());
        var context = new MemoryAccessContext();
        int calls = 0;

        // Translation is part of the same operation and charges its own source reads.
        var session = new MemorySession(schema, (request, active) =>
        {
            Assert.AreSame(context, active);
            Assert.AreEqual(0UL, request.Container.Address);
            calls++;
            Span<byte> translation = stackalloc byte[1];
            new MemoryRegion(image, 79, 1).ReadExactly(translation, active);
            return new MemoryRegion(image, request.Pointer.Address, request.TargetSize);
        });
        MemoryFieldReader<int> reader = schema.Bind<int>("node", "next.value.pid");
        var root = new MemoryRegion(image, 0, 24);
        Assert.AreEqual(42, reader.Read(session, root, context));
        image.Write(8, BitConverter.GetBytes(56UL), new MemoryAccessContext());
        Assert.AreEqual(99, reader.Read(session, root, context));
        Assert.AreEqual(2, calls);
        Assert.AreEqual(26L, context.BytesRequested);
    }

    /// <summary>Resolver translation observes cancellation and byte/request exhaustion under ordinary and prepared traversal.</summary>
    /// <param name="prepared">Whether to use a bound reader.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ResolverFailures_TerminateTheOperation(bool prepared)
    {
        MemorySchema schema = Schema();
        byte[] bytes = new byte[64];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 32);
        var source = new ByteArrayMemorySource("image", bytes);
        var root = new MemoryRegion(source, 0, 24);
        foreach (bool cancel in new[] { false, true, })
        {
            using var cancellation = new CancellationTokenSource();
            var context = new MemoryAccessContext { MaxTotalBytes = 8, CancellationToken = cancellation.Token, };

            // A translation read must not escape the caller's exhausted byte budget or cancellation.
            var session = new MemorySession(schema, (request, active) =>
            {
                Assert.AreSame(context, active);
                if (cancel)
                {
                    cancellation.Cancel();
                }

                Span<byte> scratch = stackalloc byte[1];
                new MemoryRegion(source, 63, 1).ReadExactly(scratch, active);
                return new MemoryRegion(source, request.Pointer.Address, request.TargetSize);
            });

            // Both public routes must propagate terminating failures instead of reporting absent evidence.
            Action read = () =>
            {
                if (prepared)
                {
                    schema.Bind<int>("node", "next.value.pid").TryRead(session, root, out _, out _, context);
                }
                else
                {
                    session.Read(root, "node", "next.value.pid", context);
                }
            };
            if (cancel)
            {
                Assert.Throws<OperationCanceledException>(read);
            }
            else
            {
                Assert.AreEqual(MemoryFailure.BudgetExceeded, Assert.Throws<MemoryAccessException>(read).Failure);
            }
        }
    }

    /// <summary>Direct exact reads validate extent first, retry short reads, preserve prefixes and observe empty-read cancellation.</summary>
    [TestMethod]
    public void ExactReads_PreserveBoundsShortReadsAndFailureCoordinates()
    {
        var source = new ShortSource([1, 2, 3,], 1);
        var region = new MemoryRegion(source, ulong.MaxValue - 3, 4);
        var context = new MemoryAccessContext();
        byte[] destination = [9, 9, 9, 9,];
        Assert.IsFalse(region.TryReadExactly(destination, context, out MemoryReadFailure? failure));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 9, }, destination);
        Assert.AreEqual(ulong.MaxValue, failure!.Address);
        Assert.AreEqual(MemoryFailure.MissingBytes, failure.MemoryFailure);
        Assert.AreEqual(4, context.Requests);
        Assert.AreEqual(10L, context.BytesRequested);
        Assert.Throws<ArgumentOutOfRangeException>(() => region.ReadExactly(new byte[5], context));
        Assert.AreEqual(4, context.Requests);
        region.ReadExactly([], context);
        Assert.AreEqual(4, context.Requests);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => region.ReadExactly([], new MemoryAccessContext { CancellationToken = cancelled.Token, }));
    }

    /// <summary>Signed and unsigned one-byte arrays copy identical storage without a temporary materialized array.</summary>
    [TestMethod]
    public void ByteArrays_PreserveRawSignedStorage()
    {
        foreach (string scalar in new[] { "uint8", "int8", })
        {
            var schema = new MemorySchema([new("b", "b", MemoryTypeKind.Scalar, 1, scalarType: scalar), new("a", "a", MemoryTypeKind.Array, 3, elementTypeId: "b", count: 3),]);
            var session = new MemorySession(schema);
            byte[] destination = new byte[3];
            session.ReadBytes(new MemoryRegion(new ByteArrayMemorySource("bytes", [0, 128, 255]), 0, 3), "a", string.Empty, destination);
            CollectionAssert.AreEqual(new byte[] { 0, 128, 255, }, destination);
        }
    }

    /// <summary>Bulk transport is opt-in, decodes target byte order and commits only complete chunks.</summary>
    /// <param name="little">Target byte order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ArrayDestinations_DefineTransportAndPartialCompletion(bool little)
    {
        var schema = new MemorySchema([new("n", "n", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),], isLittleEndian: little);
        var session = new MemorySession(schema);
        byte[] encoded = little ? [1, 0, 0, 0, 2, 0, 0, 0, 3, 0,] : [0, 0, 0, 1, 0, 0, 0, 2, 0, 0,];
        var source = new BorrowedMemorySource("array", encoded);
        var storage = new MemoryRegion(source, 0, 12);
        uint[] destination = [99, 99, 99,];
        Assert.IsFalse(session.TryReadArray<uint>(storage, "n", destination, out int written, out MemoryReadFailure? failure, mode: MemoryArrayReadMode.Bulk, chunkBytes: 8));
        Assert.AreEqual(2, written);
        CollectionAssert.AreEqual(new uint[] { 1, 2, 99, }, destination);
        Assert.AreEqual(10UL, failure!.Address);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ReadArray<uint>(new MemoryRegion(source, 0, 8), "n", destination));
        Assert.Throws<MemoryAccessException>(() => session.ReadArray<uint>(storage, "n", destination, new MemoryAccessContext { MaxTotalBytes = 8, }));
        session.ReadArray<uint>(new MemoryRegion(source, 0, 0), "n", []);
    }

    /// <summary>Pointer destinations preserve high and tagged stored bits at both supported widths.</summary>
    /// <param name="width">Stored pointer byte width.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(8)]
    public void PointerDestinations_DoNotFollowOrClearTags(int width)
    {
        var schema = new MemorySchema([new("p", "p", MemoryTypeKind.Pointer, width),], pointerSize: width);
        byte[] bytes = Enumerable.Repeat((byte)255, width * 2).ToArray();
        var session = new MemorySession(schema);
        ulong[] destination = new ulong[2];
        session.ReadArray<ulong>(new MemoryRegion(new ByteArrayMemorySource("pointers", bytes), 0, bytes.Length), "p", destination, mode: MemoryArrayReadMode.Bulk);
        Assert.AreEqual(ulong.MaxValue >> (64 - (width * 8)), destination[0]);
        Assert.AreEqual(destination[0], destination[1]);
    }

    /// <summary>Expected selection failures retain distinct kinds and no exception is raised for missing bytes.</summary>
    [TestMethod]
    public void NonThrowingOperations_DistinguishExpectedFailures()
    {
        MemorySchema schema = Schema();
        Assert.IsFalse(schema.TrySelect("missing", string.Empty, out _, out MemoryReadFailure? unknown));
        Assert.AreEqual(MemoryReadFailureKind.UnknownType, unknown!.Kind);
        Assert.IsFalse(schema.TrySelect("node", "absent", out _, out MemoryReadFailure? absent));
        Assert.AreEqual(MemoryReadFailureKind.AbsentMember, absent!.Kind);
        var session = new MemorySession(schema);
        var region = new MemoryRegion(new BorrowedMemorySource("empty", ReadOnlyMemory<byte>.Empty), 0, 24);
        MemoryFieldReader<int> reader = schema.Bind<int>("node", "pid");
        Assert.IsFalse(reader.TryRead(session, region, out _, out MemoryReadFailure? missing));
        Assert.AreEqual(MemoryFailure.MissingBytes, missing!.MemoryFailure);
        Assert.AreEqual("node.pid", missing.Path);
        Assert.AreSame(region, missing.LogicalRegion);
        var zero = new MemoryRegion(new ByteArrayMemorySource("zero", new byte[24]), 0, 24);
        Assert.IsFalse(session.TryResolve(zero, "node", "next.value", out _, out MemoryReadFailure? nil));
        Assert.AreEqual(MemoryReadFailureKind.NullPointer, nil!.Kind);
    }

    /// <summary>Borrowed bytes keep unsigned coordinates, readable prefixes and external stability rather than creating a snapshot.</summary>
    [TestMethod]
    public void BorrowedBuffers_KeepHighAddressesAndMissingSuffixes()
    {
        byte[] bytes = [1, 2, 9, 9,];
        var source = new BorrowedMemorySource("borrowed", bytes, ulong.MaxValue - 3, 2);
        byte[] result = [7, 7, 7, 7,];
        Assert.IsFalse(new MemoryRegion(source, source.BaseAddress, 4).TryReadExactly(result, new MemoryAccessContext(), out _));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 7, 7, }, result);
        bytes[0] = 42;
        Assert.AreEqual(1, source.Read(source.BaseAddress, result.AsSpan(0, 1), new MemoryAccessContext()));
        Assert.AreEqual((byte)42, result[0]);
        Assert.AreEqual(0L, source.Generation);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BorrowedMemorySource("wrap", bytes, ulong.MaxValue));
    }

    /// <summary>Shallow ISF descriptions preserve direct unsupported descriptors without validating unrelated or referenced types.</summary>
    [TestMethod]
    public void IsfDescriptions_AreShallowAndImportsRemainIndependent()
    {
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes("""
            {"metadata":{"format":"6.2.0"},"base_types":{"u":{"kind":"int","size":4,"signed":false,"endian":"little"}},
             "user_types":{"one":{"kind":"struct","size":4,"fields":{"x":{"offset":0,"type":{"kind":"base","name":"u"}}}},
             "two":{"kind":"struct","size":4,"fields":{"y":{"offset":0,"type":{"kind":"base","name":"u"}}}},
             "browse":{"kind":"struct","size":8,"fields":{"":{"offset":0,"type":{"kind":"unsupported","name":"missing"}}}},"bad":false}}
            """));
        IsfTypeDescription description = metadata.Describe("browse");
        Assert.AreEqual("unsupported", description.Members[0].Kind);
        StringAssert.Contains(description.Members[0].TypeDescriptor, "missing");
        Assert.Throws<CStructLayoutException>(() => metadata.Describe("bad"));
        MetadataImportResult first = metadata.Import("one");
        MetadataImportResult second = metadata.Import("two");
        Assert.AreNotSame(first.Schema, second.Schema);
        Assert.IsFalse(second.Schema.Types.ContainsKey(first.RootTypeId));

        // Equal successful scalar declarations share compilation; graph identities and option sets remain independent.
        MemoryTypeDefinition firstScalar = first.Schema.Types.Values.Single(type => type.Kind == MemoryTypeKind.Scalar);
        MemoryTypeDefinition secondScalar = second.Schema.Types.Values.Single(type => type.Kind == MemoryTypeKind.Scalar);
        Assert.AreSame(first.Schema.GetCodec(firstScalar).Layout, second.Schema.GetCodec(secondScalar).Layout);
        MetadataImportResult otherOptions = metadata.Import("two", new MetadataImportOptions { PointerSize = 4, });
        Assert.AreNotSame(first.Schema.GetCodec(firstScalar).Layout, otherOptions.Schema.GetCodec(otherOptions.Schema.GetType(secondScalar.Id)).Layout);
        Assert.Throws<CStructLayoutException>(() => metadata.Import("one", new MetadataImportOptions { MaxTypes = 1, }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => metadata.Import("one", cancellationToken: cancellation.Token));

        // Parallel imports may share immutable scalar layouts, but each creates its own root graph and notes.
        Parallel.For(0, 8, index =>
        {
            // Each import shares only immutable successful scalar compilations, never the root graph or diagnostics.
            MetadataImportResult imported = metadata.Import(index % 2 == 0 ? "one" : "two");
            Assert.AreEqual(4, imported.Schema.GetType(imported.RootTypeId).Size);
        });
    }

    /// <summary>Builds a fixed record with scalar, pointer and array members.</summary>
    /// <param name="little">Scalar byte order.</param>
    /// <returns>The immutable reusable schema.</returns>
    private static MemorySchema Schema(bool little = true) => new(
    [
        new("i", "i", MemoryTypeKind.Scalar, 4, scalarType: "int32"),
        new("pointer", "pointer", MemoryTypeKind.Pointer, 8, elementTypeId: "node"),
        new("array", "array", MemoryTypeKind.Array, 8, elementTypeId: "i", count: 2),
        new("node", "node", MemoryTypeKind.Struct, 24, [new("pid", "i", 0), new("next", "pointer", 8), new("values", "array", 16),]),
    ],
    isLittleEndian: little);

    /// <summary>A fixed-size custom codec that reports a captured invalid byte without fabricating a value.</summary>
    private sealed class TokenCodec : ICustomCodec
    {
        /// <inheritdoc/>
        public string Name => "token";

        /// <inheritdoc/>
        public int? FixedSize => 1;

        /// <inheritdoc/>
        public int Alignment => 1;

        /// <summary>Gets the number of actual decode callbacks.</summary>
        public int Reads { get; private set; }

        /// <summary>Gets an optional terminating exception raised by the actual decode callback.</summary>
        public Exception? Failure { get; init; }

        /// <inheritdoc/>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            this.Reads++;
            if (this.Failure is { } failure)
            {
                throw failure;
            }

            bytesConsumed = 1;
            value = source[0] == 255 ? null : "ok";
            return value is null ? OperationStatus.InvalidData : OperationStatus.Done;
        }

        /// <inheritdoc/>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            destination[0] = 1;
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }

    /// <summary>A high-address transport that returns at most a small positive prefix per call.</summary>
    /// <param name="bytes">Actually captured bytes.</param>
    /// <param name="maximum">Largest positive read.</param>
    private sealed class ShortSource(byte[] bytes, int maximum) : IMemorySource
    {
        /// <inheritdoc/>
        public string Id => "short";

        /// <inheritdoc/>
        public long Generation => 0;

        /// <inheritdoc/>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, destination.Length);
            ulong offset = address - (ulong.MaxValue - 3);
            if (offset >= (ulong)bytes.Length)
            {
                return 0;
            }

            int count = Math.Min(maximum, Math.Min(destination.Length, bytes.Length - (int)offset));
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }
    }
}
