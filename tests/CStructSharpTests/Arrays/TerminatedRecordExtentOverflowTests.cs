namespace CStructSharp.Tests;

/// <summary>Checks a terminated record array's final storage extent remains a valid stream coordinate.</summary>
[TestClass]
public class TerminatedRecordExtentOverflowTests
{
    /// <summary>
    ///     Reading the array up to the last stream coordinate succeeds, and the field after it fails as truncated there:
    ///     the records keep their eight-byte layout wherever they start, so the terminator ends exactly at the end.
    /// </summary>
    [TestMethod]
    public void ReadAlignedTerminator_EndsAtTheLastCoordinate()
    {
        var layout = new CStruct("struct item { uint8 value @align(8); }; struct root { item items[] @align(1); uint8 tail; };", aligned: true);
        byte[] bytes = new byte[16];
        bytes[0] = 1;
        using var source = new HighOriginSource(bytes);
        CStructSharp.Diagnostics.CStructReadException failure = Assert.ThrowsExactly<CStructSharp.Diagnostics.CStructReadException>(() => layout.Parse(source, "root"));
        StringAssert.StartsWith(failure.Message, CStructSharp.Diagnostics.ReadFailures.ShortReadPrefix);
        Assert.AreEqual(long.MaxValue, failure.Offset);
    }

    /// <summary>
    ///     A field that starts at the last stream coordinate resolves there, and resolving past it fails as truncated
    ///     rather than wrapping: here <c>tail</c> starts at <see cref="long.MaxValue"/>, so <c>after</c> has no bytes.
    /// </summary>
    [TestMethod]
    public void AlignedTerminator_ResolvesUpToTheLastCoordinate()
    {
        var layout = new CStruct("struct item { uint8 value @align(8); }; struct root { item items[] @align(1); uint64 tail; uint8 after; };", aligned: true);
        byte[] bytes = new byte[16];
        bytes[0] = 1;
        using var source = new HighOriginSource(bytes);
        Assert.AreEqual(long.MaxValue, layout.ResolveAddress(source, "root.tail"));
        Assert.ThrowsExactly<CStructSharp.Diagnostics.CStructReadException>(() => layout.ResolveAddress(source, "root.after"));
        Assert.AreEqual(long.MaxValue - 16, source.Position);
    }

    /// <summary>Maps a small non-exposable buffer to the final sixteen valid stream coordinates.</summary>
    private sealed class HighOriginSource : MemoryStream
    {
        private const long Origin = long.MaxValue - 16;

        /// <summary>Creates a read-only source without allocating its advertised leading address range.</summary>
        /// <param name="bytes">The sixteen physical bytes at the synthetic origin.</param>
        public HighOriginSource(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        /// <summary>Gets the absolute end of the mapped bytes.</summary>
        public override long Length => checked(Origin + base.Length);

        /// <summary>Translates absolute caller positions to the small backing buffer's offset.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The requested position precedes the mapped buffer.</exception>
        public override long Position
        {
            get => checked(Origin + base.Position);
            set
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value, Origin);
                base.Position = value - Origin;
            }
        }
    }
}
