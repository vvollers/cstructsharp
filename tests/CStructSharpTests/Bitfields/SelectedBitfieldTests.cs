namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>
///     Checks reading a selected bitfield: extent arithmetic before any input is touched, the position of the shared
///     storage unit, and a pointer selection that must use its union target's bitfield storage.
/// </summary>
[TestClass]
public class SelectedBitfieldTests
{
    /// <summary>A root union validates its complete storage window before reading its overlapping views.</summary>
    [TestMethod]
    public void RootUnion_RejectsAnUnrepresentableEndBeforeReading()
    {
        var layout = new CStruct("union root { uint16 value; };");
        using var source = new NearLimitSource();

        // A bounded union window cannot extend beyond the signed stream-coordinate range.
        Assert.Throws<OverflowException>(() => layout.Parse(source, "root"));
        Assert.AreEqual(0, source.ReadCalls);
        Assert.AreEqual(long.MaxValue - 1, source.Position);
    }

    /// <summary>A two-byte union view cannot start one byte below the largest stream position.</summary>
    [TestMethod]
    public void SelectedBitfield_RejectsAnUnrepresentableEndBeforeReading()
    {
        var layout = new CStruct("union root { uint16 bits : 3; };");
        using var source = new NearLimitSource();

        // The layout extent, not an allocated buffer, crosses Int64.MaxValue.
        Assert.Throws<OverflowException>(() => layout.ReadValue(source, "root.bits"));
        Assert.AreEqual(0, source.ReadCalls);
        Assert.AreEqual(long.MaxValue - 1, source.Position);
    }

    /// <summary>Selected array positions and preceding field extents must fit in a signed stream coordinate.</summary>
    /// <param name="definition">The layout with a selected array or a field preceding the selection.</param>
    /// <param name="path">The selected address whose calculation crosses the coordinate limit.</param>
    [TestMethod]
    [DataRow("struct root { uint8 data[3]; };", "root.data[2]")]
    [DataRow("struct root { uint8 prefix[3]; uint8 tail; };", "root.tail")]
    [DataRow("union choice { uint16 value; }; struct root { choice prefix; uint8 tail; };", "root.tail")]
    public void AddressResolution_RejectsAnUnrepresentableExtentBeforeReading(string definition, string path)
    {
        var layout = new CStruct(definition);
        using var source = new NearLimitSource();

        // Address-only traversal must reject arithmetic overflow without attempting to read the synthetic input.
        Assert.Throws<OverflowException>(() => layout.ResolveAddress(source, path));
        Assert.AreEqual(0, source.ReadCalls);
        Assert.AreEqual(long.MaxValue - 1, source.Position);
    }

    /// <summary>A partial MSVC unit remains active, while a completely consumed unit advances to its end.</summary>
    /// <param name="declaration">The storage type and bit width.</param>
    /// <param name="expected">The selected integer value.</param>
    /// <param name="advance">Bytes advanced beyond the input origin.</param>
    [TestMethod]
    [DataRow("uint8 value:3;", 4, 0)]
    [DataRow("uint16 value:8;", 52, 0)]
    [DataRow("uint16 value:16;", 4660, 2)]
    public void SelectedBitfield_RetainsOrCompletesItsUnit(string declaration, int expected, int advance)
    {
        // MSVC packing retains the complete declared storage unit instead of shrinking a packed SysV window.
        var layout = new CStruct(
            "struct root { " + declaration + " };",
            compilationOptions: new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, });
        using var source = new MemoryStream(new byte[] { 0xAA, 0xBB, 0x34, 0x12, });
        source.Position = 2;

        Assert.AreEqual(expected, layout.ReadValue<int>(source, "root.value"));
        Assert.AreEqual(2L + advance, source.Position);
    }

    /// <summary>A selected pointer reads each union bitfield from the target's actual storage unit.</summary>
    /// <param name="rawFirst">Whether the ordinary byte view precedes the bitfield view.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedPointer_UsesTheUnionBitfieldStorage(bool rawFirst)
    {
        string members = rawFirst ? "uint8 raw; uint8 bits : 3;" : "uint8 bits : 3; uint8 raw;";
        var layout = new CStruct("union choice { " + members + " }; struct root { choice *item; };", pointerSize: 1);
        using var source = new MemoryStream(new byte[] { 1, 5, });
        var pointer = (Pointer)layout.ReadValue(source, "root.item")!;
        Assert.AreEqual(1L, pointer.Address);
        Assert.IsTrue(pointer.IsDereferenced);
        var target = (UnionValue)pointer.Value!;
        Assert.AreEqual(5, target.Get<int>("bits"));
        Assert.AreEqual((byte)5, target.Get<byte>("raw"));
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>Advertises a near-limit cursor and rejects physical reads that preflight should prevent.</summary>
    private sealed class NearLimitSource : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => long.MaxValue;

        public override long Position { get; set; } = long.MaxValue - 1;

        /// <summary>Gets the number of physical reads attempted.</summary>
        public int ReadCalls { get; private set; }

        /// <summary>Has no buffered writes to flush.</summary>
        public override void Flush()
        {
        }

        /// <summary>Routes array reads through the same physical-read rejection.</summary>
        /// <param name="buffer">The caller's destination.</param>
        /// <param name="offset">The destination start.</param>
        /// <param name="count">The requested byte count.</param>
        /// <returns>Never returns because physical reads are forbidden by this probe.</returns>
        /// <exception cref="IOException">Every valid read request is rejected.</exception>
        public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

        /// <summary>Records an unexpected physical read and fails it.</summary>
        /// <param name="buffer">The requested destination window.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="IOException">Every read fails so missing preflight cannot appear successful.</exception>
        public override int Read(Span<byte> buffer)
        {
            this.ReadCalls++;
            throw new IOException("A read started before validating the selected extent.");
        }

        /// <summary>Moves the synthetic cursor using checked stream-coordinate arithmetic.</summary>
        /// <param name="offset">The displacement in bytes.</param>
        /// <param name="origin">The displacement origin.</param>
        /// <returns>The new stream position.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The origin is unknown.</exception>
        /// <exception cref="OverflowException">The resulting position cannot fit in Int64.</exception>
        /// <exception cref="IOException">The resulting position is negative.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            long start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => this.Position,
                SeekOrigin.End => this.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            long position = checked(start + offset);
            if (position < 0)
            {
                throw new IOException("A stream position cannot be negative.");
            }

            this.Position = position;
            return this.Position;
        }

        /// <summary>Rejects resizing the read-only source.</summary>
        /// <param name="value">The unsupported length.</param>
        /// <exception cref="NotSupportedException">The source is read-only.</exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Rejects writes to the read-only source.</summary>
        /// <param name="buffer">The unsupported input bytes.</param>
        /// <param name="offset">The input start.</param>
        /// <param name="count">The input count.</param>
        /// <exception cref="NotSupportedException">The source is read-only.</exception>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
