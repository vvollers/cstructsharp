namespace CStructSharp.Tests;

/// <summary>Checks a terminated record array's final storage extent remains a valid stream coordinate.</summary>
[TestClass]
public class TerminatedRecordExtentOverflowTests
{
    /// <summary>Absolute member alignment can leave insufficient coordinate space for the final terminator.</summary>
    [TestMethod]
    public void AlignedTerminator_RejectsAnUnrepresentableEnd()
    {
        var layout = new CStruct("struct item { uint8 value @align(8); }; struct root { item items[] @align(1); uint8 tail; };", aligned: true);
        byte[] bytes = new byte[16];
        bytes[0] = 1;
        using var source = new HighOriginSource(bytes);
        Assert.Throws<OverflowException>(() => layout.ResolveAddress(source, "root.tail"));
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
        public override long Position
        {
            get => checked(Origin + base.Position);
            set => base.Position = checked(value - Origin);
        }
    }
}
