namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Values;

/// <summary>Checks unsigned default storage when an enum or flag begins with a literal zero.</summary>
[TestClass]
public class ZeroEnumStorageTests
{
    /// <summary>Zero is not a negative literal and must not force signed default storage.</summary>
    /// <param name="keyword">The enum or flag declaration keyword.</param>
    [TestMethod]
    [DataRow("enum")]
    [DataRow("flag")]
    public void LiteralZero_PreservesUnsignedDefaultStorage(string keyword)
    {
        var layout = new CStruct(keyword + " kind { Zero=0 }; struct root { kind value; };");
        EnumValueResult value = layout.ReadValue<EnumValueResult>(new byte[] { 255, 255, 255, 255, }.AsSpan(), "root.value");
        Assert.IsFalse(value.IsSigned);
        Assert.AreEqual(32, value.BitWidth);
        Assert.AreEqual(new BigInteger(uint.MaxValue), value.Value);
    }
}
