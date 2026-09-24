namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Boundary tables for explicit metadata, source extents, work budgets, and ownership.</summary>
[TestClass]
public class MemoryValidationTests
{
    /// <summary>Budget counters charge exact limits, reject the next request, and preserve structured failure context.</summary>
    [TestMethod]
    public void Budgets_UseInclusiveLimitsAndStructuredErrors()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryAccessContext(maxBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryAccessContext(maxRequests: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryAccessContext(maxDepth: 0));
        var context = new MemoryAccessContext(maxBytes: 5, maxRequests: 2, maxDepth: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Charge("s", 1, -1));
        context.Charge("s", 9, 2);
        context.Charge("s", 9, 3);
        Assert.AreEqual(5L, context.BytesRequested);
        Assert.AreEqual(2, context.Requests);
        MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => context.Charge("s", 9, 0));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
        Assert.AreEqual("s", failure.SourceId);
        Assert.AreEqual(9UL, failure.Address);
        Assert.AreEqual(0, failure.Length);
        StringAssert.Contains(failure.Message, "budget");
        var byteLimit = new MemoryAccessContext(maxBytes: 3);
        byteLimit.Charge("s", 0, 2);
        Assert.Throws<MemoryAccessException>(() => byteLimit.Charge("s", 0, 2));
    }

    /// <summary>Nested mapping depth is restored after failure, allowing a later shallow request with the same budget.</summary>
    [TestMethod]
    public void Mappings_BoundRecursiveDepth()
    {
        var source = new ByteArrayMemorySource("image", new byte[] { 7, });
        var one = new MappedMemorySource("one", [new(1, new MemoryRegion(source, 0, 1)),]);
        var two = new MappedMemorySource("two", [new(2, new MemoryRegion(one, 1, 1)),]);
        var context = new MemoryAccessContext(maxDepth: 1);
        byte[] buffer = new byte[1];
        MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => two.Read(2, buffer, context));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
        StringAssert.Contains(failure.Message, "depth");
        Assert.AreEqual(1, one.Read(1, buffer, context));
        Assert.AreEqual(7, buffer[0]);
    }

    /// <summary>Source copies are independent; finite slices accept empty endpoints but reject overflow and negative extents.</summary>
    [TestMethod]
    public void RegionsAndImages_PreserveOwnershipAndBounds()
    {
        byte[] original = [1, 2, 3,];
        var source = new ByteArrayMemorySource("image", original);
        original[0] = 99;
        byte[] copy = source.ToArray();
        copy[1] = 99;
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, source.ToArray());
        Assert.AreEqual(3, source.Length);
        var region = new MemoryRegion(source, 0, 3);
        Assert.AreEqual(3UL, region.Slice(3, 0).Address);
        Assert.AreEqual(0L, region.Slice(3, 0).Length);
        Assert.Throws<ArgumentNullException>(() => new MemoryRegion(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRegion(source, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => region.Slice(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => region.Slice(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => region.Slice(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => region.Slice(2, 2));
        var maximum = new MemoryRegion(source, ulong.MaxValue, 1);
        Assert.Throws<OverflowException>(() => maximum.Slice(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryMapping(ulong.MaxValue, region));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryMapping(0, region.Slice(0, 0)));
        byte[] destination = new byte[5];
        Assert.AreEqual(2, source.Read(1, destination, new MemoryAccessContext()));
        CollectionAssert.AreEqual(new byte[] { 2, 3, 0, 0, 0, }, destination);
        Assert.AreEqual(0, source.Read(3, destination, new MemoryAccessContext()));
        source.Write(3, ReadOnlySpan<byte>.Empty, new MemoryAccessContext());
        Assert.Throws<MemoryAccessException>(() => source.Write(4, ReadOnlySpan<byte>.Empty, new MemoryAccessContext()));
    }

    /// <summary>Malformed descriptor graphs are rejected before any source I/O.</summary>
    [TestMethod]
    public void Schema_RejectsMalformedShapes()
    {
        var scalar = new MemoryTypeDefinition("u", "byte", MemoryTypeKind.Scalar, 1, scalarType: "uint8");
        MemoryTypeDefinition[] invalid = [
            new("bad", "bad", (MemoryTypeKind)999, 0),
            new("bad", "bad", MemoryTypeKind.Scalar, 2, scalarType: "uint8"),
            new("bad", "bad", MemoryTypeKind.Pointer, 8, elementTypeId: "missing"),
            new("bad", "bad", MemoryTypeKind.Array, 2, elementTypeId: "u", count: 3),
            new("bad", "bad", MemoryTypeKind.Incomplete, 1),
            new("bad", "bad", MemoryTypeKind.Incomplete, 0, [new("x", "u", 0),]),
            new("bad", "bad", MemoryTypeKind.Struct, 1, [new("x", "u", 1),]),
            new("bad", "bad", MemoryTypeKind.Struct, 1, [new("x", "u", 2),]),
            new("bad", "bad", MemoryTypeKind.Struct, 1, [new(string.Empty, "u", 0),]),
            new("bad", "bad", MemoryTypeKind.Struct, 2, [new("x", "u", 0), new("x", "u", 1),]),
            new("bad", "bad", MemoryTypeKind.Struct, 1, [new("x", "u", 0), new("y", "u", 0),]),
            new("bad", "bad", MemoryTypeKind.Struct, 1, [new("x", "u", 0, 1, 8),]),
            new("bad", "bad", MemoryTypeKind.Struct, 1, [new("x", "u", 0, promoted: true),]),
            new("bad", "bad", MemoryTypeKind.Scalar, 1, [new("x", "u", 0),], scalarType: "uint8"),
        ];
        foreach (MemoryTypeDefinition bad in invalid)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() => new MemorySchema([scalar, bad,]));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message));
        }

        Assert.Throws<ArgumentNullException>(() => new MemorySchema(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemorySchema([], maxTypes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemorySchema([], maxFields: 0));
        Assert.Throws<ArgumentException>(() => new MemorySchema([scalar, scalar,]));
        Assert.Throws<ArgumentException>(() => new MemorySchema([scalar, new("s", "s", MemoryTypeKind.Struct, 0),], maxTypes: 1));
        var schema = new MemorySchema([scalar, new("s", "s", MemoryTypeKind.Struct, 1, [new("x", "u", 0),]),], maxTypes: 2, maxFields: 1);
        Assert.AreEqual("byte", schema.GetType("u").Name);
        StringAssert.Contains(Assert.Throws<ArgumentException>(() => schema.GetType("missing")).Message, "missing");
        StringAssert.Contains(Assert.Throws<ArgumentException>(() => schema.GetField("s", "missing")).Message, "missing");
        Assert.Throws<ArgumentException>(() => new MemorySchema([scalar, new("s", "s", MemoryTypeKind.Struct, 2, [new("x", "u", 0), new("y", "u", 1),]),], maxFields: 1));
    }

    /// <summary>
    /// An array of a zero-size element validates when the array is also zero bytes wide - the shape of a
    /// real kernel marker struct with no members (for example Linux's <c>lock_class_key</c>, used only for
    /// its address, never its contents) and an array of them, such as a lockdep annotation declares. A
    /// zero-size element paired with a mismatched, nonzero array size is still rejected, the same as any
    /// other size disagreement: this behavior narrows what used to be an outright ban on zero-size
    /// elements, it does not remove the size-consistency check itself.
    /// </summary>
    [TestMethod]
    public void Arrays_AllowZeroSizeElementsOnlyWhenTheWholeArrayIsAlsoZeroSized()
    {
        var empty = new MemoryTypeDefinition("empty", "empty", MemoryTypeKind.Struct, 0);
        var marker = new MemoryTypeDefinition("marker", "marker", MemoryTypeKind.Array, 0, elementTypeId: "empty", count: 3);
        var schema = new MemorySchema([empty, marker,]);
        Assert.AreEqual(0, schema.GetType("marker").Size);

        var mismatched = new MemoryTypeDefinition("bad", "bad", MemoryTypeKind.Array, 3, elementTypeId: "empty", count: 3);
        StringAssert.Contains(Assert.Throws<ArgumentException>(() => new MemorySchema([empty, mismatched,])).Message, "extent");
    }

    /// <summary>Bit descriptions require paired, positive bounds and source metadata remains immutable.</summary>
    [TestMethod]
    public void Descriptors_ValidateAndSnapshot()
    {
        Assert.Throws<ArgumentException>(() => new MemoryField("x", "u", 0, 0));
        Assert.Throws<ArgumentException>(() => new MemoryField("x", "u", 0, bitWidth: 1));
        Assert.Throws<ArgumentException>(() => new MemoryField("x", "u", 0, -1, 1));
        Assert.Throws<ArgumentException>(() => new MemoryField("x", "u", 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryField("x", "u", -1));
        Assert.Throws<ArgumentException>(() => new MemoryField("x", string.Empty, 0));
        Assert.Throws<ArgumentNullException>(() => new MemoryField(null!, "u", 0));
        Assert.Throws<ArgumentException>(() => new MemoryTypeDefinition(string.Empty, "n", MemoryTypeKind.Struct, 0));
        Assert.Throws<ArgumentNullException>(() => new MemoryTypeDefinition("x", null!, MemoryTypeKind.Struct, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryTypeDefinition("x", "x", MemoryTypeKind.Struct, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryTypeDefinition("x", "x", MemoryTypeKind.Array, 0, count: -1));
        var fields = new List<MemoryField> { new("x", "u", 0), };
        var type = new MemoryTypeDefinition("s", "Record", MemoryTypeKind.Struct, 4, fields, provenance: "independent fixture");
        fields.Clear();
        Assert.AreEqual(1, type.Fields.Count);
        Assert.AreEqual("independent fixture", type.Provenance);
        Assert.AreEqual("Record", type.Name);
    }

    /// <summary>Read views enforce stream capability, disposal, and local seek contracts without owning backing bytes.</summary>
    [TestMethod]
    public void StreamView_EnforcesCapabilitiesAndDisposal()
    {
        using var file = new MemoryStream(new byte[] { 1, 2, });
        var source = new StreamMemorySource("file", file, generation: 7);
        Assert.AreEqual(7L, source.Generation);
        Stream view = new MemoryRegion(source, 0, 2).OpenRead();
        Assert.IsTrue(view.CanRead && view.CanSeek && !view.CanWrite);
        Assert.AreEqual(2L, view.Length);
        view.Flush();
        Assert.Throws<NotSupportedException>(() => view.SetLength(1));
        Assert.Throws<NotSupportedException>(() => view.Write(new byte[1], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Seek(0, (SeekOrigin)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Position = -1);
        byte[] bytes = new byte[2];
        Assert.AreEqual(2, view.Read(bytes, 0, 2));
        Assert.AreEqual(0, view.Read(bytes, 0, 2));
        view.Dispose();
        Assert.IsFalse(view.CanRead || view.CanSeek || view.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => view.Read(bytes, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => view.Flush());
        Assert.Throws<ObjectDisposedException>(() => view.Position = 0);
        Assert.IsTrue(file.CanRead);
        Assert.Throws<MemoryAccessException>(() => source.Read(ulong.MaxValue, bytes, new MemoryAccessContext()));
    }
}
