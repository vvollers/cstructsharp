namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Syntax;

/// <summary>Checks that a literal is its exact value, and how a value beyond the 128-bit expression domain behaves.</summary>
[TestClass]
public class LiteralIdentityTests
{
    /// <summary>Equality, hashing and display use the exact integer, whichever constructor built the literal.</summary>
    [TestMethod]
    public void ExactValue_DeterminesIdentityAndDisplay()
    {
        BigInteger exact = BigInteger.One << 40;
        var first = new Literal(exact);
        var same = new Literal((Int128)1 << 40);
        Assert.AreEqual(first, same);
        Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(new Literal(17), first);
        Assert.AreEqual($"Literal: {exact}", first.ToString());
        Assert.AreEqual((Int128)1 << 40, first.Value);
        Assert.IsFalse(first.TryGetInt32(out _));
    }

    /// <summary>Both Int128 endpoints are in the domain; each adjacent integer is kept exactly but fails when used.</summary>
    [TestMethod]
    public void Value_RejectsLiteralsOutsideTheInt128Range()
    {
        Assert.AreEqual(Int128.MinValue, new Literal((BigInteger)Int128.MinValue).Value);
        Assert.AreEqual(Int128.MaxValue, new Literal((BigInteger)Int128.MaxValue).Value);

        var below = new Literal((BigInteger)Int128.MinValue - 1);
        var above = new Literal((BigInteger)Int128.MaxValue + 1);
        Assert.IsFalse(below.IsInDomain);
        Assert.AreEqual((BigInteger)Int128.MaxValue + 1, above.ExactValue);
        Assert.AreNotEqual(below, above);
        Assert.AreEqual(
            "The literal 170141183460469231731687303715884105728 is outside the 128-bit range that layout expressions support.",
            Assert.Throws<InvalidOperationException>(() => above.Value).Message);
        Assert.Throws<InvalidOperationException>(() => below.Value);
    }

    /// <summary>The Int32 view accepts exactly the Int32 range.</summary>
    [TestMethod]
    public void TryGetInt32_AcceptsExactlyTheInt32Range()
    {
        Assert.IsTrue(new Literal(int.MinValue).TryGetInt32(out int minimum));
        Assert.AreEqual(int.MinValue, minimum);
        Assert.IsTrue(new Literal(int.MaxValue).TryGetInt32(out int maximum));
        Assert.AreEqual(int.MaxValue, maximum);
        Assert.IsFalse(new Literal((Int128)int.MinValue - 1).TryGetInt32(out int belowResult));
        Assert.AreEqual(0, belowResult);
        Assert.IsFalse(new Literal((Int128)int.MaxValue + 1).TryGetInt32(out _));
        Assert.IsFalse(new Literal(BigInteger.One << 200).TryGetInt32(out _));
    }
}
