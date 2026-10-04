namespace CStructSharp.Tests;

using System.Buffers.Binary;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;

/// <summary>Preserves member-selection metadata, failure order, and observable work when avoiding unused selections.</summary>
[TestClass]
public class MemorySelectionConstructionTests
{
    /// <summary>Small and wide records resolve their first, middle, and final members without source access and read only the selected byte.</summary>
    /// <param name="count">Number of byte members in the record.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(273)]
    public void DirectMembers_KeepMetadataAndReadAccounting(int count)
    {
        var fields = new MemoryField[count];
        var bytes = new byte[count];
        for (int index = 0; index < count; index++)
        {
            fields[index] = new MemoryField("field" + index, "byte", index);
            bytes[index] = (byte)(index % 251);
        }

        var scalar = new MemoryTypeDefinition("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        var record = new MemoryTypeDefinition("record", "record", MemoryTypeKind.Struct, count, fields);
        var session = new MemorySession(new MemorySchema([scalar, record,]));
        var trace = new List<string>();
        var source = new TracedSource("image", 100, bytes, trace);
        var region = new MemoryRegion(source, 100, count);
        foreach (int index in new[] { 0, count / 2, count - 1, })
        {
            var resolution = new MemoryAccessContext { MaxRequests = 1, MaxTotalBytes = 1, };
            MemorySelection selected = session.Resolve(region, "record", fields[index].Name, resolution);
            Assert.AreSame(scalar, selected.Type);
            Assert.AreSame(fields[index], selected.Field);
            Assert.AreEqual("record", selected.ParentTypeId);
            Assert.AreEqual(region, selected.Container);
            Assert.AreSame(source, selected.Region.Source);
            Assert.AreEqual(100UL + (ulong)index, selected.Region.Address);
            Assert.AreEqual(1L, selected.Region.Length);
            Assert.AreEqual(0, resolution.Requests);
            Assert.AreEqual(0L, resolution.BytesRequested);
            Assert.AreEqual(0, trace.Count);

            var reading = new MemoryAccessContext { MaxRequests = 2, MaxTotalBytes = 1, };
            Assert.AreEqual(bytes[index], session.Read(region, "record", fields[index].Name, reading));
            Assert.AreEqual(2, reading.Requests);
            Assert.AreEqual(1L, reading.BytesRequested);
            CollectionAssert.AreEqual(new[] { "image.id", $"image.read:{100 + index}:1", "image.id", "image.id", }, trace);
            trace.Clear();
        }

        var missingContext = new MemoryAccessContext { MaxRequests = 1, MaxTotalBytes = 1, };

        // An absent member must finish the search without consulting the source or spending a read request.
        CStructPathException missing = Assert.Throws<CStructPathException>(() => session.Resolve(region, "record", "absent", missingContext));
        Assert.AreEqual("Member 'record.absent' is absent (path 'record.absent').", missing.Message);
        Assert.AreEqual("record.absent", missing.Path);
        Assert.AreEqual(0, missingContext.Requests);
        Assert.AreEqual(0L, missingContext.BytesRequested);
        Assert.AreEqual(0, trace.Count);
    }

    /// <summary>Promotion retains the actual containing record, and naming a promoted composite selects that composite.</summary>
    [TestMethod]
    public void PromotedMembers_KeepTheirImmediateContainer()
    {
        var scalar = new MemoryTypeDefinition("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        var child = new MemoryTypeDefinition("child", "child", MemoryTypeKind.Struct, 3, [new("value", "byte", 2),]);
        var root = new MemoryTypeDefinition("root", "root", MemoryTypeKind.Struct, 5, [new("branch", "child", 2, promoted: true),]);
        var session = new MemorySession(new MemorySchema([scalar, child, root,]));
        var trace = new List<string>();
        var source = new TracedSource("image", 100, new byte[5], trace);
        var region = new MemoryRegion(source, 100, 5);

        MemorySelection promoted = session.Resolve(region, "root", "value");
        Assert.AreSame(child.Fields[0], promoted.Field);
        Assert.AreSame(scalar, promoted.Type);
        Assert.AreEqual("child", promoted.ParentTypeId);
        Assert.AreEqual(new MemoryRegion(source, 102, 3), promoted.Container);
        Assert.AreEqual(new MemoryRegion(source, 104, 1), promoted.Region);

        MemorySelection direct = session.Resolve(region, "root", "branch", new MemoryAccessContext { MaxNestingDepth = 1, });
        Assert.AreSame(root.Fields[0], direct.Field);
        Assert.AreSame(child, direct.Type);
        Assert.AreEqual("root", direct.ParentTypeId);
        Assert.AreEqual(region, direct.Container);
        Assert.AreEqual(new MemoryRegion(source, 102, 3), direct.Region);
        Assert.AreEqual(0, trace.Count);
    }

    /// <summary>A successful direct match does not suppress a later promoted search's nesting failure or ambiguity.</summary>
    [TestMethod]
    public void PromotedMembers_ContinueCheckingAfterDirectMatch()
    {
        var scalar = new MemoryTypeDefinition("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        var unrelated = new MemoryTypeDefinition("unrelated", "unrelated", MemoryTypeKind.Struct, 1, [new("other", "byte", 0),]);
        var duplicate = new MemoryTypeDefinition("duplicate", "duplicate", MemoryTypeKind.Struct, 1, [new("value", "byte", 0),]);
        var trace = new List<string>();
        var region = new MemoryRegion(new TracedSource("image", 100, new byte[2], trace), 100, 2);
        foreach (MemoryTypeDefinition child in new[] { unrelated, duplicate, })
        {
            var root = new MemoryTypeDefinition("root", "root", MemoryTypeKind.Struct, 2, [new("value", "byte", 0), new("branch", child.Id, 1, promoted: true),]);
            var session = new MemorySession(new MemorySchema([scalar, child, root,]));
            var shallow = new MemoryAccessContext { MaxNestingDepth = 1, };

            // Even an already-matched name must visit the later promoted record and enforce its depth.
            MemoryAccessException depth = Assert.Throws<MemoryAccessException>(() => session.Resolve(region, "root", "value", shallow));
            Assert.AreEqual(MemoryFailure.BudgetExceeded, depth.Failure);
            Assert.AreEqual(CStructErrorCode.ReadLimitExceeded, depth.Code);
            Assert.AreEqual("Memory nesting depth exceeded (path 'root.value').", depth.Message);
            Assert.AreEqual("root.value", depth.Path);
            Assert.AreSame(region, depth.LogicalRegion);
            Assert.IsNull(depth.SourceId);
            Assert.IsNull(depth.Address);
            Assert.AreEqual(0, depth.Length);
            Assert.AreEqual(0, shallow.Requests);
            Assert.AreEqual(0L, shallow.BytesRequested);

            if (child == duplicate)
            {
                // With enough depth, the second declaration exposes the original ambiguity instead.
                CStructPathException ambiguous = Assert.Throws<CStructPathException>(() => session.Resolve(region, "root", "value", new MemoryAccessContext { MaxNestingDepth = 2, }));
                Assert.AreEqual("Ambiguous promoted member 'value' (path 'root.value').", ambiguous.Message);
                Assert.AreEqual("root.value", ambiguous.Path);
            }
            else
            {
                Assert.AreSame(root.Fields[0], session.Resolve(region, "root", "value", new MemoryAccessContext { MaxNestingDepth = 2, }).Field);
            }

            Assert.AreEqual(0, trace.Count);
        }
    }

    /// <summary>An unrelated empty member still overflows when its start would be one byte past the address space.</summary>
    /// <param name="emptyFirst">Whether the empty member is examined before the matching byte member.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EmptyMemberAtMaximumAddress_PreservesOverflow(bool emptyFirst)
    {
        var scalar = new MemoryTypeDefinition("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        var empty = new MemoryTypeDefinition("empty", "empty", MemoryTypeKind.Struct, 0);
        MemoryField[] fields = [new("value", "byte", 0), new("marker", "empty", 1),];
        if (emptyFirst)
        {
            Array.Reverse(fields);
        }

        var session = new MemorySession(new MemorySchema([scalar, empty, new("root", "root", MemoryTypeKind.Struct, 1, fields),]));
        var trace = new List<string>();
        var region = new MemoryRegion(new TracedSource("image", ulong.MaxValue, [42,], trace), ulong.MaxValue, 1);
        foreach (string path in new[] { "value", "absent", })
        {
            var context = new MemoryAccessContext();

            // Slice validates every member in declaration order, including an unused zero-length member.
            Assert.Throws<OverflowException>(() => session.Resolve(region, "root", path, context));
            Assert.AreEqual(0, context.Requests);
            Assert.AreEqual(0L, context.BytesRequested);
            Assert.AreEqual(0, trace.Count);
        }
    }

    /// <summary>The first failure in declaration order wins when an empty-member overflow competes with promotion ambiguity.</summary>
    /// <param name="overflowFirst">Whether the overflowing member is encountered before the ambiguous promotion.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EmptyMemberAndAmbiguity_PreserveFailureOrder(bool overflowFirst)
    {
        var scalar = new MemoryTypeDefinition("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        var empty = new MemoryTypeDefinition("empty", "empty", MemoryTypeKind.Struct, 0);
        var child = new MemoryTypeDefinition("child", "child", MemoryTypeKind.Struct, 1, [new("value", "byte", 0),]);
        var marker = new MemoryField("marker", "empty", 1);
        var promoted = new MemoryField("branch", "child", 0, promoted: true);
        MemoryField[] fields = [new("value", "byte", 0), overflowFirst ? marker : promoted, overflowFirst ? promoted : marker,];
        var session = new MemorySession(new MemorySchema([scalar, empty, child, new("root", "root", MemoryTypeKind.Union, 1, fields),]));
        var trace = new List<string>();
        var region = new MemoryRegion(new TracedSource("image", ulong.MaxValue, [42,], trace), ulong.MaxValue, 1);
        if (overflowFirst)
        {
            // The earlier invalid address must prevent reaching the later duplicate name.
            Assert.Throws<OverflowException>(() => session.Resolve(region, "root", "value"));
        }
        else
        {
            // An already-established ambiguity must prevent examining the later invalid address.
            CStructPathException ambiguous = Assert.Throws<CStructPathException>(() => session.Resolve(region, "root", "value"));
            Assert.AreEqual("Ambiguous promoted member 'value' (path 'root.value').", ambiguous.Message);
            Assert.AreEqual("root.value", ambiguous.Path);
        }

        Assert.AreEqual(0, trace.Count);
    }

    /// <summary>Following a promoted pointer preserves resolver coordinates, property accesses, source reads, and charges.</summary>
    [TestMethod]
    public void PromotedPointer_PreservesResolverAndSourceTrace()
    {
        var trace = new List<string>();
        var requests = new List<PointerRequest>();
        (MemorySession session, MemoryRegion region) = CreatePointerFixture(trace, requests);
        var context = new MemoryAccessContext { MaxRequests = 5, MaxTotalBytes = 9, };
        Assert.AreEqual((byte)42, session.Read(region, "root", "next.value.answer", context));
        CollectionAssert.AreEqual(PointerTrace(), trace);
        Assert.AreEqual(5, context.Requests);
        Assert.AreEqual(9L, context.BytesRequested);
        Assert.AreEqual(1, requests.Count);
        PointerRequest request = requests[0];
        Assert.AreEqual(new StoredPointer(0x205), request.Pointer);
        Assert.AreEqual(new MemoryRegion(region.Source, 103, 8), request.Storage);
        Assert.AreEqual(new MemoryRegion(region.Source, 102, 10), request.Container);
        Assert.AreEqual("target", request.TargetTypeId);
        Assert.AreEqual(4, request.TargetSize);
        Assert.AreEqual("next.value.answer", request.Path);
        Assert.AreEqual(2, request.Depth);
    }

    /// <summary>Request limits retain the exact completed trace and prevent the next source or resolver operation.</summary>
    [TestMethod]
    public void PromotedPointer_PreservesBudgetFailureBoundaries()
    {
        (int Limit, long Bytes, int TraceLength, string Source, ulong Address, int Length)[] cases =
        [
            (1, 0, 2, "storage", 103, 8),
            (2, 8, 5, "storage", 103, 0),
            (3, 8, 7, "target", 515, 0),
            (4, 8, 8, "target", 515, 1),
        ];
        foreach ((int limit, long bytes, int traceLength, string source, ulong address, int length) in cases)
        {
            var trace = new List<string>();
            var requests = new List<PointerRequest>();
            (MemorySession session, MemoryRegion region) = CreatePointerFixture(trace, requests);
            var context = new MemoryAccessContext { MaxRequests = limit, };

            // Each case stops at one successive charge in the same pointer-and-scalar read.
            MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => session.Read(region, "root", "next.value.answer", context));
            Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
            Assert.AreEqual($"{source}:0x{address:x} ({length} bytes): Memory operation budget exceeded (path 'root.next.value.answer').", failure.Message);
            Assert.AreEqual("root.next.value.answer", failure.Path);
            Assert.AreSame(region, failure.LogicalRegion);
            Assert.AreEqual(source, failure.SourceId);
            Assert.AreEqual(address, failure.Address);
            Assert.AreEqual(length, failure.Length);
            Assert.AreEqual(limit, context.Requests);
            Assert.AreEqual(bytes, context.BytesRequested);
            Assert.AreEqual(limit >= 3 ? 1 : 0, requests.Count);
            CollectionAssert.AreEqual(PointerTrace()[..traceLength], trace);
        }
    }

    /// <summary>Cancellation before resolution or inside its resolver preserves the completed prefix and token identity.</summary>
    [TestMethod]
    public void PromotedPointer_PreservesCancellationBoundaries()
    {
        foreach (bool cancelBefore in new[] { false, true, })
        {
            using var cancellation = new CancellationTokenSource();
            var trace = new List<string>();
            var requests = new List<PointerRequest>();
            (MemorySession session, MemoryRegion region) = CreatePointerFixture(trace, requests, cancellation.Cancel);
            if (cancelBefore)
            {
                cancellation.Cancel();
            }

            var context = new MemoryAccessContext { CancellationToken = cancellation.Token, };

            // The resolver cancellation is observed before the next member step and its scalar read.
            OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => session.Read(region, "root", "next.value.answer", context));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            Assert.AreEqual(cancelBefore ? 0 : 3, context.Requests);
            Assert.AreEqual(cancelBefore ? 0L : 8L, context.BytesRequested);
            Assert.AreEqual(cancelBefore ? 0 : 1, requests.Count);
            CollectionAssert.AreEqual(PointerTrace()[..(cancelBefore ? 0 : 6)], trace);
        }
    }

    /// <summary>Creates a promoted pointer whose resolver changes source and receives a nested container.</summary>
    /// <param name="trace">Receives source property accesses, reads, and resolver calls in order.</param>
    /// <param name="requests">Receives the unmodified requests delivered to the resolver.</param>
    /// <param name="afterResolve">Optional action performed inside the resolver after recording its request.</param>
    /// <returns>The session and its caller-owned root region.</returns>
    private static (MemorySession Session, MemoryRegion Region) CreatePointerFixture(List<string> trace, List<PointerRequest> requests, Action? afterResolve = null)
    {
        var schema = new MemorySchema(
        [
            new("byte", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8"),
            new("pointer", "pointer", MemoryTypeKind.Pointer, 8, elementTypeId: "target"),
            new("child", "child", MemoryTypeKind.Struct, 10, [new("padding", "byte", 0), new("next", "pointer", 1),]),
            new("root", "root", MemoryTypeKind.Struct, 12, [new("prefix", "byte", 0), new("branch", "child", 2, promoted: true),]),
            new("target", "target", MemoryTypeKind.Struct, 4, [new("prefix", "byte", 0), new("answer", "byte", 3),]),
        ]);
        var bytes = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(3, 8), 0x205);
        var storage = new TracedSource("storage", 100, bytes, trace);
        var target = new TracedSource("target", 0x200, [9, 0, 0, 42,], trace);

        // Record the resolver boundary and permit cancellation before returning a different source's target.
        var session = new MemorySession(schema, request =>
        {
            trace.Add("resolve");
            requests.Add(request);
            afterResolve?.Invoke();
            return new MemoryRegion(target, 0x200, 4);
        });
        return (session, new MemoryRegion(storage, 100, 12));
    }

    /// <summary>Returns the complete observable trace for the promoted pointer fixture's successful read.</summary>
    /// <returns>Source property accesses, reads, and resolver calls in their original order.</returns>
    private static string[] PointerTrace()
        => ["storage.id", "storage.read:103:8", "storage.id", "storage.id", "storage.id", "resolve", "target.id", "target.read:515:1", "target.id", "target.id",];

    /// <summary>A deterministic positional source that records every public property access and read attempt.</summary>
    private sealed class TracedSource : IMemorySource
    {
        private readonly string id;
        private readonly ulong address;
        private readonly byte[] bytes;
        private readonly List<string> trace;

        /// <summary>Creates a source over the supplied bytes without reading or accessing its observable properties.</summary>
        /// <param name="id">Source label used in traces and budget failures.</param>
        /// <param name="address">Unsigned address corresponding to the first supplied byte.</param>
        /// <param name="bytes">Deterministic bytes owned by the test fixture.</param>
        /// <param name="trace">Receives property accesses and read attempts in order.</param>
        internal TracedSource(string id, ulong address, byte[] bytes, List<string> trace)
        {
            this.id = id;
            this.address = address;
            this.bytes = bytes;
            this.trace = trace;
        }

        /// <summary>Gets the source label and records that the caller requested it.</summary>
        public string Id
        {
            get
            {
                this.trace.Add(this.id + ".id");
                return this.id;
            }
        }

        /// <summary>Gets the stable generation and records that the caller requested it.</summary>
        public long Generation
        {
            get
            {
                this.trace.Add(this.id + ".generation");
                return 0;
            }
        }

        /// <summary>Records and charges one read attempt, then copies the requested bytes from the fixture.</summary>
        /// <param name="address">Unsigned address of the first requested byte.</param>
        /// <param name="destination">Caller-owned destination to fill completely.</param>
        /// <param name="context">Budget and cancellation charged before copying bytes.</param>
        /// <returns>The number of bytes copied.</returns>
        /// <exception cref="MemoryAccessException">The request exceeds the operation budget.</exception>
        /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            this.trace.Add($"{this.id}.read:{address}:{destination.Length}");
            context.Charge(this.id, address, destination.Length);
            this.bytes.AsSpan(checked((int)(address - this.address)), destination.Length).CopyTo(destination);
            return destination.Length;
        }
    }
}
