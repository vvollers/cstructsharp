namespace CStructSharp.Tests;

/// <summary>Checks a union whose largest member does not end on the union's alignment boundary.</summary>
[TestClass]
public class CompiledUnionTailTests
{
    /// <summary>Only aligned layouts round the union's three-byte largest member up to a two-byte boundary.</summary>
    /// <param name="aligned">Whether natural alignment and tail padding are enabled.</param>
    /// <param name="expectedSize">The resulting union extent in bytes.</param>
    [TestMethod]
    [DataRow(false, 3)]
    [DataRow(true, 4)]
    public void UnionTail_RoundsTheLargestMemberOnlyWhenAligned(bool aligned, int expectedSize)
    {
        var layout = new CStruct("union choice { uint8 bytes[3]; uint16 number; };", aligned: aligned);

        Assert.AreEqual(expectedSize, layout.GetStructSizeInBytes("choice"));
        Assert.AreEqual(expectedSize, layout.CompiledModel.Symbols["choice"].Symbol.FixedSize);
    }
}
