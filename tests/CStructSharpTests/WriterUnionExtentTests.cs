namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks union end-address arithmetic before the caller's output stream receives bytes.</summary>
[TestClass]
public class WriterUnionExtentTests
{
    /// <summary>A two-byte union cannot start one byte below the largest signed stream position.</summary>
    /// <param name="promoted">Whether the union is anonymous inside a root struct.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnionExtent_RejectsAddressOverflowBeforeOutput(bool promoted)
    {
        string definition = promoted
            ? "struct root { union { uint16 wide; uint8 small; }; };"
            : "union root { uint16 wide; uint8 small; };";
        var layout = new CStruct(definition);
        object data = promoted
            ? new Dictionary<string, object?> { ["small"] = (byte)7, }
            : UnionValue.FromMember("root", "small", (byte)7);
        using var destination = new NearLimitStream();

        // The union's end must be representable before its staged bytes reach the destination budget wrapper.
        Assert.Throws<OverflowException>(() => layout.Write(destination, "root", data));
        Assert.AreEqual(long.MaxValue - 1, destination.Position);
        Assert.AreEqual(0, destination.WriteCalls);
    }

    /// <summary>Exposes a large existing stream position without allocating the corresponding storage.</summary>
    private sealed class NearLimitStream : MemoryStream
    {
        /// <summary>Gets the simulated existing extent.</summary>
        public override long Length => long.MaxValue;

        /// <summary>Gets or sets the simulated absolute position.</summary>
        public override long Position { get; set; } = long.MaxValue - 1;

        /// <summary>Gets the number of physical array writes attempted by the union writer.</summary>
        public int WriteCalls { get; private set; }

        /// <summary>Records a physical write without allocating or changing the simulated storage.</summary>
        /// <param name="buffer">The staged union bytes.</param>
        /// <param name="offset">The first staged byte.</param>
        /// <param name="count">The number of staged bytes.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteCalls++;
        }
    }
}
