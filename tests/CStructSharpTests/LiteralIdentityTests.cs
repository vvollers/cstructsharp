namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Syntax;

/// <summary>Checks exact literal identity separately from the Int32 value used in layout expressions.</summary>
[TestClass]
public class LiteralIdentityTests
{
    /// <summary>Equality, hashing and display use the exact integer, not the traditional Int32 projection.</summary>
    [TestMethod]
    public void ExactValue_DeterminesIdentityAndDisplay()
    {
        BigInteger exact = BigInteger.One << 40;
        var first = new Literal(exact, 17);
        var same = new Literal(exact, 29);
        Assert.AreEqual(first, same);
        Assert.AreEqual(exact.GetHashCode(), first.GetHashCode());
        Assert.AreNotEqual(0, new Literal(17).GetHashCode());
        Assert.AreEqual($"Literal: {exact}", first.ToString());
        Assert.AreEqual(17, first.Value);
        Assert.AreEqual(29, same.Value);
    }

    /// <summary>Both Int32 endpoints are accepted and each immediately adjacent BigInteger is rejected.</summary>
    [TestMethod]
    public void Value_RejectsProjectionsOutsideTheInt32Range()
    {
        Assert.AreEqual(int.MinValue, new Literal(new BigInteger(int.MinValue)).Value);
        Assert.AreEqual(int.MaxValue, new Literal(new BigInteger(int.MaxValue)).Value);
        Assert.Throws<OverflowException>(() => new Literal(new BigInteger(int.MinValue) - 1).Value);
        Assert.Throws<OverflowException>(() => new Literal(new BigInteger(int.MaxValue) + 1).Value);
    }
}
