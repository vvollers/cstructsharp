namespace CStructSharp.Tests;

/// <summary>Checks a bounded character update does not decode unrelated later fields.</summary>
[TestClass]
public class FixedCharacterUpdateBoundaryTests
{
    /// <summary>A complete fixed character field can be replaced even when later root fields are unavailable.</summary>
    [TestMethod]
    public void FixedCharacterUpdate_DoesNotRequireTheFollowingField()
    {
        var layout = new CStruct("struct root { char name[2]; uint32 tail; };");
        byte[] bytes = [65, 66,];
        using var source = new MemoryStream(bytes);

        layout.Update(source, "root.name", "CD");

        CollectionAssert.AreEqual(new byte[] { 67, 68, }, bytes);
        Assert.AreEqual(2L, source.Length);
    }
}
