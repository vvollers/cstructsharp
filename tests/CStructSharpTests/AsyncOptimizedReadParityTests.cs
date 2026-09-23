namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks that ordinary async reads retain the corresponding ordinary synchronous decoding path.</summary>
[TestClass]
public class AsyncOptimizedReadParityTests
{
    /// <summary>Lowered nested alignment must not make an ordinary async wrapper silently select debug decoding.</summary>
    /// <param name="visible">Whether the async operation borrows the input buffer instead of copying it.</param>
    /// <returns>A task that completes after comparing decoded values and consumed byte positions.</returns>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoweredNestedAlignment_PreservesOrdinaryReadParity(bool visible)
    {
        var layout = new CStruct("struct item { uint8 first; uint16 second; }; struct root { uint8 prefix; item values[2] @align(1); uint8 tail; };", aligned: true);
        byte[] bytes = [9, 0xA1, 0xB2, 0xC3, 0xD4, 0xEE, 0x16, 0x27, 0x63, 0,];
        using var synchronous = new MemoryStream(bytes);
        using var asynchronous = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: visible);

        // Compare the public ordinary-read forms without declaring either placement algorithm the layout oracle.
        StructValue expected = layout.Parse(synchronous, "root");
        StructValue actual = await layout.ParseAsync(asynchronous, "root");
        Assert.AreEqual(expected.Get<byte>("prefix"), actual.Get<byte>("prefix"));
        Assert.AreEqual(expected.Get<byte>("values[0].first"), actual.Get<byte>("values[0].first"));
        Assert.AreEqual(expected.Get<ushort>("values[0].second"), actual.Get<ushort>("values[0].second"));
        Assert.AreEqual(expected.Get<byte>("values[1].first"), actual.Get<byte>("values[1].first"));
        Assert.AreEqual(expected.Get<ushort>("values[1].second"), actual.Get<ushort>("values[1].second"));
        Assert.AreEqual(expected.Get<byte>("tail"), actual.Get<byte>("tail"));
        Assert.AreEqual(synchronous.Position, asynchronous.Position);
    }
}
