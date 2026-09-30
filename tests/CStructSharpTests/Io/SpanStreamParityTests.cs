namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     The memory-backed read cursor serves span, array, and MemoryStream sources; every other stream keeps the
///     delegating path. A memory-backed operation reports its final position to the caller's stream. (That both paths
///     produce identical values, final positions and failures over every benchmark fixture is checked per fixture by
///     <see cref="EngineCorpusTests.BenchmarkFixture_MatchesItsExpectationOnEverySourceAndPath"/>.)
/// </summary>
[TestClass]
public class SpanStreamParityTests
{
    /// <summary>A memory-backed operation writes its final position back to the caller's stream, on success and on failure.</summary>
    [TestMethod]
    public void MemoryStreamPosition_IsWrittenBack_OnSuccessAndFailure()
    {
        var layout = new CStruct("struct root { uint16 a; uint32 b; uint8 tail[count]; }; #define count 3");
        using var success = new MemoryStream(new byte[] { 1, 0, 2, 0, 0, 0, 7, 8, 9, 0xFF }, writable: false);
        success.Position = 0;
        _ = layout.Parse(success, "root");
        Assert.AreEqual(9L, success.Position);

        using var failure = new MemoryStream(new byte[] { 1, 0, 2, 0, 0, 0, 7 }, writable: false);
        Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(failure, "root"));
        Assert.IsTrue(failure.Position >= 6, $"position after failure was {failure.Position}");

        using var resolve = new MemoryStream(new byte[] { 1, 0, 2, 0, 0, 0, 7, 8, 9 }, writable: false);
        resolve.Position = 0;
        Assert.AreEqual(6L, layout.ResolveAddress(resolve, "root.tail"));
        Assert.AreEqual(0L, resolve.Position, "ResolveAddress restores the position");
    }
}
