namespace CStructSharp.Tests;

using CStructSharp.Syntax;

/// <summary>The value semantics of the byte-string syntax nodes: equality, hashing, the hidden value's name and the readable forms.</summary>
[TestClass]
public class ByteStringSyntaxTests
{
    /// <summary>Two byte strings are equal when their bytes are, and their hashes agree.</summary>
    [TestMethod]
    public void BytesLiteral_EqualityHashAndText()
    {
        var first = new BytesLiteral(new byte[] { 0x7F, 0x45 });
        var same = new BytesLiteral(new byte[] { 0x7F, 0x45 });
        var other = new BytesLiteral(new byte[] { 0x7F, 0x46 });

        Assert.IsTrue(first.Equals(same));
        Assert.IsFalse(first.Equals(other));
        Assert.IsFalse(first.Equals(new Literal(1)));
        Assert.IsFalse(first.Equals(null));
        Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(first.GetHashCode(), other.GetHashCode());
        Assert.AreEqual("7F45", BytesLiteral.ToHex(first.Bytes));
        Assert.AreEqual("Bytes: 7F45", first.ToString());
    }

    /// <summary>A comparison is equal to another with the same field, bytes and operator, and its slot name spells the bytes.</summary>
    [TestMethod]
    public void ByteEquals_EqualityHashSlotAndLowering()
    {
        byte[] bytes = { 0x4E, 0x54 };
        var equal = new ByteEquals("oem", bytes, negated: false);

        Assert.IsTrue(equal.Equals(new ByteEquals("oem", new byte[] { 0x4E, 0x54 }, negated: false)));
        Assert.IsFalse(equal.Equals(new ByteEquals("oem", bytes, negated: true)));
        Assert.IsFalse(equal.Equals(new ByteEquals("sig", bytes, negated: false)));
        Assert.IsFalse(equal.Equals(new ByteEquals("oem", new byte[] { 0x4E, 0x55 }, negated: false)));
        Assert.IsFalse(equal.Equals(new Literal(1)));
        Assert.IsFalse(equal.Equals(null));
        Assert.AreEqual(equal.GetHashCode(), new ByteEquals("oem", new byte[] { 0x4E, 0x54 }, negated: false).GetHashCode());
        Assert.AreEqual("oem#4E54", equal.SlotName);
        Assert.AreEqual("oem == 4E54", equal.ToString());
        Assert.AreEqual("oem != 4E54", new ByteEquals("oem", bytes, negated: true).ToString());
        Assert.AreEqual(new Identifier("oem#4E54"), equal.Lowered());
        Assert.IsInstanceOfType<UnaryOp>(new ByteEquals("oem", bytes, negated: true).Lowered());
    }
}
