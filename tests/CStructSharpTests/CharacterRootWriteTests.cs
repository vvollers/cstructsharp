namespace CStructSharp.Tests;

/// <summary>Checks text serialization through a root named directly by its character-array type.</summary>
[TestClass]
public class CharacterRootWriteTests
{
    /// <summary>Fixed buffers pad the remaining character, while unsized text appends its terminator.</summary>
    /// <param name="root">The fixed or terminated character-array spelling.</param>
    /// <param name="expectedHex">The exact encoded bytes, including padding or termination.</param>
    [TestMethod]
    [DataRow("char[3]", "616200")]
    [DataRow("wchar[3]", "610062000000")]
    [DataRow("char[]", "616200")]
    [DataRow("wchar[]", "610062000000")]
    public void CharacterArrayRoot_WritesTextAndItsTrailingZero(string root, string expectedHex)
    {
        var layout = new CStruct("struct unused { uint8 value; };");

        byte[] output = layout.Serialize(root, "ab");

        CollectionAssert.AreEqual(Convert.FromHexString(expectedHex), output);
    }
}
