namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Checks nested measurements preserve limits, captures and field assertions while resolving later fields, and that
///     a read-only query reports a failure to restore the source position.
/// </summary>
[TestClass]
public class AddressTraversalBoundaryTests
{
    /// <summary>A fixed-point prefix cannot be an integer dependency, so resolving past it need not read it.</summary>
    [TestMethod]
    public void FixedPointPrefix_DoesNotConsumeTheSelectedReadBudget()
    {
        var layout = new CStruct("struct root { fixed16_16 amount; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 0, 0, 1, 0, 99, });
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail", options: new ReadOptions { MaxTotalBytesRead = 1, }));
    }

    /// <summary>A fixed-point member cannot supply an array length: naming it in a count fails layout construction.</summary>
    [TestMethod]
    public void FixedPointCount_FailsConstruction()
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct("struct root { fixed16_16 amount; uint8 items[amount]; uint8 tail; };"));
        StringAssert.Contains(failure.Message, "Field 'amount' is a fixed-point value");
    }

    /// <summary>A nested enum publishes the qualified count used to find a later field.</summary>
    [TestMethod]
    public void NestedEnum_PublishesItsQualifiedCount()
    {
        var layout = new CStruct("enum count_type : uint8 { Two = 2 }; struct header { count_type count; }; struct root { header head; uint8 items[head.count]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 2, 11, 12, 99, });
        Assert.AreEqual(3L, layout.ResolveAddress(source, "root.tail"));
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail"));
    }

    /// <summary>A nested zero-width bitfield separator occupies no bytes to read.</summary>
    [TestMethod]
    public void NestedSeparator_DoesNotReadAStorageUnit()
    {
        var layout = new CStruct("struct empty { uint16 :0; }; struct root { empty prefix; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 99, });
        Assert.AreEqual(0L, layout.ResolveAddress(source, "root.tail"));
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail"));
    }

    /// <summary>Measuring a nested runtime-sized field still validates its declared offset.</summary>
    /// <param name="valid">Whether the runtime count places the marker at its asserted address three.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NestedMeasurement_EnforcesItsOffsetAssertion(bool valid)
    {
        var layout = new CStruct("struct item { uint8 count; uint8 data[count]; uint8 marker @3; }; struct root { item prefix; uint8 tail; };");
        using var source = new MemoryStream(valid ? new byte[] { 2, 11, 12, 9, 99, } : new byte[] { 1, 11, 9, 99, });
        if (valid)
        {
            Assert.AreEqual(4L, layout.ResolveAddress(source, "root.tail"));
        }
        else
        {
            Assert.Throws<CStructLayoutException>(() => layout.ResolveAddress(source, "root.tail"));
        }
    }

    /// <summary>Sibling measurements reuse their parent's nesting level instead of accumulating depth.</summary>
    [TestMethod]
    public void SiblingMeasurements_ReleaseTheirNestingDepth()
    {
        var layout = new CStruct("struct item { uint8 value; }; struct root { item first; item second; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 1, 2, 99, });
        var options = new ReadOptions { MaxNestingDepth = 2, };
        Assert.AreEqual(2L, layout.ResolveAddress(source, "root.tail", options: options));
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail", options: options));
    }

    /// <summary>An exposed memory source can report a physical failure when its position is synchronized.</summary>
    [TestMethod]
    public void AddressQuery_PropagatesFinalPositionFailure()
    {
        var layout = new CStruct("struct root { uint8 count; uint8 values[count]; uint8 tail; };");
        using var source = new RejectPositionStream(new byte[] { 99, 2, 5, 6, 7, });
        source.Position = 1;
        source.RejectPositionWrites = true;

        // Buffer borrowing avoids physical reads, but completing the query still synchronizes the owned cursor.
        IOException failure = Assert.Throws<IOException>(() => layout.ResolveAddress(source, "root.tail"));
        Assert.AreSame(source.Failure, failure);
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>Exposes its buffer while permitting a controlled failure of its virtual position setter.</summary>
    private sealed class RejectPositionStream : MemoryStream
    {
        /// <summary>Creates a read-only source whose backing storage is available to the reader.</summary>
        /// <param name="bytes">The source bytes, retained without copying.</param>
        public RejectPositionStream(byte[] bytes)
            : base(bytes, 0, bytes.Length, writable: false, publiclyVisible: true)
        {
        }

        /// <summary>Gets the exact physical failure used to verify exception identity.</summary>
        public IOException Failure { get; } = new("Cannot synchronize the source position.");

        /// <summary>Gets or sets whether subsequent position assignments fail.</summary>
        public bool RejectPositionWrites { get; set; }

        /// <summary>Reads the actual cursor or rejects an armed physical synchronization attempt.</summary>
        /// <exception cref="IOException">Position assignments fail once rejection is enabled.</exception>
        public override long Position
        {
            get => base.Position;
            set
            {
                if (this.RejectPositionWrites)
                {
                    throw this.Failure;
                }

                base.Position = value;
            }
        }
    }
}
