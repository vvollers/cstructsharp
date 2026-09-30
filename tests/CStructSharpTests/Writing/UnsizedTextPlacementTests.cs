namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>
///     Unsized wide text (<c>wchar name[]</c>) in an aligned layout is placed by the writer exactly where the reader
///     reads it - at the next multiple of its element's alignment - so what every write operation produces reads back,
///     and an <c>@N</c> assertion that holds for the layout holds for the write. Every write form is checked under every
///     execution path.
/// </summary>
[TestClass]
public class UnsizedTextPlacementTests
{
    /// <summary>A record whose unsized wide text follows an odd-length terminated string.</summary>
    private const string Layout = "struct rec { uint8 tag; char name[]; wchar< wide[]; uint16 after; };";

    /// <summary>
    ///     The aligned encoding of <see cref="Value"/>: <c>tag</c>, <c>name</c> "a" with its terminator, one byte of padding
    ///     so <c>wide</c> starts at offset 4, "xy" with its terminator, <c>after</c>, and no tail.
    /// </summary>
    private static readonly byte[] Encoded = [0x01, (byte)'a', 0x00, 0x00, (byte)'x', 0x00, (byte)'y', 0x00, 0x00, 0x00, 0x07, 0x00];

    /// <summary>Gets the execution paths every case runs under, as data rows.</summary>
    public static IEnumerable<object[]> Paths
        => from ExecutionPath path in (ExecutionPath[])[ExecutionPath.Fastest, ExecutionPath.NoDirectAccess, ExecutionPath.GeneralOnly]
           select new object[] { (int)path, };

    /// <summary>Gets the value <see cref="Encoded"/> holds.</summary>
    private static Dictionary<string, object?> Value => new() { ["tag"] = (byte)1, ["name"] = "a", ["wide"] = "xy", ["after"] = (ushort)7, };

    /// <summary>
    ///     <c>Serialize</c> to an array and to a span, <c>Write</c> and <c>WriteAsync</c> into a stream at a non-zero
    ///     position all produce the reader's placement, and the bytes read back as the value.
    /// </summary>
    /// <param name="path">The execution path, as its number (the enum is internal).</param>
    [TestMethod]
    [DynamicData(nameof(Paths))]
    public void EveryWrite_PlacesUnsizedWideTextAsTheReaderDoes(int path)
    {
        var layout = new CStruct(Layout, aligned: true);
        var options = new WriteOptions { ExecutionPath = (ExecutionPath)path, };

        CollectionAssert.AreEqual(Encoded, layout.Serialize("rec", Value, options: options));

        byte[] span = Enumerable.Repeat((byte)0xCC, Encoded.Length + 3).ToArray();
        Assert.AreEqual(Encoded.Length, layout.Serialize(span.AsSpan(), "rec", Value, options: options));
        CollectionAssert.AreEqual(Encoded, span[..Encoded.Length]);

        using var stream = new MemoryStream();
        stream.Write([0xAA, 0xAA]);
        layout.Write(stream, "rec", Value, options: options);
        CollectionAssert.AreEqual(Encoded, stream.ToArray()[2..]);

        using var asyncStream = new MemoryStream();
        layout.WriteAsync(asyncStream, "rec", Value, options: options).AsTask().GetAwaiter().GetResult();
        CollectionAssert.AreEqual(Encoded, asyncStream.ToArray());

        StructValue read = layout.Parse(Encoded.AsSpan(), "rec", options: new ReadOptions { ExecutionPath = (ExecutionPath)path, });
        Assert.AreEqual("xy", read["wide"]);
        Assert.AreEqual((ushort)7, read["after"]);
    }

    /// <summary>An update of the unsized wide text or of the field after it changes the bytes where the reader reads them.</summary>
    /// <param name="path">The execution path, as its number (the enum is internal).</param>
    [TestMethod]
    [DynamicData(nameof(Paths))]
    public void Update_ChangesUnsizedWideTextInPlace(int path)
    {
        var layout = new CStruct(Layout, aligned: true);
        var options = new UpdateOptions { ExecutionPath = (ExecutionPath)path, };
        byte[] data = (byte[])Encoded.Clone();
        layout.Update(data.AsSpan(), "rec.wide", "zq", options: options);
        layout.Update(data.AsSpan(), "rec.after", (ushort)9, options: options);
        CollectionAssert.AreEqual(new byte[] { 0x01, (byte)'a', 0x00, 0x00, (byte)'z', 0x00, (byte)'q', 0x00, 0x00, 0x00, 0x09, 0x00, }, data);
    }

    /// <summary>
    ///     An <c>@N</c> assertion on unsized wide text that the layout accepts holds when the member is written: the
    ///     writer places it at the asserted, aligned offset instead of failing.
    /// </summary>
    /// <param name="path">The execution path, as its number (the enum is internal).</param>
    [TestMethod]
    [DynamicData(nameof(Paths))]
    public void OffsetAssertion_HoldsWhenWritten(int path)
    {
        var layout = new CStruct("struct rec { uint8 tag; wchar< wide[] @2; };", aligned: true);
        var value = new Dictionary<string, object?> { ["tag"] = (byte)1, ["wide"] = "x", };
        byte[] written = layout.Serialize("rec", value, options: new WriteOptions { ExecutionPath = (ExecutionPath)path, });
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x00, (byte)'x', 0x00, 0x00, 0x00, }, written);
        Assert.AreEqual("x", layout.Parse(written.AsSpan(), "rec")["wide"]);
    }
}
